using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class JsPlannedCompilerOptions
{
    internal static JsAgentOptions UsePlannedModuleCompiler(this JsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ModuleExecutionCompiler = static (realm, source, sourcePath, plan) =>
        {
            if (
                plan.RequiresTopLevelAwait
                || plan.HasTopLevelUsingLike
                || plan.HasTopLevelAwaitUsingLike
            )
                throw new NotSupportedException(
                    "The planned module compiler does not support top-level await or resource management yet."
                );
            for (var i = 0; i < plan.Operations.Count; i++)
            {
                var operation = plan.Operations[i];
                if (
                    operation.Kind == ModuleExecutionOpKind.InitializeHoistedDefaultExport
                    || operation.Statement is JsFunctionDeclaration declaration
                        && plan.ExportLocalByName.Values.Contains(
                            declaration.Name,
                            StringComparer.Ordinal
                        )
                )
                    throw new NotSupportedException(
                        "The planned module compiler does not own hoisted export instantiation yet."
                    );
            }
            return new JsPlannedModuleCompiler(realm).Compile(source, sourcePath);
        };
        return options;
    }
}
