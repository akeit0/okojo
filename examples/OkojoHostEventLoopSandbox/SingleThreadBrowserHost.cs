using Okojo.Browser;
using Okojo.Hosting;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace OkojoHostEventLoopSandbox;

internal sealed class SingleThreadBrowserHost : IDisposable
{
    private static readonly HostTaskQueueKey[] SQueueOrder =
    [
        WebTaskQueueKeys.Timers, WebTaskQueueKeys.Messages, WebTaskQueueKeys.Network, HostingTaskQueueKeys.Default,
        WebTaskQueueKeys.Rendering
    ];

    private readonly SandboxAssets assets;
    private readonly ManualHostEventLoop eventLoop;
    private readonly HttpClient httpClient;
    private readonly HostPump pump;

    private SingleThreadBrowserHost(SandboxAssets assets, JsRuntime runtime, HttpClient httpClient,
        ManualHostEventLoop eventLoop)
    {
        this.assets = assets;
        this.Runtime = runtime;
        this.httpClient = httpClient;
        this.eventLoop = eventLoop;
        pump = runtime.CreateHostPump();
    }

    public JsRuntime Runtime { get; }

    public JsRealm MainRealm => Runtime.MainRealm;

    public void Dispose()
    {
        Runtime.Dispose();
        httpClient.Dispose();
    }

    public static SingleThreadBrowserHost Create(SandboxAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var timeProvider = TimeProvider.System;
        var eventLoop = new ManualHostEventLoop(timeProvider);
        var moduleLoader = new DemoModuleLoader(assets);
        var httpClient = new HttpClient(new DemoFetchHandler(assets.FetchPayloads));
        var runtime = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseWebDelayScheduler(eventLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseAnimationFrameQueue(WebTaskQueueKeys.Rendering)
            .UseFetchCompletionQueue(WebTaskQueueKeys.Network)
            .UseModuleSourceLoader(moduleLoader)
            .UseBrowserGlobals(fetch => fetch.HttpClient = httpClient)
            .Build();

        return new(assets, runtime, httpClient, eventLoop);
    }

    public void PumpUntil(Func<bool> completed, int timeoutMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(completed);

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (completed())
                return;

            _ = HostTurnRunner.RunTurn(eventLoop, pump, SQueueOrder);

            if (completed())
                return;

            Thread.Sleep(5);
        }

        throw new TimeoutException("The single-thread browser host sandbox timed out waiting for work to complete.");
    }

    public void RunScript(string scriptPath)
    {
        _ = Runtime.MainRealm.Eval(assets.ReadScript(scriptPath));
    }
}
