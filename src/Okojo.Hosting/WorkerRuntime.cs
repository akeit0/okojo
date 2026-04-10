using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class WorkerRuntime : IDisposable
{
    private readonly JsAgentThreadHost? threadHost;
    private bool disposed;

    internal WorkerRuntime(JsRuntime engine, JsAgent agent, JsAgentThreadHost? threadHost)
    {
        Engine = engine;
        Agent = agent;
        Realm = agent.MainRealm;
        Pump = new(agent);
        this.threadHost = threadHost;
    }

    public JsRuntime Engine { get; }
    public JsAgent Agent { get; }
    public JsRealm Realm { get; }
    public HostPump Pump { get; }
    public bool IsBackgroundHostRunning => threadHost?.IsRunning == true;

    public void Dispose()
    {
        if (disposed)
            return;

        Terminate();
        threadHost?.Dispose();
        disposed = true;
    }

    public void StartBackgroundHost()
    {
        ThrowIfDisposed();
        if (threadHost is null)
            throw new InvalidOperationException("This hosted worker was not configured with a background host.");

        threadHost.Start();
    }

    public bool StopBackgroundHost(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return threadHost?.Stop(timeout) ?? true;
    }

    public void PumpUntilIdle(int maxTurns = 1024)
    {
        ThrowIfDisposed();
        Pump.PumpUntilIdle(maxTurns);
    }

    public JsValue Eval(string source)
    {
        ThrowIfDisposed();
        return Realm.Eval(source);
    }

    public JsValue LoadModule(JsRealm ownerRealm, string specifier)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ownerRealm);
        ArgumentNullException.ThrowIfNull(specifier);
        return ownerRealm.BridgeFromOtherRealm(Agent.EvaluateModule(Realm, specifier,
            ownerRealm.GetCurrentModuleResolvedIdOrNull()));
    }

    public void Terminate()
    {
        if (disposed)
            return;

        Agent.Terminate();
        if (threadHost is not null)
            _ = threadHost.Stop(TimeSpan.FromSeconds(2));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
