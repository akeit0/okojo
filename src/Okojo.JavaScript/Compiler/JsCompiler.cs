using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

public static class JsCompiler
{
    internal static JsScript Compile(JsRealm realm, FlatAst ast)
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
