using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

public static class JsPlannedCompilerOptions
{
    public static JsAgentOptions UsePlannedModuleCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ModuleExecutionCompiler = static (realm, ast, plan) =>
            new JsPlannedModuleCompiler(realm).CompileForExecution(ast);
        return options;
    }

    public static JsAgentOptions UseDirectFlatScriptCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ScriptExecutionCompiler = CompileDirectFlatWithProductionFallback;
        return options;
    }

    public static JsAgentOptions UseDirectFlatCompilers(this JsAgentOptions options) =>
        options.UseDirectFlatScriptCompiler().UsePlannedModuleCompiler();

    private static JsScript CompileDirectFlatWithProductionFallback(JsRealm realm, string source)
    {
        try
        {
            using var ast = FlatJavaScriptParser.ParseScript(source);
            return new JsPlannedScriptCompiler(realm).Compile(ast, null);
        }
        catch (JsParseException)
        {
            var program = JavaScriptParser.ParseScript(source);
            return JsCompiler.Compile(realm, program);
        }
    }
}
