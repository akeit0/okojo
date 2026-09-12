using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

public static class JsCompiler
{
    /// <summary>Parses and emits a reusable unit without allocating a realm or linking runtime state.</summary>
    public static JsCompilationUnit CompileUnit(string source, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new JsScriptCompiler(new CompileCollectionPool()).CompileUnit(source, sourcePath);
    }

    /// <summary>Compiles an executable module body; module-loader binding plans remain host-owned.</summary>
    public static JsCompilationUnit CompileModuleUnit(string source, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var ast = JavaScriptParser.ParseModule(source, sourcePath);
        return new JsModuleCompiler(new CompileCollectionPool()).CompileUnit(ast);
    }

    internal static JsScript Compile(JsRealm realm, JsAst ast)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(ast);

        using (ast)
            return new JsScriptCompiler(realm).Compile(ast, ast.SourcePath);
    }

    public static JsScript Compile(JsRealm realm, string source, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(source);

        return new JsScriptCompiler(realm).Compile(source, sourcePath);
    }
}
