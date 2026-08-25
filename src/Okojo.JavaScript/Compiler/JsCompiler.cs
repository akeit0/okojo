using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

public static class JsCompiler
{
    public static JsScript Compile(JsRealm realm, JsProgram program)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(program);

        return new JsScriptCompiler(realm).Compile(program);
    }

    public static JsScript Compile(JsRealm realm, string source, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(source);

        return new JsScriptCompiler(realm).Compile(source, sourcePath);
    }
}
