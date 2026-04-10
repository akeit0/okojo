using Okojo.Runtime;

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
        if (!ReferenceEquals(agent.Engine, engine))
            throw new InvalidOperationException("Agent does not belong to this engine.");

        return new(agent);
    }

    public static WorkerRuntime CreateWorkerRuntime(
        this JsRuntime engine,
        Action<WorkerRuntimeOptions>? configure = null)
    {
        return WorkerRuntimeFactory.CreateWorkerRuntime(engine, configure);
    }
}
