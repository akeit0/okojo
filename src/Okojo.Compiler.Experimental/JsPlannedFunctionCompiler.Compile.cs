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
        return CompileFunction(
            name,
            parameterPlan,
            body.StrictDeclared,
            ast,
            ast.Root,
            hasSelfBinding
        );
    }

    internal JsBytecodeFunction CompileFunction(
        string? name,
        FunctionParameterPlan parameterPlan,
        bool strictDeclared,
        FlatAst ast,
        int bodyRoot,
        bool hasSelfBinding = false
    )
    {
        builder.SetStrictDeclared(strictDeclared);
        InitializeParameterRegisterMap(parameterPlan);
        using var collected = CompilerBindingCollector.CollectFunction(
            name,
            -1,
            parameterPlan,
            ast,
            bodyRoot,
            hasSelfBinding
        );
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();

        ref readonly var root = ref ast[bodyRoot];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with { SourceCode = null, StrictDeclared = strictDeclared };
        script.BindAgent(Vm.Agent);
        return new JsBytecodeFunction(
            Vm,
            script,
            name ?? string.Empty,
            requiresClosureBinding: false,
            isStrict: strictDeclared,
            hasNewTarget: false,
            kind: JsBytecodeFunctionKind.Normal,
            isArrow: false,
            isMethod: false,
            formalParameterCount: parameterPlan.Names.Count,
            hasSimpleParameterList: parameterPlan.HasSimpleParameterList,
            isClassConstructor: false,
            isDerivedConstructor: false,
            hasEagerGeneratorParameterBinding: false,
            expectedArgumentCount: parameterPlan.FunctionLength
        );
    }

    private void InitializeParameterRegisterMap(FunctionParameterPlan parameterPlan)
    {
        parameterRegisterByName.Clear();
        for (var i = 0; i < parameterPlan.Bindings.Count; i++)
        {
            var binding = parameterPlan.Bindings[i];
            parameterRegisterByName.TryAdd(binding.Name, i);
            for (var j = 0; j < binding.BoundIdentifiers.Count; j++)
                parameterRegisterByName.TryAdd(binding.BoundIdentifiers[j].Name, i);
        }
    }
}
