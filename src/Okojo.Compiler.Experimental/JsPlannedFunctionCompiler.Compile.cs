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
                parameterPlan.FunctionLength
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
        bool hasSelfBinding = false
    )
    {
        var name = ast.GetString(function.NameStringIndex);
        InitializeParameterRegisterMap(ast, function);
        using var collected = CompilerBindingCollector.CollectFunction(
            ast,
            function,
            bodyRoot,
            hasSelfBinding
        );
        return CompileFunctionCore(
            new FunctionCompileMetadata(
                name,
                function.StrictDeclared,
                function.ParameterCount,
                function.HasSimpleParameterList,
                function.FunctionLength
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
        if (!metadata.HasSimpleParameterList && flatFunction is null)
            throw new NotSupportedException("Advanced parameters require flat function metadata.");
        builder.SetStrictDeclared(metadata.StrictDeclared);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        for (var i = 0; i < metadata.ParameterCount; i++)
            builder.AllocatePinnedRegister();
        InitializeRootBindings(parameterRegisterByName);
        initializeParametersInPrologue = !metadata.HasSimpleParameterList;
        EmitFunctionContextSetup();
        if (flatFunction is { } function)
            EmitParameterPrologue(ast, function);

        ref readonly var root = ref ast[bodyRoot];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode = null,
            StrictDeclared = metadata.StrictDeclared,
        };
        script.BindAgent(Vm.Agent);
        return new JsBytecodeFunction(
            Vm,
            script,
            metadata.Name,
            requiresClosureBinding: false,
            isStrict: metadata.StrictDeclared,
            hasNewTarget: false,
            kind: JsBytecodeFunctionKind.Normal,
            isArrow: false,
            isMethod: false,
            formalParameterCount: metadata.ParameterCount,
            hasSimpleParameterList: metadata.HasSimpleParameterList,
            isClassConstructor: false,
            isDerivedConstructor: false,
            hasEagerGeneratorParameterBinding: false,
            expectedArgumentCount: metadata.FunctionLength
        );
    }

    private void InitializeParameterRegisterMap(FunctionParameterPlan parameterPlan)
    {
        parameterRegisterByName.Clear();
        for (var i = 0; i < parameterPlan.Bindings.Count; i++)
        {
            var binding = parameterPlan.Bindings[i];
            if (!binding.IsPattern)
                parameterRegisterByName[binding.Name] = i;
        }
    }

    private void InitializeParameterRegisterMap(FlatAst ast, in FlatFunctionInfo function)
    {
        parameterRegisterByName.Clear();
        var parameters = ast.GetParameters(function);
        for (var i = 0; i < parameters.Length; i++)
            if (
                parameters[i].Kind
                is JsFormalParameterBindingKind.Plain
                    or JsFormalParameterBindingKind.Rest
            )
                parameterRegisterByName[ast.GetString(parameters[i].NameStringIndex)] = i;
    }

    private readonly record struct FunctionCompileMetadata(
        string Name,
        bool StrictDeclared,
        int ParameterCount,
        bool HasSimpleParameterList,
        int FunctionLength
    );
}
