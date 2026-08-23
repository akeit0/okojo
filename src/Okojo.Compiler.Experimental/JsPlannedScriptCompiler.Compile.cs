using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    public JsScript Compile(JsProgram program)
    {
        using var ast = FlatAstLowerer.Lower(program);
        ast.StrictDeclared = program.StrictDeclared;
        return Compile(ast, program.SourcePath);
    }

    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = FlatJavaScriptParser.ParseScript(source, sourcePath);
        return Compile(ast, sourcePath);
    }

    private JsScript Compile(FlatAst ast, string? sourcePath)
    {
        builder.SetSourceText(ast.SourceText);
        builder.SetStrictDeclared(ast.StrictDeclared);
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
                string.IsNullOrEmpty(ast.SourceText) && sourcePath is null
                    ? null
                    : new SourceCode(ast.SourceText, sourcePath),
            StrictDeclared = ast.StrictDeclared,
        };
        script.BindAgent(Vm.Agent);
        return script;
    }
}
