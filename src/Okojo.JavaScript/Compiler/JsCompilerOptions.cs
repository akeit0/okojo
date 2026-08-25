using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler;

public static class JsCompilerOptions
{
    public static JsAgentOptions UseModuleCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ModuleExecutionCompiler = static (realm, ast, plan) =>
            new JsModuleCompiler(realm).CompileForExecution(ast);
        return options;
    }
}
