using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class JsAgentThreadHost : IDisposable
{
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly HostPump pump;
    private Thread? thread;

    public JsAgentThreadHost(JsAgent agent)
        : this(new HostPump(agent))
    {
    }

    public JsAgentThreadHost(HostPump pump)
    {
        ArgumentNullException.ThrowIfNull(pump);
        this.pump = pump;
    }

    public JsAgent Agent => pump.Agent;
    public bool IsRunning => thread is { IsAlive: true };

    public void Dispose()
    {
        _ = Stop(TimeSpan.FromSeconds(2));
        cancellationSource.Dispose();
    }

    public void Start()
    {
        if (thread is { IsAlive: true })
            throw new InvalidOperationException("Agent host thread is already running.");

        var localThread = new Thread(() => pump.Run(cancellationSource.Token));
        localThread.Start();
        thread = localThread;
    }

    public bool Stop(TimeSpan timeout)
    {
        cancellationSource.Cancel();
        return thread is null || thread.Join(timeout);
    }
}
