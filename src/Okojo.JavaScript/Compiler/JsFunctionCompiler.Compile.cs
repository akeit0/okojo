using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal sealed partial class JsFunctionCompiler
{
    internal JsBytecodeFunction CompileFunction(
        JsAst ast,
        in JsFunctionInfo function,
        int bodyRoot,
        bool hasSelfBinding = false,
        string? inferredName = null,
        int instanceFieldClassIndex = -1
    )
    {
        var declaredName = ast.GetString(function.NameStringIndex);
        var name = declaredName.Length == 0 ? inferredName ?? string.Empty : declaredName;
        InitializeParameterRegisterMap(ast, function);
        InstanceFieldClassIndex = instanceFieldClassIndex;
        using var collected = CompilerBindingCollector.CollectFunction(
            ast,
            function,
            bodyRoot,
            hasSelfBinding,
            instanceFieldClassIndex
        );
        return CompileFunctionCore(
            new FunctionCompileMetadata(
                name,
                function.StrictDeclared,
                function.ParameterCount,
                function.HasSimpleParameterList,
                function.FunctionLength,
                function.IsMethod,
                function.IsArrow,
                function.IsGenerator,
                function.IsAsync,
                function.IsClassConstructor,
                function.IsDerivedConstructor,
                function.EmitImplicitSuperForwardAll,
                function.HasSuperPropertyReference
            ),
            collected,
            ast,
            bodyRoot,
            function
        );
    }

    private JsBytecodeFunction CompileFunctionCore(
        in FunctionCompileMetadata metadata,
        CompilerBindingCollectionResult collected,
        JsAst ast,
        int bodyRoot,
        JsFunctionInfo? functionInfo
    )
    {
        hasNewTarget = false;
        isGenerator = metadata.IsGenerator;
        isAsync = metadata.IsAsync;
        if (!metadata.HasSimpleParameterList && functionInfo is null)
            throw new NotSupportedException("Advanced parameters require function metadata.");
        strictDeclared = metadata.StrictDeclared;
        returnInferredNameStringIndex = functionInfo?.ReturnInferredNameStringIndex ?? -1;
        returnInferredNameFromFirstParameter =
            functionInfo?.ReturnInferredNameFromFirstParameter ?? false;
        builder.SetStrictDeclared(strictDeclared);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        EmitGeneratorPrologue();
        for (var i = 0; i < metadata.ParameterCount; i++)
            builder.AllocatePinnedRegister();
        InitializeRootBindings(parameterRegisterByName);
        if (metadata.HasSimpleParameterList)
            MarkInitializedParameters();
        PrepareLexicalHoleInitializationSkips(ast, bodyRoot);
        if (!metadata.IsArrow && metadata.IsDerivedConstructor)
        {
            derivedThisContextSlot = rootContextSlotCount;
            rootContextSlotCount++;
            var rootScope = activeScopes.Pop();
            activeScopes.Push(
                new ActiveScope(
                    rootScope.ScopeId,
                    rootScope.Bindings,
                    rootContextSlotCount,
                    rootScope.DebugStartPc
                )
            );
        }
        var superBaseContextSlot = FindSuperBaseContextSlot();
        initializeParametersInPrologue = !metadata.HasSimpleParameterList;
        externalCaptureContextDepthOffset =
            metadata.HasSuperPropertyReference && !metadata.IsArrow ? 1 : 0;
        EmitFunctionContextSetup();
        if (derivedThisContextSlot >= 0)
        {
            builder.EmitLda(JsOpCode.LdaTheHole);
            EmitStaCurrentContextSlot(derivedThisContextSlot);
        }
        var argumentsMaterialized = -1;
        if (HasSyntheticArgumentsBinding())
        {
            EmitArgumentsObjectCreation();
            argumentsMaterialized = builder.AllocatePinnedRegister();
            EmitStar(argumentsMaterialized);
        }
        var restMaterialized = -1;
        if (
            functionInfo is { } restFn
            && !restFn.HasSimpleParameterList
            && restFn.RestParameterIndex >= 0
        )
        {
            if ((uint)restFn.RestParameterIndex > byte.MaxValue)
                throw new NotSupportedException(
                    "Rest parameter index exceeds byte operand capacity."
                );
            builder.Emit(JsOpCode.CreateRestParameter, (byte)restFn.RestParameterIndex);
            restMaterialized = builder.AllocatePinnedRegister();
            EmitStar(restMaterialized);
        }
        EmitScopeLexicalHoleInitialization();
        if (superBaseContextSlot >= 0)
            EmitSuperBaseContextInitialization(metadata, superBaseContextSlot);
        if (argumentsMaterialized >= 0)
            EmitArgumentsBinding(argumentsMaterialized);
        EmitFunctionSelfBinding();
        if (functionInfo is { } function)
            EmitParameterPrologue(ast, function, restMaterialized);
        EmitDeclarationPrologue(ast, bodyRoot);
        if (metadata.EmitImplicitSuperForwardAll)
            builder.EmitCallRuntime((int)RuntimeId.CallSuperConstructorForwardAll, 0, 0);
        if (
            InstanceFieldClassIndex >= 0
            && (!metadata.IsDerivedConstructor || metadata.EmitImplicitSuperForwardAll)
        )
            EmitInstanceFieldInitializers(ast, InstanceFieldClassIndex);
        if (isGenerator && !metadata.HasSimpleParameterList)
            EmitGeneratorPrestartSuspend();

        var rootIndex = bodyRoot;
        var bodyOffset = ast[rootIndex].Arg0;
        var bodyCount = ast[rootIndex].Arg1;
        EmitBodyStatementListWithResources(
            ast,
            bodyOffset,
            bodyCount,
            () => EmitRootStatementList(ast, bodyOffset, bodyCount)
        );

        if (!BodyEndsAbruptly(ast, bodyOffset, bodyCount))
        {
            builder.EmitLda(JsOpCode.LdaUndefined);
            builder.Emit(JsOpCode.Return);
        }
        EmitRootLocalDebugInfos();
        PatchGeneratorSwitchTable();
        var functionSourceStart = functionInfo?.Position ?? -1;
        var functionSourceEnd = functionInfo?.EndPosition ?? -1;
        FunctionSourceTextSegment? functionSourceText = null;
        if (
            functionInfo is { } sourceFn
            && !string.IsNullOrEmpty(ast.SourceText)
            && (uint)functionSourceStart <= (uint)ast.SourceText.Length
            && functionSourceEnd > functionSourceStart
            && functionSourceEnd <= ast.SourceText.Length
        )
        {
            var start = functionSourceStart;
            while (start < functionSourceEnd && char.IsWhiteSpace(ast.SourceText[start]))
                start++;
            var end = functionSourceEnd;
            while (end > start && char.IsWhiteSpace(ast.SourceText[end - 1]))
                end--;
            if (end > start)
                functionSourceText = new FunctionSourceTextSegment(
                    ast.SourceText,
                    start,
                    end - start
                );
        }

        var script = builder.ToScript(
            sourceCode: scriptSourceCode,
            functionSourceText: functionSourceText ?? default
        );
        script.BindAgent(Vm.Agent);
        var result = new JsBytecodeFunction(
            Vm,
            script,
            metadata.Name,
            requiresClosureBinding: false,
            isStrict: metadata.StrictDeclared,
            hasNewTarget: hasNewTarget,
            kind: isGenerator && isAsync ? JsBytecodeFunctionKind.AsyncGenerator
                : isGenerator ? JsBytecodeFunctionKind.Generator
                : isAsync ? JsBytecodeFunctionKind.Async
                : JsBytecodeFunctionKind.Normal,
            isArrow: metadata.IsArrow,
            isMethod: metadata.IsMethod,
            formalParameterCount: metadata.ParameterCount,
            hasSimpleParameterList: metadata.HasSimpleParameterList,
            isClassConstructor: metadata.IsClassConstructor,
            isDerivedConstructor: metadata.IsDerivedConstructor,
            hasEagerGeneratorParameterBinding: isGenerator && !metadata.HasSimpleParameterList,
            expectedArgumentCount: metadata.FunctionLength
        );
        result.ArgumentsMappedSlots = BuildArgumentsMappedSlots(metadata);
        result.SuperBaseContextSlot = superBaseContextSlot;
        result.DerivedThisContextSlot = derivedThisContextSlot;
        if (
            metadata.IsArrow
            && inheritedCaptures.TryGetValue(DerivedThisBindingName, out var derivedThis)
        )
        {
            result.LexicalThisContextSlot = derivedThis.Slot;
            result.LexicalThisContextDepth = derivedThis.Depth;
        }
        // Return all pooled collections before handing the finished script out;
        // skipping this made every nested-function compile pay full pool-rent cost.
        builder.Dispose();
        ReleaseCompilerStorage();
        return result;
    }

    private int FindSuperBaseContextSlot()
    {
        var bindings = activeScopes.Peek().Bindings;
        for (var i = 0; i < bindings.Count; i++)
            if (bindings[i].Planned.Kind == CompilerCollectedBindingKind.SuperBase)
                return bindings[i].Planned.StorageIndex;
        return -1;
    }

    private void EmitSuperBaseContextInitialization(
        in FunctionCompileMetadata metadata,
        int superBaseContextSlot
    )
    {
        if (metadata.IsArrow)
        {
            if (
                !TryResolveExternalBinding(
                    CompilerBindingCollector.SuperBaseBindingName,
                    out var capture,
                    out var depth
                )
            )
                throw new InvalidOperationException("Arrow super property has no home object.");
            EmitLdaContextSlot(capture.Slot, depth);
        }
        else
        {
            EmitLdaContextSlot(0, 1);
            builder.EmitCallRuntime((int)RuntimeId.GetObjectPrototypeForSuper, 0, 0);
        }
        EmitStaCurrentContextSlot(superBaseContextSlot);
    }

    private void InitializeParameterRegisterMap(JsAst ast, in JsFunctionInfo function)
    {
        EnsureParameterMaps();
        parameterRegisterByName!.Clear();
        parameterNames!.Clear();
        var parameters = ast.GetParameters(function);
        for (var i = 0; i < parameters.Length; i++)
        {
            parameterNames.Add(
                parameters[i].Kind
                    is JsFormalParameterBindingKind.Plain
                        or JsFormalParameterBindingKind.Rest
                    ? ast.GetString(parameters[i].NameStringIndex)
                    : null
            );
            if (
                parameters[i].Kind
                is JsFormalParameterBindingKind.Plain
                    or JsFormalParameterBindingKind.Rest
            )
                parameterRegisterByName[ast.GetString(parameters[i].NameStringIndex)] = i;
        }
    }

    private int[]? BuildArgumentsMappedSlots(in FunctionCompileMetadata metadata)
    {
        var rootBindings = GetPlannedBindings(0);
        var hasArguments = false;
        for (var i = 0; i < rootBindings.Length; i++)
            if (rootBindings[i].Kind == CompilerCollectedBindingKind.Arguments)
            {
                hasArguments = true;
                break;
            }
        if (!hasArguments || !metadata.HasSimpleParameterList || metadata.ParameterCount == 0)
            return null;

        var slots = new int[metadata.ParameterCount];
        Array.Fill(slots, -1);
        for (var i = 0; i < parameterNames!.Count; i++)
        {
            var name = parameterNames[i];
            if (name is null || parameterNames.LastIndexOf(name) != i)
                continue;
            for (var j = 0; j < rootBindings.Length; j++)
                if (
                    rootBindings[j].Kind == CompilerCollectedBindingKind.Parameter
                    && rootBindings[j].StorageKind == CompilerPlannedStorageKind.ContextSlot
                    && string.Equals(rootBindings[j].Name, name, StringComparison.Ordinal)
                )
                {
                    slots[i] = rootBindings[j].StorageIndex;
                    break;
                }
        }
        return slots;
    }

    private readonly record struct FunctionCompileMetadata(
        string Name,
        bool StrictDeclared,
        int ParameterCount,
        bool HasSimpleParameterList,
        int FunctionLength,
        bool IsMethod,
        bool IsArrow,
        bool IsGenerator,
        bool IsAsync,
        bool IsClassConstructor,
        bool IsDerivedConstructor,
        bool EmitImplicitSuperForwardAll,
        bool HasSuperPropertyReference
    );
}
