using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
{
    public JsBytecodeFunction CompileFunction(
        string? name,
        FunctionParameterPlan parameterPlan,
        JsBlockStatement body,
        bool hasSelfBinding = false)
    {
        builder.SetStrictDeclared(body.StrictDeclared);
        InitializeParameterRegisterMap(parameterPlan);
        using var collected = CompilerBindingCollector.CollectFunction(name, -1, parameterPlan, body, hasSelfBinding);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();

        for (var i = 0; i < body.Statements.Count; i++)
            EmitStatement(body.Statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode = null,
            StrictDeclared = body.StrictDeclared
        };
        script.BindAgent(Vm.Agent);
        return new JsBytecodeFunction(
            Vm,
            script,
            name ?? string.Empty,
            requiresClosureBinding: false,
            isStrict: body.StrictDeclared,
            hasNewTarget: false,
            kind: JsBytecodeFunctionKind.Normal,
            isArrow: false,
            isMethod: false,
            formalParameterCount: parameterPlan.Names.Count,
            hasSimpleParameterList: parameterPlan.HasSimpleParameterList,
            isClassConstructor: false,
            isDerivedConstructor: false,
            hasEagerGeneratorParameterBinding: false,
            expectedArgumentCount: parameterPlan.FunctionLength);
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
