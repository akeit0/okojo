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
        return CreateWorkerRuntimeCore(engine.CreateWorkerAgent, configure);
    }

    public static WorkerRuntime CreateWorkerRuntime(
        JsRealm ownerRealm,
        Action<WorkerRuntimeOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(ownerRealm);
        return CreateWorkerRuntimeCore(
            ownerRealm.Agent.CreateWorkerAgent,
            options =>
            {
                options.ModuleReferrer ??= ownerRealm.GetCurrentModuleResolvedIdOrNull();
                configure?.Invoke(options);
            }
        );
    }

    private static WorkerRuntime CreateWorkerRuntimeCore(
        Func<Action<JsAgentOptions>?, JsAgent> createWorkerAgent,
        Action<WorkerRuntimeOptions>? configure
    )
    {
        var options = new WorkerRuntimeOptions();
        configure?.Invoke(options);

        var agent = createWorkerAgent(null);
        var realm = agent.MainRealm;
        var threadHost = options.StartBackgroundHost ? new JsAgentThreadHost(agent) : null;

        if (!string.IsNullOrEmpty(options.ModuleEntry))
            _ = agent.EvaluateModule(realm, options.ModuleEntry, options.ModuleReferrer);

        var hostedWorker = new WorkerRuntime(agent, threadHost);
        if (options.StartBackgroundHost)
            hostedWorker.StartBackgroundHost();

        return hostedWorker;
    }
}
