using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class JsPlannedModuleCompiler(JsRealm realm) : JsPlannedCompilerBase(realm)
{
    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = FlatJavaScriptParser.ParseModule(source, sourcePath);
        builder.SetSourceText(ast.SourceText);
        strictDeclared = true;
        builder.SetStrictDeclared(true);
        using var collected = CompilerBindingCollector.Collect(ast);
        using var plan = CompilerStoragePlanner.Plan(collected, ast);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();
        EmitScopeLexicalHoleInitialization();
        EmitDeclarationPrologue(ast, ast.Root);

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode = new SourceCode(ast.SourceText, sourcePath),
            StrictDeclared = true,
        };
        script.BindAgent(Vm.Agent);
        return script;
    }
}
