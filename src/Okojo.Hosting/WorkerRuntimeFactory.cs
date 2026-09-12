using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

public static class WorkerRuntimeFactory
{
    public static WorkerRuntime CreateWorkerRuntime(
        JsRuntime engine,
        Action<WorkerRuntimeOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(engine);
        return CreateWorkerRuntimeCore(engine.MainRealm, engine.CreateWorkerAgent, configure);
    }

    public static WorkerRuntime CreateWorkerRuntime(
        JsRealm ownerRealm,
        Action<WorkerRuntimeOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);
        return CreateWorkerRuntimeCore(
            ownerRealm,
            ownerRealm.Agent.CreateWorkerAgent,
            options =>
            {
                options.ModuleReferrer ??= ownerRealm.CurrentModuleResolvedId;
                configure?.Invoke(options);
            }
        );
    }

    private static WorkerRuntime CreateWorkerRuntimeCore(
        JsRealm scriptRealm,
        Func<Action<JsAgentOptions>?, JsAgent> createWorkerAgent,
        Action<WorkerRuntimeOptions>? configure
    )
    {
        var options = new WorkerRuntimeOptions();
        configure?.Invoke(options);

        var agent = createWorkerAgent(null);
        var realm = agent.MainRealm;
        var threadHost = options.StartBackgroundHost ? new JsAgentThreadHost(agent) : null;

        if (!string.IsNullOrEmpty(options.ScriptEntry))
        {
            if (options.ScriptType == WorkerScriptType.Module)
            {
                _ = realm.Import(options.ScriptEntry, options.ScriptReferrer);
            }
            else
            {
                var source = scriptRealm.LoadWorkerScript(
                    options.ScriptEntry,
                    options.ScriptReferrer
                );
                realm.Execute(source);
            }
        }

        var hostedWorker = new WorkerRuntime(agent, threadHost);
        if (options.StartBackgroundHost)
            hostedWorker.StartBackgroundHost();

        return hostedWorker;
    }
}
