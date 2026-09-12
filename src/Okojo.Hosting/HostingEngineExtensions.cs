using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

public static class HostingEngineExtensions
{
    public static HostPump CreateHostPump(this JsRuntime engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new(engine.MainAgent);
    }

    public static HostPump CreateHostPump(this JsRuntime engine, JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(agent);
        if (!engine.Agents.Contains(agent))
            throw new InvalidOperationException("Agent does not belong to this engine.");

        return new(agent);
    }

    public static WorkerRuntime CreateWorkerRuntime(
        this JsRuntime engine,
        Action<WorkerRuntimeOptions>? configure = null
    )
    {
        return WorkerRuntimeFactory.CreateWorkerRuntime(engine, configure);
    }
}
