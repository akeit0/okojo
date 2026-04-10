using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Runtime;

internal static class ModuleExecutor
{
    internal static JsValue ExecuteProgram(
        JsRealm realm,
        string? moduleSourcePath,
        string? moduleSourceText,
        JsIdentifierTable? moduleIdentifierTable,
        ModuleExecutionPlan executionPlan,
        IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings,
        bool waitForTopLevelAwaitCompletion = true)
    {
        using var compiler = JsCompiler.CreateForModuleExecution(
            realm,
            moduleVariableBindings);

        JsValue result;
        if (executionPlan.RequiresTopLevelAwait)
        {
            var compiled = compiler.CompileModuleExecutionAsync(
                executionPlan,
                moduleSourceText,
                moduleSourcePath,
                moduleIdentifierTable);
            realm.Execute(compiled, waitForTopLevelAwaitCompletion);
            result = realm.Accumulator;
        }
        else
        {
            var compiled = compiler.CompileModuleExecution(
                executionPlan,
                moduleSourceText,
                moduleSourcePath,
                moduleIdentifierTable);
            realm.Execute(compiled);
            result = realm.Accumulator;
        }

        if (executionPlan.RequiresTopLevelAwait &&
            result.TryGetObject(out var resultObj) &&
            resultObj is JsPromiseObject promise)
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
                promise.Result);
        }

        return result;
    }
}
