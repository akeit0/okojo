using System.ComponentModel;
using Okojo.Hosting;
using Okojo.RegExp;
using Okojo.Runtime;
using Okojo.WebAssembly;

namespace Okojo.Node;

/// <summary>
///     Node runtime/profile wrapper over <see cref="JsRuntimeBuilder" />.
///     Prefer Node-specific methods on this type plus <see cref="ConfigureRuntime" />
///     for core Okojo configuration.
/// </summary>
public sealed class NodeRuntimeBuilder
{
    private readonly JsRuntimeBuilder runtimeBuilder = JsRuntime.CreateBuilder();
    private readonly NodeTerminalOptions terminalOptions = new();
    private bool installNodeGlobals = true;

    /// <summary>
    ///     Escape hatch for core runtime configuration. Prefer this over adding more
    ///     pass-through core builder methods to the Node profile wrapper.
    /// </summary>
    public NodeRuntimeBuilder ConfigureRuntime(Action<JsRuntimeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(runtimeBuilder);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public NodeRuntimeBuilder UseModuleSourceLoader(IModuleSourceLoader moduleLoader)
    {
        ArgumentNullException.ThrowIfNull(moduleLoader);
        runtimeBuilder.UseModuleSourceLoader(moduleLoader);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public NodeRuntimeBuilder UseThreadPoolHosting()
    {
        runtimeBuilder.UseThreadPoolHosting();
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public NodeRuntimeBuilder UseHosting(Action<HostingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        runtimeBuilder.UseHosting(configure);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public NodeRuntimeBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        runtimeBuilder.UseTimeProvider(timeProvider);
        return this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public NodeRuntimeBuilder UseHostTaskScheduler(IHostTaskScheduler hostTaskScheduler)
    {
        ArgumentNullException.ThrowIfNull(hostTaskScheduler);
        runtimeBuilder.UseHostTaskScheduler(hostTaskScheduler);
        return this;
    }

    public NodeRuntimeBuilder InstallNodeGlobals(bool enabled = true)
    {
        installNodeGlobals = enabled;
        return this;
    }

    public NodeRuntimeBuilder ConfigureTerminal(Action<NodeTerminalOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(terminalOptions);
        return this;
    }

    public NodeRuntimeBuilder UseWebAssembly(Action<WebAssemblyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        runtimeBuilder.UseWebAssembly(configure);
        return this;
    }

    public NodeRuntime Build()
    {
        var options = runtimeBuilder.BuildOptions();
        if (options.Core.RegExpEngine is null)
            options.Core.UseRegExpEngine(RegExpEngine.Default);
        var baseLoader = options.ModuleSourceLoader ?? new FileModuleSourceLoader();
        var nodeLoader = new NodeModuleSourceLoader(baseLoader, options.Host.SourceMapRegistry);
        options.Host.UseModuleSourceLoader(nodeLoader);
        return new(JsRuntime.Create(options), nodeLoader, terminalOptions.Clone(), installNodeGlobals);
    }
}
