using Okojo.Browser;
using Okojo.Hosting;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace OkojoHostEventLoopSandbox;

internal sealed class ThreadAffinityBrowserHost : IDisposable
{
    private static readonly HostTaskQueueKey[] SQueueOrder =
    [
        WebTaskQueueKeys.Timers, WebTaskQueueKeys.Messages, WebTaskQueueKeys.Network, HostingTaskQueueKeys.Default,
        WebTaskQueueKeys.Rendering
    ];

    private readonly SandboxAssets assets;
    private readonly ThreadAffinityHostLoop hostLoop;
    private readonly HttpClient httpClient;
    private readonly HostPump pump;

    private ThreadAffinityBrowserHost(
        SandboxAssets assets,
        JsRuntime runtime,
        HttpClient httpClient,
        ThreadAffinityHostLoop hostLoop)
    {
        this.assets = assets;
        this.Runtime = runtime;
        this.httpClient = httpClient;
        this.hostLoop = hostLoop;
        pump = runtime.CreateHostPump();
    }

    public JsRuntime Runtime { get; }

    public JsRealm MainRealm => Runtime.MainRealm;

    public void Dispose()
    {
        Runtime.Dispose();
        httpClient.Dispose();
        hostLoop.Dispose();
    }

    public static ThreadAffinityBrowserHost Create(SandboxAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var hostLoop = new ThreadAffinityHostLoop();
        var moduleLoader = new DemoModuleLoader(assets);
        var httpClient = new HttpClient(new DemoFetchHandler(assets.FetchPayloads));
        var runtime = JsRuntime.CreateBuilder()
            .UseLowLevelHost(host => host.UseTaskScheduler(hostLoop))
            .UseWebDelayScheduler(hostLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseAnimationFrameQueue(WebTaskQueueKeys.Rendering)
            .UseFetchCompletionQueue(WebTaskQueueKeys.Network)
            .UseModuleSourceLoader(moduleLoader)
            .UseBrowserGlobals(fetch => fetch.HttpClient = httpClient)
            .Build();

        return new(assets, runtime, httpClient, hostLoop);
    }

    public void PumpUntil(Func<bool> completed, int timeoutMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(completed);

        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var deadline = timeout == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : Environment.TickCount64 + checked((long)Math.Ceiling(timeout.TotalMilliseconds));

        while (timeout == Timeout.InfiniteTimeSpan || Environment.TickCount64 < deadline)
        {
            if (completed())
                return;

            var remaining = timeout == Timeout.InfiniteTimeSpan
                ? Timeout.InfiniteTimeSpan
                : TimeSpan.FromMilliseconds(Math.Max(1, deadline - Environment.TickCount64));

            _ = hostLoop.RunOneTurn(
                remaining < TimeSpan.FromMilliseconds(20) ? remaining : TimeSpan.FromMilliseconds(20),
                SQueueOrder,
                pump);

            if (completed())
                return;
        }

        throw new TimeoutException("The thread-affinity browser host sandbox timed out waiting for work to complete.");
    }

    public void RunScript(string scriptPath)
    {
        _ = Runtime.MainRealm.Eval(assets.ReadScript(scriptPath));
    }
}
