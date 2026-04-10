using System.ComponentModel;
using System.Reflection;
using Okojo.RegExp;
using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     Layered Okojo runtime configuration.
///     Prefer <see cref="JsRuntimeBuilder" /> for composition; use this object when you need reusable configuration state.
/// </summary>
public sealed class JsRuntimeOptions
{
    /// <summary>Engine-core runtime configuration.</summary>
    public JsRuntimeCoreOptions Core { get; private set; } = new();

    /// <summary>Agent-level runtime configuration and default realm settings.</summary>
    public JsAgentOptions Agent { get; private set; } = new();

    /// <summary>Host-facing runtime configuration such as time providers and source loaders.</summary>
    public JsRuntimeHostOptions Host { get; private set; } = new();

    /// <summary>
    ///     Low-level host integration surface for advanced embedders. This exposes the
    ///     direct engine host seams; higher-level default runtime policy should usually
    ///     come from <c>Okojo.Hosting</c>.
    /// </summary>
    public JsRuntimeLowLevelHostOptions LowLevelHost { get; } = new();

    public TimeProvider? TimeProvider => Host.TimeProvider;
    public IModuleSourceLoader? ModuleSourceLoader => Host.ModuleSourceLoader;
    public IWorkerScriptSourceLoader? WorkerScriptSourceLoader => Host.WorkerScriptSourceLoader;
    internal JsRuntimeLowLevelHostOptions HostServices => LowLevelHost;

    internal ISharedWaiterControllerFactory SharedWaiterControllerFactory { get; private set; } =
        DefaultSharedWaiterControllerFactory.Shared;

    public bool ClrAccessEnabled => Core.ClrAccessEnabled;
    public IRegExpEngine? RegExpEngine => Core.RegExpEngine;
    public IReadOnlyList<Assembly> ClrAssemblies => Core.ClrAssemblies;
    public IReadOnlyList<IRealmApiModule> RealmApiModules => Core.RealmApiModules;
    internal IClrAccessProvider? ClrAccessProvider => Core.ClrAccessProvider;

    internal JsRuntimeOptions EnableClrAccess(IClrAccessProvider? provider = null)
    {
        Core.EnableClrAccess(provider);
        return this;
    }

    internal JsRuntimeOptions UseClrAccessProvider(IClrAccessProvider provider)
    {
        Core.UseClrAccessProvider(provider);
        return this;
    }

    public JsRuntimeOptions AddRealmApiModule(IRealmApiModule module)
    {
        Core.AddRealmApiModule(module);
        return this;
    }

    public JsRuntimeOptions UseRealmSetup(Action<JsRealm> setup)
    {
        Core.UseRealmSetup(setup);
        return this;
    }

    public JsRuntimeOptions UseGlobals(Action<JsGlobalInstaller> configure)
    {
        Core.UseGlobals(configure);
        return this;
    }

    public JsRuntimeOptions UseRegExpEngine(IRegExpEngine engine)
    {
        Core.UseRegExpEngine(engine);
        return this;
    }

    public JsRuntimeOptions UseTimeProvider(TimeProvider timeProvider)
    {
        Host.UseTimeProvider(timeProvider);
        return this;
    }

    public JsRuntimeOptions UseModuleSourceLoader(IModuleSourceLoader moduleLoader)
    {
        Host.UseModuleSourceLoader(moduleLoader);
        return this;
    }

    public JsRuntimeOptions UseWorkerScriptSourceLoader(IWorkerScriptSourceLoader workerScriptLoader)
    {
        Host.UseWorkerScriptSourceLoader(workerScriptLoader);
        return this;
    }

    public JsRuntimeOptions UseSourceMapRegistry(SourceMapRegistry sourceMapRegistry)
    {
        Host.UseSourceMapRegistry(sourceMapRegistry);
        return this;
    }

    /// <summary>
    ///     Advanced host seam configuration for embedders who need direct control over
    ///     scheduling, worker, and message integration. Prefer this over the individual
    ///     advanced helper methods when configuring non-default hosting behavior.
    /// </summary>
    public JsRuntimeOptions UseLowLevelHost(Action<JsRuntimeLowLevelHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(LowLevelHost);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeOptions UseBackgroundScheduler(IBackgroundScheduler scheduler)
    {
        LowLevelHost.UseBackgroundScheduler(scheduler);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeOptions UseHostTaskScheduler(IHostTaskScheduler hostTaskScheduler)
    {
        LowLevelHost.UseTaskScheduler(hostTaskScheduler);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeOptions UseMessageSerializer(IHostMessageSerializer messageSerializer)
    {
        LowLevelHost.UseMessageSerializer(messageSerializer);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeOptions UseWorkerHost(IWorkerHost workerHost)
    {
        LowLevelHost.UseWorkerHost(workerHost);
        return this;
    }

    internal JsRuntimeOptions UseSharedWaiterControllerFactory(ISharedWaiterControllerFactory controllerFactory)
    {
        ArgumentNullException.ThrowIfNull(controllerFactory);
        SharedWaiterControllerFactory = controllerFactory;
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeOptions UseWorkerMessageQueue(HostTaskQueueKey queueKey)
    {
        LowLevelHost.UseWorkerMessageQueue(queueKey);
        return this;
    }

    public JsRuntimeOptions AddClrAssembly(params Assembly[] assemblies)
    {
        Core.AddClrAssembly(assemblies);
        return this;
    }

    internal JsRuntimeOptions Clone()
    {
        var clone = new JsRuntimeOptions();
        clone.Core = Core.Clone();
        clone.Agent = Agent.Clone();
        clone.Host = Host.Clone();
        clone.LowLevelHost.UseBackgroundScheduler(LowLevelHost.BackgroundScheduler);
        clone.LowLevelHost.UseTaskScheduler(LowLevelHost.HostTaskScheduler);
        clone.LowLevelHost.UseMessageSerializer(LowLevelHost.MessageSerializer);
        clone.LowLevelHost.UseWorkerHost(LowLevelHost.WorkerHost);
        clone.LowLevelHost.UseWorkerMessageQueue(LowLevelHost.WorkerMessageQueueKey);
        clone.SharedWaiterControllerFactory = SharedWaiterControllerFactory;
        return clone;
    }
}
