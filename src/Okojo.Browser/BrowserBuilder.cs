using Okojo.Hosting;
using Okojo.Runtime;
using Okojo.WebPlatform;
using System.Runtime.CompilerServices;

namespace Okojo.Browser;

public sealed class BrowserBuilder
{
    private sealed class State
    {
        public HostTaskQueueKey AnimationFrameQueueKey = WebTaskQueueKeys.Rendering;
        public TimeSpan AnimationFrameInterval = TimeSpan.FromMilliseconds(16);
    }

    private static readonly ConditionalWeakTable<JsRuntimeOptions, State> SStates = new();

    private readonly JsRuntimeOptions options;
    private readonly State state;

    internal BrowserBuilder(JsRuntimeOptions options)
    {
        this.options = options;
        state = SStates.GetValue(options, static _ => new State());
    }

    public BrowserBuilder UseBrowserGlobals(Action<FetchOptions>? configureFetch = null)
    {
        options.UseRealmSetup(realm =>
        {
            new BrowserApiModule(
                _ => new TimeProviderDelayScheduler(realm.Engine.TimeProvider),
                WebTaskQueueKeys.Timers,
                state.AnimationFrameQueueKey,
                state.AnimationFrameInterval).Install(realm);
        });

        return UseFetch(configureFetch);
    }

    public BrowserBuilder UseBrowserHost(Action<BrowserHostOptions>? configure = null)
    {
        var hostOptions = new BrowserHostOptions();
        configure?.Invoke(hostOptions);

        if (hostOptions.UseThreadPoolHosting)
            options.UseHosting(static hosting => hosting.UseThreadPoolDefaults());
        if (hostOptions.ModuleSourceLoader is not null)
            options.UseModuleSourceLoader(hostOptions.ModuleSourceLoader);

        UseBrowserGlobals(fetch => fetch.HttpClient = hostOptions.HttpClient);

        if (hostOptions.InstallWebWorkers)
            options.UseWebPlatform(web => web.UseWebWorkers(hostOptions.ConfigureWebWorkers));

        return this;
    }

    public BrowserBuilder UseAnimationFrameQueue(HostTaskQueueKey queueKey)
    {
        state.AnimationFrameQueueKey = queueKey;
        return this;
    }

    public BrowserBuilder UseAnimationFrameInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        state.AnimationFrameInterval = interval;
        return this;
    }

    public BrowserBuilder UseFetch(Action<FetchOptions>? configure = null)
    {
        options.UseWebPlatform(web => web.UseFetch(configure));
        return this;
    }
}
