using System.Diagnostics;
using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void VisitClassExpression(JsClassExpression classExpr, string? inferredName = null,
        string? classLexicalBindingName = null)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var constructorName = classExpr.Name ?? inferredName ?? string.Empty;
            var classLexicalName = classExpr.Name ?? classLexicalBindingName;
            CompilerIdentifierName? classLexicalIdentifier = classLexicalName is not null
                ? new CompilerIdentifierName(classLexicalName, classExpr.NameId)
                : null;
            string? classLexicalInternalName = null;
            JsFunctionExpression? constructorFunction = null;
            var useImplicitDerivedSuperForwardAll = false;
            var privateFieldBindingBySourceName = new Dictionary<string, PrivateFieldBinding>(StringComparer.Ordinal);
            var privateFieldInitializers = new List<PrivateFieldInitPlan>();
            var instanceFieldInitializers = new List<InstanceFieldInitializerPlan>();
            var privateAccessorInitBySourceName =
                new Dictionary<string, PrivateAccessorInitPlan>(StringComparer.Ordinal);
            var privateMethodInitializers = new List<PrivateMethodInitPlan>();
            var publicFieldInitializers = new List<PublicFieldInitPlan>();
            var staticPrivateAccessorInitBySourceName =
                new Dictionary<string, PrivateAccessorInitPlan>(StringComparer.Ordinal);
            var instancePrivateBrandId = 0;
            var staticPrivateBrandId = 0;
            var nextPrivateSlot = 0;
            var nextInstanceComputedFieldInitKeyIndex = 0;
            var hasExtends = classExpr.HasExtends;
            var hasInstancePrivateMembers = false;
            var hasStaticPrivateMembers = false;
            var superReg = -1;

            int GetOrAllocatePrivateBrandId(bool isStatic)
            {
                if (isStatic)
                {
                    staticPrivateBrandId = staticPrivateBrandId != 0
                        ? staticPrivateBrandId
                        : AllocateClassPrivateFieldBrandId();
                    return staticPrivateBrandId;
                }

                instancePrivateBrandId = instancePrivateBrandId != 0
                    ? instancePrivateBrandId
                    : AllocateClassPrivateFieldBrandId();
                return instancePrivateBrandId;
            }

            if (!string.IsNullOrEmpty(classLexicalName))
            {
                classLexicalInternalName = GetClassLexicalInternalName(classLexicalName, classExpr.Position);
                var classLexicalSymbolId = GetOrCreateSymbolId(classLexicalInternalName);
                GetOrCreateLocal(classLexicalSymbolId);
                MarkLexicalBinding(classLexicalSymbolId, true);
                PushAliasScope(new CompilerIdentifierName(classLexicalName, classExpr.NameId),
                    classLexicalInternalName);
            }

            try
            {
                if (hasExtends)
                {
                    if (classExpr.ExtendsExpression is null)
                        throw new InvalidOperationException("Class extends expression is missing.");
                    VisitExpression(classExpr.ExtendsExpression);
                    superReg = AllocateTemporaryRegister();
                    EmitStarRegister(superReg);
                }

                foreach (var element in classExpr.Elements)
                    if (element.Kind == JsClassElementKind.Constructor)
                    {
                        constructorFunction = element.Value
                                              ?? throw new NotSupportedException(
                                                  "Class constructor must have a function value.");
                        break;
                    }

                foreach (var element in classExpr.Elements)
                {
                    if (element.Kind == JsClassElementKind.Constructor)
                        continue;
                    if (element.Kind == JsClassElementKind.StaticBlock)
                        continue;
                    if (element.IsPrivate && (element.IsComputedKey || element.ComputedKey is not null))
                        throw new NotSupportedException(
                            "computed private class element keys are not supported in Okojo Phase 2.");
                    if (!element.IsComputedKey && element.Key is null)
                        throw new InvalidOperationException("Class element key is missing.");

                    if (element.IsPrivate)
                    {
                        if (element.IsStatic)
                            hasStaticPrivateMembers = true;
                        else
                            hasInstancePrivateMembers = true;

                        var sourcePrivateName = element.Key!;
                        switch (element.Kind)
                        {
                            case JsClassElementKind.Field:
                            {
                                if (privateFieldBindingBySourceName.ContainsKey(sourcePrivateName) ||
                                    privateAccessorInitBySourceName.ContainsKey(sourcePrivateName))
                                    throw new NotSupportedException(
                                        $"Duplicate private class member '{sourcePrivateName}' is not supported in Okojo Phase 2.");

                                var binding = new PrivateFieldBinding(GetOrAllocatePrivateBrandId(element.IsStatic),
                                    nextPrivateSlot++);
                                privateFieldBindingBySourceName[sourcePrivateName] = binding;
                                if (!element.IsStatic)
                                {
                                    var initPlan =
                                        new PrivateFieldInitPlan(sourcePrivateName, binding, element.FieldInitializer);
                                    privateFieldInitializers.Add(initPlan);
                                    instanceFieldInitializers.Add(new(
                                        InstanceFieldInitializerKind.PrivateField,
                                        initPlan,
                                        default));
                                }

                                break;
                            }
                            case JsClassElementKind.Getter:
                            case JsClassElementKind.Setter:
                            {
                                if (privateFieldBindingBySourceName.TryGetValue(sourcePrivateName,
                                        out var existingBinding) &&
                                    existingBinding.Kind != PrivateMemberKind.Accessor)
                                    throw new NotSupportedException(
                                        $"Private field/accessor name collision '{sourcePrivateName}' is not supported in Okojo Phase 2.");

                                if (!privateFieldBindingBySourceName.TryGetValue(sourcePrivateName,
                                        out var accessorBinding))
                                {
                                    accessorBinding = new(
                                        GetOrAllocatePrivateBrandId(element.IsStatic),
                                        nextPrivateSlot++,
                                        PrivateMemberKind.Accessor);
                                    privateFieldBindingBySourceName[sourcePrivateName] = accessorBinding;
                                    if (element.IsStatic)
                                        staticPrivateAccessorInitBySourceName[sourcePrivateName] =
                                            new(
                                                sourcePrivateName, accessorBinding, null, null);
                                    else
                                        privateAccessorInitBySourceName[sourcePrivateName] = new(
                                            sourcePrivateName, accessorBinding, null, null);
                                }
                                else if (accessorBinding.Kind != PrivateMemberKind.Accessor)
                                {
                                    throw new NotSupportedException(
                                        $"Private field/accessor name collision '{sourcePrivateName}' is not supported in Okojo Phase 2.");
                                }

                                var accessorMap = element.IsStatic
                                    ? staticPrivateAccessorInitBySourceName
                                    : privateAccessorInitBySourceName;
                                if (!accessorMap.TryGetValue(sourcePrivateName, out var initPlan))
                                    initPlan = new(sourcePrivateName, accessorBinding, null,
                                        null);

                                if (element.Value is null)
                                    throw new InvalidOperationException("Private accessor function is missing.");

                                if (element.Kind == JsClassElementKind.Getter)
                                {
                                    if (initPlan.Getter is not null)
                                        throw new NotSupportedException(
                                            $"Duplicate private getter '{sourcePrivateName}' is not supported in Okojo Phase 2.");
                                    initPlan = initPlan with { Getter = element.Value };
                                }
                                else
                                {
                                    if (initPlan.Setter is not null)
                                        throw new NotSupportedException(
                                            $"Duplicate private setter '{sourcePrivateName}' is not supported in Okojo Phase 2.");
                                    initPlan = initPlan with { Setter = element.Value };
                                }

                                accessorMap[sourcePrivateName] = initPlan;
                                break;
                            }
                            case JsClassElementKind.Method:
                            {
                                if (privateFieldBindingBySourceName.ContainsKey(sourcePrivateName) ||
                                    privateAccessorInitBySourceName.ContainsKey(sourcePrivateName) ||
                                    staticPrivateAccessorInitBySourceName.ContainsKey(sourcePrivateName))
                                    throw new NotSupportedException(
                                        $"Duplicate private class member '{sourcePrivateName}' is not supported in Okojo Phase 2.");

                                if (element.Value is null)
                                    throw new InvalidOperationException("Private method function is missing.");

                                var methodBinding = new PrivateFieldBinding(
                                    GetOrAllocatePrivateBrandId(element.IsStatic),
                                    nextPrivateSlot++,
                                    PrivateMemberKind.Method);
                                privateFieldBindingBySourceName[sourcePrivateName] = methodBinding;

                                if (element.IsStatic)
                                {
                                    // Static private methods initialize on ctor in class-definition lowering pass.
                                }
                                else
                                {
                                    privateMethodInitializers.Add(new(sourcePrivateName,
                                        methodBinding,
                                        element.Value));
                                }

                                break;
                            }
                            default:
                                throw new NotSupportedException(
                                    $"Private class element kind '{element.Kind}' is not supported in Okojo Phase 2.");
                        }

                        continue;
                    }

                    if (element.Kind == JsClassElementKind.Field && !element.IsStatic)
                    {
                        var initPlan = new PublicFieldInitPlan(
                            element,
                            element.IsComputedKey ? nextInstanceComputedFieldInitKeyIndex++ : -1,
                            element.Key);
                        publicFieldInitializers.Add(initPlan);
                        instanceFieldInitializers.Add(new(
                            InstanceFieldInitializerKind.PublicField,
                            default,
                            initPlan));
                    }
                }

                var privateAccessorInitializers = privateAccessorInitBySourceName.Values.ToList();
                var visiblePrivateBindings = MergeVisiblePrivateBindings(privateFieldBindingBySourceName);
                activeClassPrivateBindingScopes.Push(visiblePrivateBindings);
                try
                {
                    var inheritedPrivateBrandIds =
                        CollectInheritedPrivateBrandIds(visiblePrivateBindings, instancePrivateBrandId,
                            staticPrivateBrandId);
                    var inheritedActivePrivateBrandMappings =
                        CollectInheritedPrivateBrandMappingsFromActiveScopes(instancePrivateBrandId,
                            staticPrivateBrandId);
                    var inheritedPrivateBrandSourceReg = -1;
                    if (inheritedPrivateBrandIds is not null)
                    {
                        inheritedPrivateBrandSourceReg = AllocateTemporaryRegister();
                        builder.EmitLda(JsOpCode.LdaCurrentFunction);
                        EmitStarRegister(inheritedPrivateBrandSourceReg);
                    }

                    if (constructorFunction is null)
                    {
                        JsBlockStatement constructorBody;
                        if (hasExtends)
                        {
                            useImplicitDerivedSuperForwardAll = true;
                            constructorBody = new(Array.Empty<JsStatement>(), true);
                        }
                        else
                        {
                            constructorBody = new(Array.Empty<JsStatement>(), true);
                        }

                        constructorFunction = new(
                            constructorName,
                            Array.Empty<string>(),
                            constructorBody);
                    }

                    var instanceFieldInitializersUseSuper = instanceFieldInitializers.Any(static initializer =>
                        initializer.Kind == InstanceFieldInitializerKind.PrivateField
                            ? initializer.PrivateField.Initializer is not null &&
                              FieldInitializerUsesDirectSuper(initializer.PrivateField.Initializer)
                            : initializer.PublicField.Element.FieldInitializer is not null &&
                              FieldInitializerUsesDirectSuper(initializer.PublicField.Element.FieldInitializer));

                    var ctorParameterPlan = FunctionParameterPlan.FromFunction(constructorFunction);
                    var constructorUsesSuper =
                        FunctionUsesSuper(ctorParameterPlan.Initializers, constructorFunction.Body);
                    var constructorNeedsMethodEnvironment = constructorUsesSuper || instanceFieldInitializersUseSuper;
                    var ctorObj = CompileFunctionObject(
                        constructorName,
                        ctorParameterPlan,
                        constructorFunction.Body,
                        CreateFunctionShape(
                            constructorFunction.IsGenerator,
                            constructorFunction.IsAsync,
                            constructorFunction.IsArrow,
                            isImplicitlyStrict: true,
                            isClassConstructor: true,
                            isDerivedConstructor: hasExtends,
                            emitImplicitSuperForwardAll: useImplicitDerivedSuperForwardAll),
                        sourceStartPosition: classExpr.Position,
                        sourceEndPosition: classExpr.EndPosition,
                        useMethodEnvironmentCapture: constructorNeedsMethodEnvironment,
                        classPrivateNameToBinding: visiblePrivateBindings,
                        privateFieldInitializers: privateFieldInitializers,
                        privateAccessorInitializers: privateAccessorInitializers,
                        privateMethodInitializers: privateMethodInitializers,
                        instanceFieldInitializers: instanceFieldInitializers,
                        publicFieldInitializers: publicFieldInitializers,
                        forceHasSuperReference: instanceFieldInitializersUseSuper);
                    if (ctorObj.RequiresClosureBinding)
                        requiresClosureBinding = true;

                    var ctorIdx = builder.AddObjectConstant(ctorObj);
                    EmitCreateClosureByIndex(ctorIdx);
                    if (inheritedPrivateBrandSourceReg >= 0)
                        EmitSetFunctionPrivateBrandMappingsFromAccumulator(inheritedPrivateBrandSourceReg,
                            inheritedPrivateBrandIds!);
                    if (inheritedActivePrivateBrandMappings is not null)
                        EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(inheritedActivePrivateBrandMappings);
                    var ctorReg = AllocateTemporaryRegister();
                    EmitStarRegister(ctorReg);

                    if (classExpr.Name is null && !string.IsNullOrEmpty(inferredName))
                    {
                        var argStart = AllocateTemporaryRegisterBlock(2);
                        EmitLdaRegister(ctorReg);
                        EmitStarRegister(argStart);
                        var nameIdx = builder.AddObjectConstant(inferredName);
                        EmitLdaStringConstantByIndex(nameIdx);
                        EmitStarRegister(argStart + 1);
                        EmitCallRuntime(RuntimeId.SetFunctionName, argStart, 2);
                        EmitLdaRegister(ctorReg);
                    }

                    if (classLexicalInternalName is not null)
                    {
                        EmitLdaRegister(ctorReg);
                        StoreIdentifier(classLexicalInternalName, true,
                            classLexicalName);
                    }

                    if (hasExtends)
                    {
                        var argsStart = AllocateTemporaryRegisterBlock(2);
                        var superArgReg = argsStart + 1;
                        EmitMoveRegister(ctorReg, argsStart);
                        EmitMoveRegister(superReg, superArgReg);
                        EmitCallRuntime(RuntimeId.SetClassHeritage, argsStart, 2);
                    }

                    EmitCallRuntime(RuntimeId.ClassGetPrototypeAndSetConstructor, ctorReg, 1);
                    var protoReg = AllocateTemporaryRegister();
                    EmitStarRegister(protoReg);
                    if (ctorObj.UsesMethodEnvironmentCapture)
                        EmitSetFunctionMethodEnvironment(ctorReg, protoReg, ctorReg);

                    var primaryCtorPrivateBrandSourceReg = hasInstancePrivateMembers
                        ? protoReg
                        : hasStaticPrivateMembers
                            ? ctorReg
                            : -1;
                    var instanceMemberPrivateBrandSourceReg = hasInstancePrivateMembers
                        ? protoReg
                        : hasStaticPrivateMembers
                            ? ctorReg
                            : -1;
                    var staticMemberPrivateBrandSourceReg = hasStaticPrivateMembers
                        ? ctorReg
                        : hasInstancePrivateMembers
                            ? protoReg
                            : -1;
                    List<int>? ctorExtraPrivateBrandIds = null;
                    if (hasInstancePrivateMembers && hasStaticPrivateMembers)
                    {
                        ctorExtraPrivateBrandIds = new(1);
                        if (staticPrivateBrandId != 0)
                            ctorExtraPrivateBrandIds.Add(staticPrivateBrandId);
                    }

                    var instanceMemberInheritedPrivateBrandIds =
                        CombinePrivateBrandIds(inheritedPrivateBrandIds, staticPrivateBrandId);
                    var staticMemberInheritedPrivateBrandIds =
                        CombinePrivateBrandIds(inheritedPrivateBrandIds, instancePrivateBrandId);
                    var currentClassPrivateBrandMappings = CreateCurrentClassPrivateBrandMappings(
                        instancePrivateBrandId,
                        protoReg,
                        staticPrivateBrandId,
                        ctorReg);
                    var visiblePrivateBrandMappings = CombinePrivateBrandMappings(
                        inheritedActivePrivateBrandMappings,
                        currentClassPrivateBrandMappings);

                    activeClassPrivateSourceScopes.Push(new(
                        instancePrivateBrandId,
                        protoReg,
                        staticPrivateBrandId,
                        ctorReg));
                    try
                    {
                        if (primaryCtorPrivateBrandSourceReg >= 0)
                        {
                            EmitLdaRegister(ctorReg);
                            EmitSetFunctionPrivateBrandTokenFromAccumulator(primaryCtorPrivateBrandSourceReg);
                            if (ctorExtraPrivateBrandIds is not null)
                                EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(ctorReg,
                                    ctorExtraPrivateBrandIds);

                            for (var i = 0; i < privateMethodInitializers.Count; i++)
                            {
                                var initPlan = privateMethodInitializers[i];
                                var methodParameterPlan = FunctionParameterPlan.FromFunction(initPlan.Function);
                                var methodObj = CompileFunctionObject(
                                    initPlan.SourceName,
                                    methodParameterPlan,
                                    initPlan.Function.Body,
                                    CreateFunctionShape(
                                        initPlan.Function.IsGenerator,
                                        initPlan.Function.IsAsync,
                                        initPlan.Function.IsArrow,
                                        true,
                                        true),
                                    sourceStartPosition: initPlan.Function.Position,
                                    useMethodEnvironmentCapture: true,
                                    classPrivateNameToBinding: visiblePrivateBindings);
                                if (methodObj.RequiresClosureBinding)
                                    requiresClosureBinding = true;

                                EmitCreateClosureForMethodWithEnvironment(methodObj, protoReg,
                                    privateBrandSourceReg: instanceMemberPrivateBrandSourceReg,
                                    inheritedPrivateBrandIds: instanceMemberInheritedPrivateBrandIds,
                                    inheritedPrivateBrandSourceReg: ctorReg,
                                    explicitPrivateBrandMappings: currentClassPrivateBrandMappings);
                                EmitSetFunctionPrivateMethodValueFromAccumulator(ctorReg, initPlan.Binding.SlotIndex);
                            }
                        }

                        var staticPrivateAccessorMaskByKey = new Dictionary<string, byte>(StringComparer.Ordinal);
                        foreach (var element in classExpr.Elements)
                        {
                            if (!element.IsPrivate || !element.IsStatic)
                                continue;
                            if (element.IsComputedKey || element.ComputedKey is not null)
                                throw new NotSupportedException(
                                    "computed private class element keys are not supported in Okojo Phase 2.");
                            if (element.Kind is not (JsClassElementKind.Getter or JsClassElementKind.Setter))
                                continue;
                            var bit = element.Kind == JsClassElementKind.Getter ? (byte)1 : (byte)2;
                            staticPrivateAccessorMaskByKey.TryGetValue(element.Key!, out var existing);
                            staticPrivateAccessorMaskByKey[element.Key!] = (byte)(existing | bit);
                        }

                        var staticPrivateAccessorStateByKey =
                            new Dictionary<string, PendingClassAccessorState>(StringComparer.Ordinal);
                        var accessorMaskByKey = new Dictionary<string, byte>(StringComparer.Ordinal);
                        var computedKeyPlans = new List<ComputedClassElementKeyPlan>();
                        var nextInstanceComputedFieldKeyIndex = 0;
                        foreach (var element in classExpr.Elements)
                        {
                            if (!element.IsComputedKey || element.IsPrivate ||
                                element.Kind == JsClassElementKind.StaticBlock)
                                continue;
                            if (element.Kind == JsClassElementKind.Constructor)
                                continue;
                            if (element.ComputedKey is null)
                                throw new InvalidOperationException(
                                    "Computed class element key expression is missing.");

                            VisitExpression(element.ComputedKey);
                            var normalizedKeyReg = AllocateTemporaryRegister();
                            EmitStarRegister(normalizedKeyReg);
                            EmitCallRuntime(RuntimeId.NormalizePropertyKey, normalizedKeyReg, 1);
                            EmitStarRegister(normalizedKeyReg);

                            if (element.Kind == JsClassElementKind.Field && !element.IsStatic)
                            {
                                var argStart = AllocateTemporaryRegisterBlock(3);
                                var ctorArgReg = argStart;
                                var indexReg = argStart + 1;
                                var valueReg = argStart + 2;
                                EmitLdaRegister(normalizedKeyReg);
                                EmitStarRegister(valueReg);
                                EmitLdaRegister(ctorReg);
                                EmitStarRegister(ctorArgReg);
                                EmitLda(nextInstanceComputedFieldKeyIndex);
                                EmitStarRegister(indexReg);
                                EmitCallRuntime(RuntimeId.SetFunctionInstanceFieldKey, argStart, 3);
                                computedKeyPlans.Add(new(
                                    InstanceFieldKeyIndex: nextInstanceComputedFieldKeyIndex));
                                nextInstanceComputedFieldKeyIndex++;
                            }
                            else
                            {
                                computedKeyPlans.Add(new(normalizedKeyReg));
                            }
                        }

                        var publicFieldInitPlanIndex = 0;
                        var computedKeyPlanIndex = 0;
                        foreach (var element in classExpr.Elements)
                        {
                            ComputedClassElementKeyPlan computedKeyPlan = default;
                            var hasComputedKeyPlan = false;
                            if (element.IsComputedKey && !element.IsPrivate &&
                                element.Kind != JsClassElementKind.StaticBlock &&
                                element.Kind != JsClassElementKind.Constructor)
                            {
                                computedKeyPlan = computedKeyPlans[computedKeyPlanIndex++];
                                hasComputedKeyPlan = true;
                            }

                            if (element.Kind == JsClassElementKind.Constructor)
                                continue;
                            if (element.IsPrivate && !element.IsStatic)
                                continue;
                            if (element.Kind == JsClassElementKind.StaticBlock)
                                continue;
                            if (!element.IsComputedKey && element.Key is null)
                                throw new InvalidOperationException("Class element key is missing.");
                            if (!element.IsStatic && element.Kind == JsClassElementKind.Field)
                            {
                                var initPlan = publicFieldInitializers[publicFieldInitPlanIndex++];
                                if (initPlan.ComputedKeyIndex >= 0)
                                    Debug.Assert(hasComputedKeyPlan &&
                                                 computedKeyPlan.InstanceFieldKeyIndex == initPlan.ComputedKeyIndex);

                                continue;
                            }

                            if (element.Kind is not
                                (JsClassElementKind.Method or JsClassElementKind.Getter or JsClassElementKind.Setter or
                                JsClassElementKind.Field or JsClassElementKind.StaticBlock))
                                throw new NotSupportedException(
                                    $"Class element kind '{element.Kind}' is not supported in Okojo Phase 2.");
                            if (element.Kind is not (JsClassElementKind.Field or JsClassElementKind.StaticBlock) &&
                                element.Value is null)
                                throw new InvalidOperationException($"Class {element.Kind} value is missing.");

                            if (!element.IsComputedKey &&
                                element.Kind is JsClassElementKind.Getter or JsClassElementKind.Setter)
                            {
                                var bit = element.Kind == JsClassElementKind.Getter ? (byte)1 : (byte)2;
                                var accessorMaskKey = MakeClassAccessorMaskKey(element.Key!, element.IsStatic);
                                accessorMaskByKey.TryGetValue(accessorMaskKey, out var existing);
                                accessorMaskByKey[accessorMaskKey] = (byte)(existing | bit);
                            }
                        }

                        computedKeyPlanIndex = 0;
                        var pairedAccessorStateByKey =
                            new Dictionary<string, PendingClassAccessorState>(StringComparer.Ordinal);
                        foreach (var element in classExpr.Elements)
                        {
                            ComputedClassElementKeyPlan computedKeyPlan = default;
                            var hasComputedKeyPlan = false;
                            if (element.IsComputedKey && !element.IsPrivate &&
                                element.Kind != JsClassElementKind.StaticBlock &&
                                element.Kind != JsClassElementKind.Constructor)
                            {
                                computedKeyPlan = computedKeyPlans[computedKeyPlanIndex++];
                                hasComputedKeyPlan = true;
                            }

                            if (element.Kind == JsClassElementKind.Constructor)
                                continue;
                            if (element.IsPrivate && !element.IsStatic)
                                continue;

                            if (element.Kind == JsClassElementKind.StaticBlock)
                            {
                                EmitClassStaticBlock(ctorReg, classLexicalIdentifier, element,
                                    staticMemberPrivateBrandSourceReg,
                                    staticMemberInheritedPrivateBrandIds,
                                    ctorReg,
                                    visiblePrivateBrandMappings);
                                continue;
                            }

                            var key = element.Key!;
                            if (element.IsPrivate)
                            {
                                if (!privateFieldBindingBySourceName.TryGetValue(key, out var privateBinding))
                                    throw new InvalidOperationException($"Missing private binding for '{key}'.");

                                switch (element.Kind)
                                {
                                    case JsClassElementKind.Field:
                                    {
                                        if (element.IsStatic)
                                            EmitStaticPrivateFieldInitializerOnTarget(
                                                ctorReg,
                                                privateBinding,
                                                element.FieldInitializer,
                                                element.Position,
                                                element.Position,
                                                visiblePrivateBindings,
                                                classLexicalIdentifier,
                                                key,
                                                staticMemberInheritedPrivateBrandIds,
                                                ctorReg,
                                                visiblePrivateBrandMappings);
                                        else
                                            EmitPrivateFieldInitializerOnTarget(ctorReg, privateBinding,
                                                element.FieldInitializer,
                                                key);
                                        break;
                                    }
                                    case JsClassElementKind.Getter:
                                    case JsClassElementKind.Setter:
                                    {
                                        var elementFunction = element.Value!;
                                        var accessorFn = CompileClassElementFunction(
                                            element.Kind == JsClassElementKind.Getter ? $"get {key}" : $"set {key}",
                                            elementFunction,
                                            visiblePrivateBindings,
                                            classLexicalIdentifier);

                                        var keyHasBothAccessorKinds =
                                            staticPrivateAccessorMaskByKey.TryGetValue(key, out var mask) &&
                                            mask == 3;
                                        if (!keyHasBothAccessorKinds)
                                        {
                                            EmitPrivateAccessorInitializerOnTarget(
                                                ctorReg,
                                                privateBinding,
                                                element.Kind == JsClassElementKind.Getter ? accessorFn : null,
                                                element.Kind == JsClassElementKind.Setter ? accessorFn : null,
                                                staticMemberInheritedPrivateBrandIds,
                                                ctorReg,
                                                visiblePrivateBrandMappings);
                                            break;
                                        }

                                        if (!staticPrivateAccessorStateByKey.TryGetValue(key, out var state))
                                        {
                                            state = new();
                                            staticPrivateAccessorStateByKey[key] = state;
                                        }

                                        if (element.Kind == JsClassElementKind.Getter)
                                            state.Getter = accessorFn;
                                        else
                                            state.Setter = accessorFn;

                                        if (state.Getter is not null && state.Setter is not null)
                                            EmitPrivateAccessorInitializerOnTarget(ctorReg, privateBinding,
                                                state.Getter,
                                                state.Setter, staticMemberInheritedPrivateBrandIds,
                                                ctorReg,
                                                visiblePrivateBrandMappings);
                                        break;
                                    }
                                    case JsClassElementKind.Method:
                                    {
                                        EmitPrivateMethodInitializerOnTarget(ctorReg, key, privateBinding,
                                            element.Value!,
                                            staticMemberInheritedPrivateBrandIds, ctorReg,
                                            visiblePrivateBrandMappings);
                                        break;
                                    }
                                    default:
                                        throw new NotSupportedException(
                                            $"Private class element kind '{element.Kind}' is not supported in Okojo Phase 2.");
                                }

                                continue;
                            }

                            var targetReg = element.IsStatic ? ctorReg : protoReg;
                            switch (element.Kind)
                            {
                                case JsClassElementKind.Method:
                                {
                                    var elementFunction = element.Value!;
                                    var methodObj = CompileClassElementFunction(key, elementFunction,
                                        visiblePrivateBindings,
                                        classLexicalIdentifier);
                                    EmitDefineClassMethod(targetReg, ctorReg, element, methodObj,
                                        hasComputedKeyPlan ? computedKeyPlan.KeyRegister : -1,
                                        element.IsStatic
                                            ? staticMemberPrivateBrandSourceReg
                                            : instanceMemberPrivateBrandSourceReg,
                                        element.IsStatic
                                            ? staticMemberInheritedPrivateBrandIds
                                            : instanceMemberInheritedPrivateBrandIds,
                                        ctorReg,
                                        visiblePrivateBrandMappings);
                                    break;
                                }
                                case JsClassElementKind.Getter:
                                case JsClassElementKind.Setter:
                                {
                                    var elementFunction = element.Value!;
                                    var accessorFunctionName =
                                        element.Kind == JsClassElementKind.Getter ? $"get {key}" : $"set {key}";
                                    var accessorFn = CompileClassElementFunction(accessorFunctionName, elementFunction,
                                        visiblePrivateBindings,
                                        classLexicalIdentifier);
                                    if (element.IsComputedKey)
                                    {
                                        EmitDefineClassAccessor(targetReg, ctorReg, element,
                                            element.Kind == JsClassElementKind.Getter ? accessorFn : null,
                                            element.Kind == JsClassElementKind.Setter ? accessorFn : null,
                                            hasComputedKeyPlan ? computedKeyPlan.KeyRegister : -1,
                                            element.IsStatic
                                                ? staticMemberPrivateBrandSourceReg
                                                : instanceMemberPrivateBrandSourceReg,
                                            element.IsStatic
                                                ? staticMemberInheritedPrivateBrandIds
                                                : instanceMemberInheritedPrivateBrandIds,
                                            ctorReg,
                                            visiblePrivateBrandMappings);
                                        break;
                                    }

                                    var accessorMaskKey = MakeClassAccessorMaskKey(key, element.IsStatic);
                                    var keyHasBothAccessorKinds =
                                        accessorMaskByKey.TryGetValue(accessorMaskKey, out var accessorMask) &&
                                        accessorMask == 3;
                                    if (!keyHasBothAccessorKinds)
                                    {
                                        EmitDefineClassAccessor(targetReg, ctorReg, element,
                                            element.Kind == JsClassElementKind.Getter ? accessorFn : null,
                                            element.Kind == JsClassElementKind.Setter ? accessorFn : null,
                                            hasComputedKeyPlan ? computedKeyPlan.KeyRegister : -1,
                                            element.IsStatic
                                                ? staticMemberPrivateBrandSourceReg
                                                : instanceMemberPrivateBrandSourceReg,
                                            element.IsStatic
                                                ? staticMemberInheritedPrivateBrandIds
                                                : instanceMemberInheritedPrivateBrandIds,
                                            ctorReg,
                                            visiblePrivateBrandMappings);
                                        break;
                                    }

                                    if (!pairedAccessorStateByKey.TryGetValue(accessorMaskKey, out var state))
                                    {
                                        state = new();
                                        pairedAccessorStateByKey[accessorMaskKey] = state;
                                    }

                                    if (element.Kind == JsClassElementKind.Getter)
                                        state.Getter = accessorFn;
                                    else
                                        state.Setter = accessorFn;

                                    if (state.Getter is not null && state.Setter is not null)
                                        EmitDefineClassAccessor(targetReg, ctorReg, element, state.Getter, state.Setter,
                                            hasComputedKeyPlan ? computedKeyPlan.KeyRegister : -1,
                                            element.IsStatic
                                                ? staticMemberPrivateBrandSourceReg
                                                : instanceMemberPrivateBrandSourceReg,
                                            element.IsStatic
                                                ? staticMemberInheritedPrivateBrandIds
                                                : instanceMemberInheritedPrivateBrandIds,
                                            ctorReg,
                                            visiblePrivateBrandMappings);
                                    break;
                                }
                                case JsClassElementKind.Field:
                                {
                                    if (!element.IsStatic)
                                        break;
                                    EmitStaticClassFieldInitializer(
                                        ctorReg,
                                        element,
                                        visiblePrivateBindings,
                                        classLexicalIdentifier,
                                        key,
                                        hasComputedKeyPlan ? computedKeyPlan.KeyRegister : -1,
                                        staticMemberInheritedPrivateBrandIds,
                                        ctorReg,
                                        visiblePrivateBrandMappings);
                                    break;
                                }
                                default:
                                    throw new NotSupportedException(
                                        $"Class element kind '{element.Kind}' is not supported in Okojo Phase 2.");
                            }
                        }

                        EmitLdaRegister(ctorReg);
                    }
                    finally
                    {
                        activeClassPrivateSourceScopes.Pop();
                    }
                }
                finally
                {
                    activeClassPrivateBindingScopes.Pop();
                }
            }
            finally
            {
                if (classLexicalInternalName is not null)
                    PopAliasScope();
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitClassStaticBlock(int ctorReg, CompilerIdentifierName? classLexicalIdentifier,
        JsClassElement element,
        int privateBrandSourceReg,
        IReadOnlyList<int>? inheritedPrivateBrandIds,
        int inheritedPrivateBrandSourceReg,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings)
    {
        if (element.StaticBlock is null)
            throw new InvalidOperationException("Class static block body is missing.");

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var staticBlockFunction = new JsFunctionExpression(
                null,
                Array.Empty<string>(),
                element.StaticBlock);
            var parameterPlan = FunctionParameterPlan.FromFunction(staticBlockFunction);
            var functionObj = CompileFunctionObject(
                null,
                parameterPlan,
                staticBlockFunction.Body,
                CreateFunctionShape(
                    false,
                    false,
                    false,
                    true,
                    true),
                sourceStartPosition: element.Position,
                sourceEndPosition: element.StaticBlock.EndPosition,
                useMethodEnvironmentCapture: true,
                classLexicalNameForMethodResolution: classLexicalIdentifier);
            if (functionObj.RequiresClosureBinding)
                requiresClosureBinding = true;

            EmitCreateClosureForMethodWithEnvironment(functionObj, ctorReg, ctorReg, privateBrandSourceReg,
                inheritedPrivateBrandIds, inheritedPrivateBrandSourceReg, explicitPrivateBrandMappings);
            var blockFnReg = AllocateTemporaryRegister();
            EmitStarRegister(blockFnReg);
            EmitRaw(JsOpCode.CallProperty, (byte)blockFnReg, (byte)ctorReg, 0, 0);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitStaticClassFieldInitializer(
        int ctorReg,
        JsClassElement element,
        IReadOnlyDictionary<string, PrivateFieldBinding> visiblePrivateBindings,
        CompilerIdentifierName? classLexicalIdentifier,
        string? fieldName,
        int computedKeyReg,
        IReadOnlyList<int>? inheritedPrivateBrandIds,
        int inheritedPrivateBrandSourceReg,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var valueReg = AllocateTemporaryRegister();
            if (element.FieldInitializer is not null)
            {
                var initializerFunction = CompileClassFieldInitializerFunction(
                    element.FieldInitializer,
                    element.Position,
                    element.Position,
                    visiblePrivateBindings,
                    classLexicalIdentifier,
                    fieldName);
                EmitCreateClosureForMethodWithEnvironment(
                    initializerFunction,
                    ctorReg,
                    ctorReg,
                    ctorReg,
                    inheritedPrivateBrandIds,
                    inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings);
                var initFnReg = AllocateTemporaryRegister();
                EmitStarRegister(initFnReg);
                EmitRaw(JsOpCode.CallProperty, (byte)initFnReg, (byte)ctorReg, 0, 0);
            }
            else
            {
                EmitLdaUndefined();
            }

            EmitStarRegister(valueReg);
            EmitDefineClassField(ctorReg, element, valueReg, computedKeyReg);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private JsBytecodeFunction CompileClassFieldInitializerFunction(
        JsExpression initializer,
        int sourceStartPosition,
        int sourceEndPosition,
        IReadOnlyDictionary<string, PrivateFieldBinding> visiblePrivateBindings,
        CompilerIdentifierName? classLexicalIdentifier,
        string? inferredName)
    {
        var initializerBody = new JsBlockStatement(new JsStatement[]
        {
            new JsReturnStatement(ApplyInferredNameIfNeeded(initializer, inferredName))
        }, true)
        {
            Position = sourceStartPosition,
            EndPosition = sourceEndPosition
        };

        var parameterPlan = FunctionParameterPlan.Empty();
        var functionObj = CompileFunctionObject(
            null,
            parameterPlan,
            initializerBody,
            CreateFunctionShape(
                false,
                false,
                false,
                true,
                true),
            sourceStartPosition: sourceStartPosition,
            sourceEndPosition: sourceEndPosition,
            useMethodEnvironmentCapture: true,
            classLexicalNameForMethodResolution: classLexicalIdentifier,
            classPrivateNameToBinding: visiblePrivateBindings,
            forceHasSuperReference: FieldInitializerUsesDirectSuper(initializer));
        if (functionObj.RequiresClosureBinding)
            requiresClosureBinding = true;
        return functionObj;
    }

    private void EmitStaticPrivateFieldInitializerOnTarget(
        int targetReg,
        in PrivateFieldBinding binding,
        JsExpression? initializer,
        int sourceStartPosition,
        int sourceEndPosition,
        IReadOnlyDictionary<string, PrivateFieldBinding> visiblePrivateBindings,
        CompilerIdentifierName? classLexicalIdentifier,
        string? sourceName,
        IReadOnlyList<int>? inheritedPrivateBrandIds,
        int inheritedPrivateBrandSourceReg,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var valueReg = AllocateTemporaryRegister();
            if (initializer is not null)
            {
                var initializerFunction = CompileClassFieldInitializerFunction(
                    initializer,
                    sourceStartPosition,
                    sourceEndPosition,
                    visiblePrivateBindings,
                    classLexicalIdentifier,
                    sourceName);
                EmitCreateClosureForMethodWithEnvironment(
                    initializerFunction,
                    targetReg,
                    targetReg,
                    targetReg,
                    inheritedPrivateBrandIds,
                    inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings);
                var initFnReg = AllocateTemporaryRegister();
                EmitStarRegister(initFnReg);
                EmitRaw(JsOpCode.CallProperty, (byte)initFnReg, (byte)targetReg, 0, 0);
            }
            else
            {
                EmitLdaUndefined();
            }

            EmitStarRegister(valueReg);
            EmitPrivateFieldOp(JsOpCode.InitPrivateField, targetReg, valueReg, binding.BrandId, binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private static bool FieldInitializerUsesDirectSuper(JsExpression initializer)
    {
        return initializer switch
        {
            JsSuperExpression => true,
            JsAssignmentExpression a => FieldInitializerUsesDirectSuper(a.Left) ||
                                        FieldInitializerUsesDirectSuper(a.Right),
            JsBinaryExpression b => FieldInitializerUsesDirectSuper(b.Left) || FieldInitializerUsesDirectSuper(b.Right),
            JsConditionalExpression c => FieldInitializerUsesDirectSuper(c.Test) ||
                                         FieldInitializerUsesDirectSuper(c.Consequent) ||
                                         FieldInitializerUsesDirectSuper(c.Alternate),
            JsCallExpression c => FieldInitializerUsesDirectSuper(c.Callee) ||
                                  c.Arguments.Any(FieldInitializerUsesDirectSuper),
            JsNewExpression n => FieldInitializerUsesDirectSuper(n.Callee) ||
                                 n.Arguments.Any(FieldInitializerUsesDirectSuper),
            JsMemberExpression m => FieldInitializerUsesDirectSuper(m.Object) ||
                                    (m.IsComputed && FieldInitializerUsesDirectSuper(m.Property)),
            JsSequenceExpression s => s.Expressions.Any(FieldInitializerUsesDirectSuper),
            JsSpreadExpression s => FieldInitializerUsesDirectSuper(s.Argument),
            JsIntrinsicCallExpression i => i.Arguments.Any(FieldInitializerUsesDirectSuper),
            JsParameterInitializerExpression p => FieldInitializerUsesDirectSuper(p.Expression),
            JsArrayExpression a => a.Elements.Any(e => e is not null && FieldInitializerUsesDirectSuper(e)),
            JsTemplateExpression t => t.Expressions.Any(FieldInitializerUsesDirectSuper),
            JsTaggedTemplateExpression tt => FieldInitializerUsesDirectSuper(tt.Tag) ||
                                             tt.Template.Expressions.Any(FieldInitializerUsesDirectSuper),
            JsObjectExpression o => o.Properties.Any(static property =>
                (property.IsComputed && property.ComputedKey is not null &&
                 FieldInitializerUsesDirectSuper(property.ComputedKey)) ||
                (property.Value is not null && FieldInitializerUsesDirectSuper(property.Value))),
            JsFunctionExpression f => f.IsArrow &&
                                      ((f.ParameterInitializers?.Any(i =>
                                           i is not null && ExpressionContainsSuperInCurrentFunction(i)) ?? false) ||
                                       StatementContainsSuperInCurrentFunction(f.Body)),
            JsClassExpression => false,
            JsUnaryExpression u => FieldInitializerUsesDirectSuper(u.Argument),
            JsUpdateExpression u => FieldInitializerUsesDirectSuper(u.Argument),
            JsYieldExpression y => y.Argument is not null && FieldInitializerUsesDirectSuper(y.Argument),
            JsAwaitExpression a => FieldInitializerUsesDirectSuper(a.Argument),
            _ => false
        };
    }

    private JsBytecodeFunction CompileClassElementFunction(string functionName, JsFunctionExpression functionExpr,
        IReadOnlyDictionary<string, PrivateFieldBinding> privateFieldBindingBySourceName,
        CompilerIdentifierName? classLexicalIdentifier = null)
    {
        var parameterPlan = FunctionParameterPlan.FromFunction(functionExpr);
        var functionObj = CompileFunctionObject(
            functionName,
            parameterPlan,
            functionExpr.Body,
            CreateFunctionShape(
                functionExpr.IsGenerator,
                functionExpr.IsAsync,
                functionExpr.IsArrow,
                true,
                true),
            sourceStartPosition: functionExpr.Position,
            useMethodEnvironmentCapture: true,
            classLexicalNameForMethodResolution: classLexicalIdentifier,
            classPrivateNameToBinding: privateFieldBindingBySourceName);
        if (functionObj.RequiresClosureBinding)
            requiresClosureBinding = true;
        return functionObj;
    }

    private void EmitDefineClassMethod(int targetReg, int classLexicalReg, JsClassElement element,
        JsBytecodeFunction methodObj, int computedKeyReg, int privateBrandSourceReg,
        IReadOnlyList<int>? inheritedPrivateBrandIds,
        int inheritedPrivateBrandSourceReg,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            var keyReg = argStart + 1;
            var valueReg = argStart + 2;

            EmitLdaRegister(targetReg);
            EmitStarRegister(argStart);
            EmitClassElementKeyToRegister(element, keyReg, computedKeyReg);

            EmitCreateClosureForMethodWithEnvironment(methodObj, targetReg, classLexicalReg, privateBrandSourceReg,
                inheritedPrivateBrandIds, inheritedPrivateBrandSourceReg, explicitPrivateBrandMappings);
            EmitStarRegister(valueReg);

            EmitCallRuntime(RuntimeId.DefineClassMethod, argStart, 3);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitDefineClassAccessor(int targetReg, int classLexicalReg, JsClassElement element,
        JsBytecodeFunction? getterObj, JsBytecodeFunction? setterObj, int computedKeyReg, int privateBrandSourceReg,
        IReadOnlyList<int>? inheritedPrivateBrandIds,
        int inheritedPrivateBrandSourceReg,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(4);
            var keyReg = argStart + 1;
            var getterReg = argStart + 2;
            var setterReg = argStart + 3;

            EmitLdaRegister(targetReg);
            EmitStarRegister(argStart);
            EmitClassElementKeyToRegister(element, keyReg, computedKeyReg);

            if (getterObj is not null)
                EmitCreateClosureForMethodWithEnvironment(getterObj, targetReg, classLexicalReg,
                    privateBrandSourceReg, inheritedPrivateBrandIds, inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings);
            else
                EmitLdaUndefined();

            EmitStarRegister(getterReg);

            if (setterObj is not null)
                EmitCreateClosureForMethodWithEnvironment(setterObj, targetReg, classLexicalReg,
                    privateBrandSourceReg, inheritedPrivateBrandIds, inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings);
            else
                EmitLdaUndefined();

            EmitStarRegister(setterReg);

            EmitCallRuntime(RuntimeId.DefineClassAccessor, argStart, 4);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitDefineClassField(int targetReg, JsClassElement element, int valueReg, int computedKeyReg = -1)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            var keyReg = argStart + 1;
            var valueArgReg = argStart + 2;

            EmitLdaRegister(targetReg);
            EmitStarRegister(argStart);
            EmitClassElementKeyToRegister(element, keyReg, computedKeyReg);
            EmitLdaRegister(valueReg);
            EmitStarRegister(valueArgReg);

            EmitCallRuntime(RuntimeId.DefineClassField, argStart, 3);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private static string MakeClassAccessorMaskKey(string name, bool isStatic)
    {
        return isStatic ? $"s:{name}" : $"i:{name}";
    }

    private void EmitClassElementKeyToRegister(JsClassElement element, int keyReg, int computedKeyReg = -1)
    {
        if (element.IsComputedKey)
        {
            if (computedKeyReg >= 0)
            {
                EmitLdaRegister(computedKeyReg);
            }
            else
            {
                if (element.ComputedKey is null)
                    throw new InvalidOperationException("Computed class element key expression is missing.");
                VisitExpression(element.ComputedKey);
            }
        }
        else
        {
            var keyIdx = builder.AddObjectConstant(element.Key!);
            EmitLdaStringConstantByIndex(keyIdx);
        }

        EmitStarRegister(keyReg);
    }
}
