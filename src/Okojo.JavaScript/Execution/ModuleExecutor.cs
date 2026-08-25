using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Execution;

internal static class ModuleExecutor
{
    internal static JsValue ExecuteProgram(
        JsRealm realm,
        ModuleExecutionCompilation? moduleCompilation,
        string? moduleSourcePath,
        string? moduleSourceText,
        JsIdentifierTable? moduleIdentifierTable,
        ModuleExecutionPlan executionPlan,
        IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings,
        bool waitForTopLevelAwaitCompletion = true
    )
    {
        _ = moduleSourcePath;
        _ = moduleSourceText;
        _ = moduleIdentifierTable;
        _ = moduleVariableBindings;

        if (moduleCompilation is null)
            throw new InvalidOperationException(
                "Module evaluation requires an instantiated compilation."
            );
        realm.Execute(moduleCompilation.Script, waitForTopLevelAwaitCompletion);
        var result = realm.Accumulator;

        if (
            executionPlan.RequiresTopLevelAwait
            && result.TryGetObject(out var resultObj)
            && resultObj is JsPromiseObject promise
        )
        {
            if (!waitForTopLevelAwaitCompletion)
                return result;

            while (promise.State == JsPromiseObject.PromiseState.Pending)
                realm.Agent.PumpJobs();

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return promise.Result;

            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Top-level await module rejected",
                "MODULE_TOP_LEVEL_AWAIT_REJECTED",
                promise.Result
            );
        }

        return result;
    }
}
