using System.Buffers;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
{
    private void EmitClassDeclaration(JsAst ast, in AstNode node)
    {
        var info = ast.GetClass(node.Arg0);
        EmitClassExpression(ast, node.Arg0);
        var name = ast.GetString(info.NameStringIndex);
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException($"No planned class binding found for '{name}'.");
        EmitStore(binding, isInitialization: true);
    }

    private void EmitClassExpression(
        JsAst ast,
        int classIndex,
        string? inferredName = null,
        int inferredNameRegister = -1
    )
    {
        var info = ast.GetClass(classIndex);

        var classScope = FindChildScope(
            activeScopes.Peek().ScopeId,
            CompilerCollectedScopeKind.Class,
            info.Position
        );
        EnterScope(classScope.ScopeId);
        var privateBindingsBeforeClass = visiblePrivateBindings;
        var exactPrivateBrandSourcesBeforeClass = activeExactPrivateBrandSources;
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var declaredName = ast.GetString(info.NameStringIndex);
            var constructorName =
                declaredName.Length == 0 ? inferredName ?? string.Empty : declaredName;
            var heritageRegister = -1;
            if (info.HasExtends)
            {
                EmitExpression(ast, info.ExtendsNode);
                heritageRegister = builder.AllocateTemporaryRegister();
                EmitStar(heritageRegister);
            }

            var elements = ast.GetClassElements(info);
            visiblePrivateBindings = BuildClassPrivateBindings(
                ast,
                elements,
                out var instancePrivateBrandId,
                out var staticPrivateBrandId
            );

            ref readonly var constructor = ref ast[info.ConstructorNode];
            EmitFunctionExpression(
                ast,
                constructor.Arg0,
                constructor.Arg1,
                constructorName.Length == 0 ? null : constructorName,
                classIndex
            );
            var constructorRegister = builder.AllocateTemporaryRegister();
            EmitStar(constructorRegister);

            if (heritageRegister >= 0)
            {
                var heritageArguments = builder.AllocateTemporaryRegisterBlock(2);
                EmitMove(constructorRegister, heritageArguments);
                EmitMove(heritageRegister, heritageArguments + 1);
                builder.EmitCallRuntime((int)RuntimeId.SetClassHeritage, heritageArguments, 2);
            }

            builder.EmitCallRuntime(
                (int)RuntimeId.ClassGetPrototypeAndSetConstructor,
                constructorRegister,
                1
            );
            var prototypeRegister = builder.AllocateTemporaryRegister();
            EmitStar(prototypeRegister);
            var ownPrivateBrandSources = new List<PrivateBrandSource>(2);
            if (instancePrivateBrandId != 0)
                ownPrivateBrandSources.Add(new(instancePrivateBrandId, prototypeRegister));
            if (staticPrivateBrandId != 0)
                ownPrivateBrandSources.Add(new(staticPrivateBrandId, constructorRegister));
            if (ownPrivateBrandSources.Count != 0)
            {
                EmitExactPrivateBrandMappings(constructorRegister, ownPrivateBrandSources);
                var combined = new List<PrivateBrandSource>(
                    exactPrivateBrandSourcesBeforeClass.Count + ownPrivateBrandSources.Count
                );
                combined.AddRange(exactPrivateBrandSourcesBeforeClass);
                combined.AddRange(ownPrivateBrandSources);
                activeExactPrivateBrandSources = combined;
            }
            EmitLdar(constructorRegister);
            EmitAttachMethodEnvironmentIfNeeded(ast, info.ConstructorNode, prototypeRegister);

            var staticFieldKeyRegisters = ArrayPool<int>.Shared.Rent(Math.Max(1, elements.Length));
            Array.Fill(staticFieldKeyRegisters, -1, 0, elements.Length);
            Dictionary<string, PrivateAccessorRegisters>? staticPrivateAccessors = null;
            try
            {
                for (var i = 0; i < elements.Length; i++)
                {
                    ref readonly var element = ref elements[i];
                    if (element.Kind == JsClassElementKind.Constructor)
                        continue;
                    if (element.Kind == JsClassElementKind.StaticBlock)
                        continue;
                    if (element.IsPrivate)
                    {
                        if (element.Kind == JsClassElementKind.Field)
                            continue;
                        if (element.Kind == JsClassElementKind.Method)
                            EmitPreparePrivateMethod(
                                ast,
                                element,
                                constructorRegister,
                                prototypeRegister
                            );
                        else
                            EmitPreparePrivateAccessor(
                                ast,
                                element,
                                constructorRegister,
                                prototypeRegister,
                                ref staticPrivateAccessors
                            );
                        continue;
                    }
                    if (element.Kind == JsClassElementKind.Field)
                    {
                        if (!element.IsStatic)
                        {
                            if (element.IsComputed)
                                EmitCacheInstanceFieldKey(ast, element, constructorRegister);
                            continue;
                        }
                        var keyRegister = builder.AllocateTemporaryRegister();
                        EmitClassElementKey(ast, element, keyRegister);
                        staticFieldKeyRegisters[i] = keyRegister;
                        continue;
                    }
                    EmitClassElement(ast, element, constructorRegister, prototypeRegister);
                }
                if (staticPrivateAccessors is not null)
                    foreach (var accessor in staticPrivateAccessors.Values)
                        EmitPrivateAccessorInitialization(
                            constructorRegister,
                            accessor.GetterRegister,
                            accessor.SetterRegister,
                            accessor.Binding
                        );

                if (declaredName.Length != 0)
                {
                    if (!TryResolveBinding(declaredName, out var classAlias))
                        throw new InvalidOperationException(
                            $"No planned class lexical binding found for '{declaredName}'."
                        );
                    EmitLdar(constructorRegister);
                    EmitStore(classAlias, isInitialization: true);
                }
                else if (inferredNameRegister >= 0)
                {
                    EmitLdar(constructorRegister);
                    EmitSetFunctionName(inferredNameRegister);
                }

                for (var i = 0; i < elements.Length; i++)
                    if (elements[i].Kind == JsClassElementKind.Field && elements[i].IsPrivate)
                    {
                        if (elements[i].IsStatic)
                            EmitStaticPrivateFieldInitializer(
                                ast,
                                elements[i],
                                constructorRegister
                            );
                    }
                    else if (elements[i].Kind == JsClassElementKind.StaticBlock)
                        EmitClassStaticBlock(ast, elements[i], constructorRegister);
                    else if (staticFieldKeyRegisters[i] >= 0)
                        EmitStaticClassFieldInitializer(
                            ast,
                            elements[i],
                            constructorRegister,
                            staticFieldKeyRegisters[i]
                        );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(staticFieldKeyRegisters);
            }

            EmitLdar(constructorRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
            visiblePrivateBindings = privateBindingsBeforeClass;
            activeExactPrivateBrandSources = exactPrivateBrandSourcesBeforeClass;
            LeaveScope();
        }
    }

    private void EmitPreparePrivateMethod(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister,
        int prototypeRegister
    )
    {
        var binding = ResolvePrivateBinding(ast.GetString(element.Key));
        var homeObjectRegister = element.IsStatic ? constructorRegister : prototypeRegister;
        EmitClassElementFunction(ast, element, homeObjectRegister);
        if (element.IsStatic)
        {
            var methodRegister = builder.AllocateTemporaryRegister();
            EmitStar(methodRegister);
            EmitPrivateFieldOp(
                JsOpCode.InitPrivateMethod,
                constructorRegister,
                methodRegister,
                binding
            );
            return;
        }
        EmitSetFunctionPrivateMethodValue(constructorRegister, binding.SlotIndex * 2);
    }

    private void EmitPreparePrivateAccessor(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister,
        int prototypeRegister,
        ref Dictionary<string, PrivateAccessorRegisters>? staticAccessors
    )
    {
        var name = ast.GetString(element.Key);
        var binding = ResolvePrivateBinding(name);
        var homeObjectRegister = element.IsStatic ? constructorRegister : prototypeRegister;
        EmitClassElementFunction(ast, element, homeObjectRegister);
        if (!element.IsStatic)
        {
            EmitSetFunctionPrivateMethodValue(
                constructorRegister,
                binding.SlotIndex * 2 + (element.Kind == JsClassElementKind.Getter ? 0 : 1)
            );
            return;
        }

        var functionRegister = builder.AllocateTemporaryRegister();
        EmitStar(functionRegister);
        staticAccessors ??= new(StringComparer.Ordinal);
        staticAccessors.TryGetValue(name, out var accessor);
        accessor = accessor.Binding.BrandId == 0 ? new(-1, -1, binding) : accessor;
        if (element.Kind == JsClassElementKind.Getter)
            accessor = accessor with { GetterRegister = functionRegister };
        else
            accessor = accessor with { SetterRegister = functionRegister };
        staticAccessors[name] = accessor;
    }

    private void EmitSetFunctionPrivateMethodValue(int constructorRegister, int valueIndex)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitStar(arguments + 2);
            EmitLdar(constructorRegister);
            EmitStar(arguments);
            EmitSmi(valueIndex);
            EmitStar(arguments + 1);
            builder.EmitCallRuntime((int)RuntimeId.SetFunctionPrivateMethodValue, arguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private readonly record struct PrivateAccessorRegisters(
        int GetterRegister,
        int SetterRegister,
        PlannedPrivateBinding Binding
    );

    private IReadOnlyDictionary<string, PlannedPrivateBinding> BuildClassPrivateBindings(
        JsAst ast,
        ReadOnlySpan<JsClassElement> elements,
        out int instanceBrandId,
        out int staticBrandId
    )
    {
        var bindings = new Dictionary<string, PlannedPrivateBinding>(
            visiblePrivateBindings,
            StringComparer.Ordinal
        );
        var ownBindings = new Dictionary<string, PlannedPrivateBinding>(StringComparer.Ordinal);
        Dictionary<string, byte>? accessorMasks = null;
        instanceBrandId = 0;
        staticBrandId = 0;
        var nextSlot = 0;
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var element = ref elements[i];
            if (!element.IsPrivate)
                continue;
            var name = ast.GetString(element.Key);
            var kind = element.Kind switch
            {
                JsClassElementKind.Field => PlannedPrivateMemberKind.Field,
                JsClassElementKind.Method => PlannedPrivateMemberKind.Method,
                JsClassElementKind.Getter or JsClassElementKind.Setter =>
                    PlannedPrivateMemberKind.Accessor,
                _ => throw new NotSupportedException(
                    $"{CompilerName} does not support private class element '{element.Kind}' yet."
                ),
            };
            if (ownBindings.TryGetValue(name, out var existing))
            {
                var accessorBit = element.Kind == JsClassElementKind.Getter ? 1 : 2;
                accessorMasks ??= new(StringComparer.Ordinal);
                accessorMasks.TryGetValue(name, out var mask);
                if (
                    kind != PlannedPrivateMemberKind.Accessor
                    || existing.Kind != PlannedPrivateMemberKind.Accessor
                    || existing.IsStatic != element.IsStatic
                    || (mask & accessorBit) != 0
                )
                    throw new InvalidOperationException(
                        $"Duplicate private class member '{name}'."
                    );
                accessorMasks[name] = (byte)(mask | accessorBit);
                continue;
            }
            ref var brandId = ref (element.IsStatic ? ref staticBrandId : ref instanceBrandId);
            if (brandId == 0)
                brandId = Vm.Agent.AllocatePrivateBrandId();
            var binding = new PlannedPrivateBinding(brandId, nextSlot++, kind, element.IsStatic);
            ownBindings.Add(name, binding);
            bindings[name] = binding;
            if (kind == PlannedPrivateMemberKind.Accessor)
            {
                accessorMasks ??= new(StringComparer.Ordinal);
                accessorMasks[name] = element.Kind == JsClassElementKind.Getter ? (byte)1 : (byte)2;
            }
        }
        RegisterPrivateDebugNames(bindings);
        return bindings;
    }

    private void EmitClassElement(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister,
        int prototypeRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var targetRegister = element.IsStatic ? constructorRegister : prototypeRegister;
            if (element.Kind is JsClassElementKind.Getter or JsClassElementKind.Setter)
            {
                var arguments = builder.AllocateTemporaryRegisterBlock(4);
                EmitLdar(targetRegister);
                EmitStar(arguments);
                EmitClassElementKey(ast, element, arguments + 1);
                if (element.Kind == JsClassElementKind.Getter)
                    EmitClassElementFunction(ast, element, targetRegister);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(arguments + 2);
                if (element.Kind == JsClassElementKind.Setter)
                    EmitClassElementFunction(ast, element, targetRegister);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(arguments + 3);
                builder.EmitCallRuntime((int)RuntimeId.DefineClassAccessor, arguments, 4);
                return;
            }

            if (element.Kind != JsClassElementKind.Method)
                throw new NotSupportedException(
                    $"Class element '{element.Kind}' is not implemented yet."
                );
            var methodArguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitLdar(targetRegister);
            EmitStar(methodArguments);
            EmitClassElementKey(ast, element, methodArguments + 1);
            EmitClassElementFunction(ast, element, targetRegister);
            EmitStar(methodArguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.DefineClassMethod, methodArguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitStaticClassFieldInitializer(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister,
        int keyRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitLdar(constructorRegister);
            EmitStar(arguments);
            EmitLdar(keyRegister);
            EmitStar(arguments + 1);
            EmitClassElementFunction(ast, element, constructorRegister);
            var initializerRegister = builder.AllocateTemporaryRegister();
            EmitStar(initializerRegister);
            builder.EmitCallProperty(
                initializerRegister,
                constructorRegister,
                element.IsComputed ? keyRegister : 0,
                element.IsComputed ? 1 : 0
            );
            EmitStar(arguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.DefineClassField, arguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitStaticPrivateFieldInitializer(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitClassElementFunction(ast, element, constructorRegister);
            var initializerRegister = builder.AllocateTemporaryRegister();
            EmitStar(initializerRegister);
            builder.EmitCallProperty(initializerRegister, constructorRegister, 0, 0);
            var valueRegister = builder.AllocateTemporaryRegister();
            EmitStar(valueRegister);
            var binding = ResolvePrivateBinding(ast.GetString(element.Key));
            EmitPrivateFieldOp(
                JsOpCode.InitPrivateField,
                constructorRegister,
                valueRegister,
                binding
            );
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitClassStaticBlock(JsAst ast, in JsClassElement element, int constructorRegister)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitClassElementFunction(ast, element, constructorRegister);
            var functionRegister = builder.AllocateTemporaryRegister();
            EmitStar(functionRegister);
            builder.EmitCallProperty(functionRegister, constructorRegister, 0, 0);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitCacheInstanceFieldKey(
        JsAst ast,
        in JsClassElement element,
        int constructorRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitLdar(constructorRegister);
            EmitStar(arguments);
            EmitSmi(element.InstanceFieldKeyIndex);
            EmitStar(arguments + 1);
            EmitClassElementKey(ast, element, arguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.SetFunctionInstanceFieldKey, arguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    protected void EmitInstanceFieldInitializers(JsAst ast, int classIndex)
    {
        var elements = ast.GetClassElements(ast.GetClass(classIndex));
        Dictionary<string, byte>? privateAccessorMasks = null;
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var accessor = ref elements[i];
            if (
                !accessor.IsPrivate
                || accessor.IsStatic
                || accessor.Kind is not (JsClassElementKind.Getter or JsClassElementKind.Setter)
            )
                continue;
            privateAccessorMasks ??= new(StringComparer.Ordinal);
            var name = ast.GetString(accessor.Key);
            privateAccessorMasks.TryGetValue(name, out var mask);
            privateAccessorMasks[name] = (byte)(
                mask | (accessor.Kind == JsClassElementKind.Getter ? 1 : 2)
            );
        }
        HashSet<string>? initializedAccessors = null;
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var element = ref elements[i];
            if (!element.IsPrivate || element.IsStatic)
                continue;
            if (element.Kind == JsClassElementKind.Method)
            {
                EmitInstancePrivateMethod(ast, element);
                continue;
            }
            if (element.Kind is not (JsClassElementKind.Getter or JsClassElementKind.Setter))
                continue;
            var name = ast.GetString(element.Key);
            initializedAccessors ??= new(StringComparer.Ordinal);
            if (initializedAccessors.Add(name))
                EmitInstancePrivateAccessor(
                    ResolvePrivateBinding(name),
                    privateAccessorMasks![name]
                );
        }
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var element = ref elements[i];
            if (element.IsStatic || element.Kind != JsClassElementKind.Field)
                continue;

            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var arguments = builder.AllocateTemporaryRegisterBlock(3);
                builder.EmitLda(JsOpCode.LdaThis);
                EmitStar(arguments);
                if (element.IsPrivate)
                {
                    if (element.ValueNode >= 0)
                    {
                        emittingInstanceFieldInitializer = true;
                        try
                        {
                            if (!element.IsComputed)
                                EmitExpressionWithInferredName(
                                    ast,
                                    element.ValueNode,
                                    ast.GetString(element.Key)
                                );
                            else
                                EmitExpression(ast, element.ValueNode);
                        }
                        finally
                        {
                            emittingInstanceFieldInitializer = false;
                        }
                    }
                    else
                        builder.EmitLda(JsOpCode.LdaUndefined);
                    EmitStar(arguments + 1);
                    EmitPrivateFieldOp(
                        JsOpCode.InitPrivateField,
                        arguments,
                        arguments + 1,
                        ResolvePrivateBinding(ast.GetString(element.Key))
                    );
                    continue;
                }
                if (element.InstanceFieldKeyIndex >= 0)
                {
                    EmitSmi(element.InstanceFieldKeyIndex);
                    EmitStar(arguments + 1);
                    builder.EmitCallRuntime(
                        (int)RuntimeId.LoadCurrentFunctionInstanceFieldKey,
                        arguments + 1,
                        1
                    );
                }
                else
                    EmitStringLiteral(ast.GetString(element.Key));
                EmitStar(arguments + 1);
                if (element.ValueNode >= 0)
                {
                    emittingInstanceFieldInitializer = true;
                    try
                    {
                        if (!element.IsComputed)
                            EmitExpressionWithInferredName(
                                ast,
                                element.ValueNode,
                                ast.GetString(element.Key)
                            );
                        else
                            EmitExpressionWithComputedName(ast, element.ValueNode, arguments + 1);
                    }
                    finally
                    {
                        emittingInstanceFieldInitializer = false;
                    }
                }
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(arguments + 2);
                builder.EmitCallRuntime((int)RuntimeId.DefineClassField, arguments, 3);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
        }
    }

    private void EmitInstancePrivateMethod(JsAst ast, in JsClassElement element)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var registers = builder.AllocateTemporaryRegisterBlock(2);
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStar(registers);
            var binding = ResolvePrivateBinding(ast.GetString(element.Key));
            EmitLoadCurrentFunctionPrivateMethodValue(binding.SlotIndex * 2);
            EmitStar(registers + 1);
            EmitPrivateFieldOp(JsOpCode.InitPrivateMethod, registers, registers + 1, binding);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitInstancePrivateAccessor(in PlannedPrivateBinding binding, byte mask)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var registers = builder.AllocateTemporaryRegisterBlock(3);
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStar(registers);
            if ((mask & 1) != 0)
                EmitLoadCurrentFunctionPrivateMethodValue(binding.SlotIndex * 2);
            else
                builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(registers + 1);
            if ((mask & 2) != 0)
                EmitLoadCurrentFunctionPrivateMethodValue(binding.SlotIndex * 2 + 1);
            else
                builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(registers + 2);
            EmitPrivateAccessorOp(registers, registers + 1, registers + 2, binding);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitLoadCurrentFunctionPrivateMethodValue(int valueIndex)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var argument = builder.AllocateTemporaryRegister();
            EmitSmi(valueIndex);
            EmitStar(argument);
            builder.EmitCallRuntime(
                (int)RuntimeId.LoadCurrentFunctionPrivateMethodValue,
                argument,
                1
            );
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitClassElementKey(JsAst ast, in JsClassElement element, int keyRegister)
    {
        if (element.IsComputed)
        {
            EmitExpression(ast, element.Key);
            EmitStar(keyRegister);
            builder.EmitCallRuntime((int)RuntimeId.NormalizePropertyKey, keyRegister, 1);
        }
        else
            EmitStringLiteral(ast.GetString(element.Key));
        EmitStar(keyRegister);
    }

    private PlannedPrivateBinding ResolvePrivateBinding(string name)
    {
        if (visiblePrivateBindings.TryGetValue(name, out var binding))
            return binding;
        throw new InvalidOperationException($"Private name '{name}' was not planned.");
    }

    private void EmitPrivateBrandMappingsForClosure()
    {
        if (visiblePrivateBindings.Count == 0 && activeExactPrivateBrandSources.Count == 0)
            return;
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var closureRegister = builder.AllocateTemporaryRegister();
            EmitStar(closureRegister);
            if (visiblePrivateBindings.Count != 0)
            {
                var sourceRegister = builder.AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaCurrentFunction);
                EmitStar(sourceRegister);
                HashSet<int>? mappedBrands = null;
                foreach (var binding in visiblePrivateBindings.Values)
                {
                    mappedBrands ??= [];
                    if (!mappedBrands.Add(binding.BrandId))
                        continue;
                    EmitPrivateBrandMapping(
                        RuntimeId.SetFunctionPrivateBrandMapping,
                        closureRegister,
                        binding.BrandId,
                        sourceRegister
                    );
                }
            }
            EmitExactPrivateBrandMappings(closureRegister, activeExactPrivateBrandSources);
            EmitLdar(closureRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitExactPrivateBrandMappings(
        int targetRegister,
        IReadOnlyList<PrivateBrandSource> sources
    )
    {
        for (var i = 0; i < sources.Count; i++)
            EmitPrivateBrandMapping(
                RuntimeId.SetFunctionPrivateBrandMappingExact,
                targetRegister,
                sources[i].BrandId,
                sources[i].Register
            );
    }

    private void EmitPrivateBrandMapping(
        RuntimeId runtime,
        int targetRegister,
        int brandId,
        int sourceRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var arguments = builder.AllocateTemporaryRegisterBlock(3);
            EmitMove(targetRegister, arguments);
            EmitSmi(brandId);
            EmitStar(arguments + 1);
            EmitMove(sourceRegister, arguments + 2);
            builder.EmitCallRuntime((int)runtime, arguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitPrivateFieldOp(
        JsOpCode op,
        int objectRegister,
        int valueRegister,
        in PlannedPrivateBinding binding
    )
    {
        if (
            (uint)objectRegister > byte.MaxValue
            || (uint)valueRegister > byte.MaxValue
            || binding.BrandId < 0
            || (uint)binding.SlotIndex > ushort.MaxValue
        )
            throw new NotSupportedException("Private field operands exceed bytecode capacity.");
        builder.Emit(
            op,
            [
                (byte)objectRegister,
                (byte)valueRegister,
                (byte)binding.BrandId,
                (byte)(binding.BrandId >> 8),
                (byte)(binding.BrandId >> 16),
                (byte)(binding.BrandId >> 24),
                (byte)binding.SlotIndex,
                (byte)(binding.SlotIndex >> 8),
            ]
        );
    }

    private void EmitPrivateAccessorInitialization(
        int objectRegister,
        int getterRegister,
        int setterRegister,
        in PlannedPrivateBinding binding
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            if (getterRegister < 0)
            {
                getterRegister = builder.AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(getterRegister);
            }
            if (setterRegister < 0)
            {
                setterRegister = builder.AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStar(setterRegister);
            }
            EmitPrivateAccessorOp(objectRegister, getterRegister, setterRegister, binding);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitPrivateAccessorOp(
        int objectRegister,
        int getterRegister,
        int setterRegister,
        in PlannedPrivateBinding binding
    )
    {
        if (
            (uint)objectRegister > byte.MaxValue
            || (uint)getterRegister > byte.MaxValue
            || (uint)setterRegister > byte.MaxValue
            || binding.BrandId < 0
            || (uint)binding.SlotIndex > ushort.MaxValue
        )
            throw new NotSupportedException("Private accessor operands exceed bytecode capacity.");
        builder.Emit(
            JsOpCode.InitPrivateAccessor,
            [
                (byte)objectRegister,
                (byte)getterRegister,
                (byte)setterRegister,
                (byte)binding.BrandId,
                (byte)(binding.BrandId >> 8),
                (byte)(binding.BrandId >> 16),
                (byte)(binding.BrandId >> 24),
                (byte)binding.SlotIndex,
                (byte)(binding.SlotIndex >> 8),
            ]
        );
    }

    private void EmitPrivateFieldOp(
        JsOpCode op,
        int objectRegister,
        in PlannedPrivateBinding binding
    )
    {
        if (
            (uint)objectRegister > byte.MaxValue
            || binding.BrandId < 0
            || (uint)binding.SlotIndex > ushort.MaxValue
        )
            throw new NotSupportedException("Private field operands exceed bytecode capacity.");
        builder.Emit(
            op,
            [
                (byte)objectRegister,
                (byte)binding.BrandId,
                (byte)(binding.BrandId >> 8),
                (byte)(binding.BrandId >> 16),
                (byte)(binding.BrandId >> 24),
                (byte)binding.SlotIndex,
                (byte)(binding.SlotIndex >> 8),
            ]
        );
    }

    private void EmitClassElementFunction(
        JsAst ast,
        in JsClassElement element,
        int homeObjectRegister
    )
    {
        ref readonly var function = ref ast[element.ValueNode];
        var inferredName = element.IsComputed ? null : ast.GetString(element.Key);
        if (inferredName is not null)
            inferredName = element.Kind switch
            {
                JsClassElementKind.Getter => $"get {inferredName}",
                JsClassElementKind.Setter => $"set {inferredName}",
                _ => inferredName,
            };
        EmitFunctionExpression(ast, function.Arg0, function.Arg1, inferredName);
        EmitAttachMethodEnvironmentIfNeeded(ast, element.ValueNode, homeObjectRegister);
    }
}
