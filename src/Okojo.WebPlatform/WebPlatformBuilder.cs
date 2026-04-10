using System.Runtime.CompilerServices;
using Okojo.Hosting;
using Okojo.Runtime;

namespace Okojo.WebPlatform;

public sealed class WebPlatformBuilder
{
    private static readonly ConditionalWeakTable<JsRuntimeOptions, State> SStates = new();

    private readonly JsRuntimeOptions options;
    private readonly State state;

    internal WebPlatformBuilder(JsRuntimeOptions options)
    {
        this.options = options;
        state = SStates.GetValue(options, static _ => new());
    }

    public WebPlatformBuilder UseWebRuntimeGlobals()
    {
        options.UseRealmSetup(realm =>
        {
            var scheduler = state.WebRuntimeDelayScheduler ?? new TimeProviderDelayScheduler(realm.Engine.TimeProvider);
            new WebRuntimeApiModule(_ => scheduler, state.WebRuntimeTimerQueueKey).Install(realm);
        });
        return this;
    }

    public WebPlatformBuilder UseWebDelayScheduler(IHostDelayScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        state.WebRuntimeDelayScheduler = scheduler;
        return this;
    }

    public WebPlatformBuilder UseWebTimerQueue(HostTaskQueueKey queueKey)
    {
        state.WebRuntimeTimerQueueKey = queueKey;
        return this;
    }

    public WebPlatformBuilder UseRealmSetup(Action<JsRealm> setup)
    {
        options.UseRealmSetup(setup);
        return this;
    }

    public WebPlatformBuilder UseGlobal(string name, Func<JsRealm, JsValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueFactory);
        options.UseRealmSetup(realm => realm.Global[name] = valueFactory(realm));
        return this;
    }

    public WebPlatformBuilder UseServerRuntime(Action<ServerRuntimeOptions>? configure = null)
    {
        var serverOptions = new ServerRuntimeOptions();
        configure?.Invoke(serverOptions);

        options.AddRealmApiModule(AbortApiModule.Shared);
        for (var i = 0; i < serverOptions.RealmApiModules.Count; i++)
            options.AddRealmApiModule(serverOptions.RealmApiModules[i]);
        for (var i = 0; i < serverOptions.RealmSetups.Count; i++)
            options.UseRealmSetup(serverOptions.RealmSetups[i]);

        return UseFetch(fetch => fetch.HttpClient = serverOptions.HttpClient);
    }

    public WebPlatformBuilder UseServerHost(Action<ServerHostOptions>? configure = null)
    {
        var hostOptions = new ServerHostOptions();
        configure?.Invoke(hostOptions);

        if (hostOptions.UseThreadPoolHosting)
            options.UseHosting(static hosting => hosting.UseThreadPoolDefaults());
        if (hostOptions.ModuleSourceLoader is not null)
            options.UseModuleSourceLoader(hostOptions.ModuleSourceLoader);

        UseServerRuntime(server =>
        {
            server.HttpClient = hostOptions.HttpClient;
            for (var i = 0; i < hostOptions.RealmApiModules.Count; i++)
                server.RealmApiModules.Add(hostOptions.RealmApiModules[i]);
            for (var i = 0; i < hostOptions.RealmSetups.Count; i++)
                server.RealmSetups.Add(hostOptions.RealmSetups[i]);
        });

        return this;
    }

    public WebPlatformBuilder UseFetch(Action<FetchOptions>? configure = null)
    {
        var fetchOptions = new FetchOptions();
        configure?.Invoke(fetchOptions);
        return UseFetch(new FetchApiModule(fetchOptions.HttpClient, state.FetchCompletionQueueKey));
    }

    public WebPlatformBuilder UseFetch(FetchApiModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        options.AddRealmApiModule(module);
        return this;
    }

    public WebPlatformBuilder UseWebWorkers(Action<WebWorkerOptions>? configure = null)
    {
        var workerOptions = new WebWorkerOptions();
        configure?.Invoke(workerOptions);
        return UseWebWorkers(new WebWorkerHost(workerOptions));
    }

    public WebPlatformBuilder UseWebWorkers(WebWorkerHost workerHost)
    {
        ArgumentNullException.ThrowIfNull(workerHost);
        options.AddRealmApiModule(WebWorkerApiModule.Shared);
        options.UseLowLevelHost(host => host.UseWorkerMessageQueue(WebTaskQueueKeys.Messages));
        options.UseHosting(hosting => hosting
            .UseWorkerGlobals()
            .UseJsWorkerHost(workerHost));
        return this;
    }

    public WebPlatformBuilder UseFetchCompletionQueue(HostTaskQueueKey queueKey)
    {
        state.FetchCompletionQueueKey = queueKey;
        return this;
    }

    private sealed class State
    {
        public HostTaskQueueKey FetchCompletionQueueKey = WebTaskQueueKeys.Network;
        public IHostDelayScheduler? WebRuntimeDelayScheduler;
        public HostTaskQueueKey WebRuntimeTimerQueueKey = WebTaskQueueKeys.Timers;
    }
}
