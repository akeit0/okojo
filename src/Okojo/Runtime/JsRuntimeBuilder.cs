using System.ComponentModel;
using System.Reflection;
using Okojo.RegExp;
using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     Preferred composition entry point for Okojo runtime construction.
///     Use this when assembling core, agent, realm, and host integration settings.
/// </summary>
public sealed class JsRuntimeBuilder
{
    private readonly JsRuntimeOptions options = new();

    internal JsRuntimeBuilder EnableClrAccess(IClrAccessProvider? provider = null)
    {
        options.Core.EnableClrAccess(provider);
        return this;
    }

    internal JsRuntimeBuilder UseClrAccessProvider(IClrAccessProvider provider)
    {
        options.Core.UseClrAccessProvider(provider);
        return this;
    }

    public JsRuntimeBuilder AddRealmApiModule(IRealmApiModule module)
    {
        options.Core.AddRealmApiModule(module);
        return this;
    }

    public JsRuntimeBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        options.Host.UseTimeProvider(timeProvider);
        return this;
    }

    public JsRuntimeBuilder UseModuleSourceLoader(IModuleSourceLoader moduleLoader)
    {
        options.Host.UseModuleSourceLoader(moduleLoader);
        return this;
    }

    public JsRuntimeBuilder UseWorkerScriptSourceLoader(IWorkerScriptSourceLoader workerScriptLoader)
    {
        options.Host.UseWorkerScriptSourceLoader(workerScriptLoader);
        return this;
    }

    public JsRuntimeBuilder UseSourceMapRegistry(SourceMapRegistry sourceMapRegistry)
    {
        options.Host.UseSourceMapRegistry(sourceMapRegistry);
        return this;
    }

    public JsRuntimeBuilder UseRealmSetup(Action<JsRealm> setup)
    {
        options.Core.UseRealmSetup(setup);
        return this;
    }

    public JsRuntimeBuilder UseGlobals(Action<JsGlobalInstaller> configure)
    {
        options.Core.UseGlobals(configure);
        return this;
    }

    public JsRuntimeBuilder UseGlobal(string name, Func<JsRealm, JsValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueFactory);
        return UseGlobals(globals => globals.Value(name, valueFactory));
    }

    public JsRuntimeBuilder UseRegExpEngine(IRegExpEngine engine)
    {
        options.Core.UseRegExpEngine(engine);
        return this;
    }

    public JsRuntimeBuilder AddClrAssembly(params Assembly[] assemblies)
    {
        options.Core.AddClrAssembly(assemblies);
        return this;
    }

    public JsRuntimeBuilder UseCore(Action<JsRuntimeCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options.Core);
        return this;
    }

    public JsRuntimeBuilder UseAgent(Action<JsAgentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options.Agent);
        return this;
    }

    public JsRuntimeBuilder UseHost(Action<JsRuntimeHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options.Host);
        return this;
    }

    /// <summary>
    ///     Advanced host seam configuration for embedders who need direct control over
    ///     scheduling, worker, and message integration. Prefer this over the individual
    ///     advanced helper methods when configuring non-default hosting behavior.
    /// </summary>
    public JsRuntimeBuilder UseLowLevelHost(Action<JsRuntimeLowLevelHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options.LowLevelHost);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeBuilder UseBackgroundScheduler(IBackgroundScheduler scheduler)
    {
        options.LowLevelHost.UseBackgroundScheduler(scheduler);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeBuilder UseHostTaskScheduler(IHostTaskScheduler hostTaskScheduler)
    {
        options.LowLevelHost.UseTaskScheduler(hostTaskScheduler);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeBuilder UseMessageSerializer(IHostMessageSerializer messageSerializer)
    {
        options.LowLevelHost.UseMessageSerializer(messageSerializer);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeBuilder UseWorkerHost(IWorkerHost workerHost)
    {
        options.LowLevelHost.UseWorkerHost(workerHost);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsRuntimeBuilder UseWorkerMessageQueue(HostTaskQueueKey queueKey)
    {
        options.LowLevelHost.UseWorkerMessageQueue(queueKey);
        return this;
    }

    public JsRuntimeBuilder UseRealm(Action<JsRealmOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options.Agent.Realm);
        return this;
    }

    public JsRuntimeBuilder ConfigureOptions(Action<JsRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(options);
        return this;
    }

    public JsRuntimeOptions BuildOptions()
    {
        return options.Clone();
    }

    public JsRuntime Build()
    {
        return JsRuntime.Create(BuildOptions());
    }
}
