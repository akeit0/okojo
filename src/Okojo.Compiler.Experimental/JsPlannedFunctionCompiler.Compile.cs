using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
{
    public JsBytecodeFunction CompileFunction(
        string? name,
        FunctionParameterPlan parameterPlan,
        JsBlockStatement body,
        bool hasSelfBinding = false
    )
    {
        using var ast = FlatAstLowerer.Lower(body);
        InitializeParameterRegisterMap(parameterPlan);
        using var collected = CompilerBindingCollector.CollectFunction(
            name,
            -1,
            parameterPlan,
            ast,
            ast.Root,
            hasSelfBinding
        );
        return CompileFunctionCore(
            new FunctionCompileMetadata(
                name ?? string.Empty,
                body.StrictDeclared,
                parameterPlan.Names.Count,
                parameterPlan.HasSimpleParameterList,
                parameterPlan.FunctionLength,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false
            ),
            collected,
            ast,
            ast.Root,
            null
        );
    }

    internal JsBytecodeFunction CompileFunction(
        FlatAst ast,
        in FlatFunctionInfo function,
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
        FlatAst ast,
        int bodyRoot,
        FlatFunctionInfo? flatFunction
    )
    {
        hasNewTarget = false;
        isGenerator = metadata.IsGenerator;
        isAsync = metadata.IsAsync;
        if (!metadata.HasSimpleParameterList && flatFunction is null)
            throw new NotSupportedException("Advanced parameters require flat function metadata.");
        strictDeclared = metadata.StrictDeclared;
        returnInferredNameStringIndex = flatFunction?.ReturnInferredNameStringIndex ?? -1;
        builder.SetStrictDeclared(strictDeclared);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        EmitGeneratorPrologue();
        for (var i = 0; i < metadata.ParameterCount; i++)
            builder.AllocatePinnedRegister();
        InitializeRootBindings(parameterRegisterByName);
        var superBaseContextSlot = FindSuperBaseContextSlot();
        initializeParametersInPrologue = !metadata.HasSimpleParameterList;
        externalCaptureContextDepthOffset =
            metadata.HasSuperPropertyReference && !metadata.IsArrow ? 1 : 0;
        EmitFunctionContextSetup();
        EmitScopeLexicalHoleInitialization();
        if (superBaseContextSlot >= 0)
            EmitSuperBaseContextInitialization(metadata, superBaseContextSlot);
        EmitFunctionSelfBinding();
        EmitArgumentsBinding();
        if (flatFunction is { } function)
            EmitParameterPrologue(ast, function);
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

        ref readonly var root = ref ast[bodyRoot];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        PatchGeneratorSwitchTable();
        var script = builder.ToScript() with
        {
            SourceCode = null,
            StrictDeclared = metadata.StrictDeclared,
        };
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

    private void InitializeParameterRegisterMap(FunctionParameterPlan parameterPlan)
    {
        parameterRegisterByName.Clear();
        parameterNames.Clear();
        for (var i = 0; i < parameterPlan.Bindings.Count; i++)
        {
            var binding = parameterPlan.Bindings[i];
            parameterNames.Add(binding.IsPattern ? null : binding.Name);
            if (!binding.IsPattern)
                parameterRegisterByName[binding.Name] = i;
        }
    }

    private void InitializeParameterRegisterMap(FlatAst ast, in FlatFunctionInfo function)
    {
        parameterRegisterByName.Clear();
        parameterNames.Clear();
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
        for (var i = 0; i < parameterNames.Count; i++)
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
