using System.Buffers;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    private void EmitClassDeclaration(FlatAst ast, in AstNode node)
    {
        var info = ast.GetClass(node.Arg0);
        EmitClassExpression(ast, node.Arg0);
        var name = ast.GetString(info.NameStringIndex);
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException($"No planned class binding found for '{name}'.");
        EmitStore(binding, isInitialization: true);
    }

    private void EmitClassExpression(FlatAst ast, int classIndex, string? inferredName = null)
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
                EmitLdar(constructorRegister);
                EmitStar(heritageArguments);
                EmitLdar(heritageRegister);
                EmitStar(heritageArguments + 1);
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
            try
            {
                for (var i = 0; i < elements.Length; i++)
                {
                    ref readonly var element = ref elements[i];
                    if (element.Kind == JsClassElementKind.Constructor)
                        continue;
                    if (element.Kind == JsClassElementKind.StaticBlock)
                        continue;
                    if (element.Kind == JsClassElementKind.Field)
                    {
                        if (element.IsPrivate)
                            continue;
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

                if (declaredName.Length != 0)
                {
                    if (!TryResolveBinding(declaredName, out var classAlias))
                        throw new InvalidOperationException(
                            $"No planned class lexical binding found for '{declaredName}'."
                        );
                    EmitLdar(constructorRegister);
                    EmitStore(classAlias, isInitialization: true);
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

    private IReadOnlyDictionary<string, PlannedPrivateBinding> BuildClassPrivateBindings(
        FlatAst ast,
        ReadOnlySpan<FlatClassElement> elements,
        out int instanceBrandId,
        out int staticBrandId
    )
    {
        var bindings = new Dictionary<string, PlannedPrivateBinding>(
            visiblePrivateBindings,
            StringComparer.Ordinal
        );
        HashSet<string>? ownNames = null;
        instanceBrandId = 0;
        staticBrandId = 0;
        var nextSlot = 0;
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var element = ref elements[i];
            if (!element.IsPrivate)
                continue;
            if (element.Kind != JsClassElementKind.Field)
                throw new NotSupportedException(
                    $"{CompilerName} does not support private class element '{element.Kind}' yet."
                );
            var name = ast.GetString(element.Key);
            ownNames ??= new(StringComparer.Ordinal);
            if (!ownNames.Add(name))
                throw new InvalidOperationException($"Duplicate private class member '{name}'.");
            ref var brandId = ref (element.IsStatic ? ref staticBrandId : ref instanceBrandId);
            if (brandId == 0)
                brandId = Vm.Agent.AllocatePrivateBrandId();
            bindings[name] = new(brandId, nextSlot++);
        }
        return bindings;
    }

    private void EmitClassElement(
        FlatAst ast,
        in FlatClassElement element,
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
                    $"Flat class element '{element.Kind}' is not implemented yet."
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
        FlatAst ast,
        in FlatClassElement element,
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
            builder.EmitCallProperty(initializerRegister, constructorRegister, 0, 0);
            EmitStar(arguments + 2);
            builder.EmitCallRuntime((int)RuntimeId.DefineClassField, arguments, 3);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitStaticPrivateFieldInitializer(
        FlatAst ast,
        in FlatClassElement element,
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

    private void EmitClassStaticBlock(
        FlatAst ast,
        in FlatClassElement element,
        int constructorRegister
    )
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
        FlatAst ast,
        in FlatClassElement element,
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

    protected void EmitInstanceFieldInitializers(FlatAst ast, int classIndex)
    {
        var elements = ast.GetClassElements(ast.GetClass(classIndex));
        for (var i = 0; i < elements.Length; i++)
        {
            ref readonly var element = ref elements[i];
            if (element.Kind != JsClassElementKind.Field || element.IsStatic)
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
                        EmitExpression(ast, element.ValueNode);
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

    private void EmitClassElementKey(FlatAst ast, in FlatClassElement element, int keyRegister)
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
            EmitLdar(targetRegister);
            EmitStar(arguments);
            EmitSmi(brandId);
            EmitStar(arguments + 1);
            EmitLdar(sourceRegister);
            EmitStar(arguments + 2);
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
            || (uint)binding.BrandId > ushort.MaxValue
            || (uint)binding.SlotIndex > ushort.MaxValue
        )
            throw new NotSupportedException("Private field operands exceed bytecode capacity.");
        builder.Emit(
            op,
            (byte)objectRegister,
            (byte)valueRegister,
            (byte)binding.BrandId,
            (byte)(binding.BrandId >> 8),
            (byte)binding.SlotIndex,
            (byte)(binding.SlotIndex >> 8)
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
            || (uint)binding.BrandId > ushort.MaxValue
            || (uint)binding.SlotIndex > ushort.MaxValue
        )
            throw new NotSupportedException("Private field operands exceed bytecode capacity.");
        builder.Emit(
            op,
            (byte)objectRegister,
            (byte)binding.BrandId,
            (byte)(binding.BrandId >> 8),
            (byte)binding.SlotIndex,
            (byte)(binding.SlotIndex >> 8)
        );
    }

    private void EmitClassElementFunction(
        FlatAst ast,
        in FlatClassElement element,
        int homeObjectRegister
    )
    {
        ref readonly var function = ref ast[element.ValueNode];
        EmitFunctionExpression(
            ast,
            function.Arg0,
            function.Arg1,
            element.IsComputed ? null : ast.GetString(element.Key)
        );
        EmitAttachMethodEnvironmentIfNeeded(ast, element.ValueNode, homeObjectRegister);
    }
}
