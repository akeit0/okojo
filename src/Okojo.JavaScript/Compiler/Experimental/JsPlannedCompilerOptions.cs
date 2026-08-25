using Okojo.JavaScript.Execution;

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

    public static JsAgentOptions UseDirectFlatCompilers(this JsAgentOptions options) =>
        options.UsePlannedModuleCompiler();
}
