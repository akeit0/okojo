using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class JsPlannedCompilerOptions
{
    internal static JsAgentOptions UsePlannedModuleCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ModuleExecutionCompiler = static (realm, ast, plan) =>
        {
            if (
                plan.RequiresTopLevelAwait
                || plan.HasTopLevelUsingLike
                || plan.HasTopLevelAwaitUsingLike
            )
                throw new NotSupportedException(
                    "The planned module compiler does not support top-level await or resource management yet."
                );
            return new JsPlannedModuleCompiler(realm).CompileForExecution(ast);
        };
        return options;
    }
}
