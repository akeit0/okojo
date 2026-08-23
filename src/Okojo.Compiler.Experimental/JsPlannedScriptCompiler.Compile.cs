using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    public JsScript Compile(JsProgram program)
    {
        builder.SetSourceText(program.SourceText);
        builder.SetStrictDeclared(program.StrictDeclared);

        using var ast = FlatAstLowerer.Lower(program);
        using var collected = CompilerBindingCollector.Collect(ast);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        if (statements.Length == 0)
            builder.EmitLda(JsOpCode.LdaUndefined);

        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode =
                program.SourceText is null && program.SourcePath is null
                    ? null
                    : new SourceCode(program.SourceText, program.SourcePath),
            StrictDeclared = program.StrictDeclared,
        };
        script.BindAgent(Vm.Agent);
        return script;
    }
}
