using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    public JsScript Compile(JsProgram program)
    {
        builder.SetSourceText(program.SourceText);
        builder.SetStrictDeclared(program.StrictDeclared);

        using var collected = CompilerBindingCollector.Collect(program);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();

        for (var i = 0; i < program.Statements.Count; i++)
            EmitStatement(program.Statements[i]);

        if (program.Statements.Count == 0)
            builder.EmitLda(JsOpCode.LdaUndefined);

        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode = program.SourceText is null && program.SourcePath is null ? null : new SourceCode(program.SourceText, program.SourcePath),
            StrictDeclared = program.StrictDeclared
        };
        script.BindAgent(Vm.Agent);
        return script;
    }
}
