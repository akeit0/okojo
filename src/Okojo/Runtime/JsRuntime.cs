using Okojo.Bytecode;
using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     Root Okojo runtime object.
///     Prefer the builder or options-based factory for embedding code.
/// </summary>
public sealed class JsRuntime : IDisposable
{
    private readonly List<JsAgent> agents = new();
    private readonly object agentsGate = new();
    private volatile bool disposed;
    private int nextAgentId = 1;

    private JsRuntime(
        JsRuntimeOptions options,
        TimeProvider? timeProvider,
        IWorkerScriptSourceLoader? workerScriptLoader,
        IModuleSourceLoader? moduleLoader,
        bool _)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options.Clone();
        if (timeProvider is not null)
            Options.Host.UseTimeProvider(timeProvider);
        if (moduleLoader is not null)
            Options.Host.UseModuleSourceLoader(moduleLoader);
        if (workerScriptLoader is not null)
            Options.Host.UseWorkerScriptSourceLoader(workerScriptLoader);

        TimeProvider = Options.TimeProvider ?? TimeProvider.System;
        ModuleSourceLoader = Options.ModuleSourceLoader ?? new FileModuleSourceLoader();
        WorkerScriptSourceLoader = Options.WorkerScriptSourceLoader ??
                                   new WorkerScriptSourceLoaderFromModuleSourceLoader(ModuleSourceLoader);
        SourceMapRegistry = Options.Host.SourceMapRegistry;
        MainAgent = new(this, JsAgentKind.Main, 0, Options.Agent);
        lock (agentsGate)
        {
            agents.Add(MainAgent);
        }
    }

    /// <summary>Reusable runtime configuration for the current instance.</summary>
    public JsRuntimeOptions Options { get; }

    /// <summary>Active time provider used by timers and host scheduling.</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>Module source loader used by the runtime and worker helpers.</summary>
    public IModuleSourceLoader ModuleSourceLoader { get; }

    /// <summary>Worker script loader used by worker bootstrap and hosted worker paths.</summary>
    public IWorkerScriptSourceLoader WorkerScriptSourceLoader { get; }

    /// <summary>Optional shared source-map registry used to remap runtime locations.</summary>
    public SourceMapRegistry? SourceMapRegistry { get; }

    /// <summary>Main agent that owns the default realm set.</summary>
    public JsAgent MainAgent { get; }

    /// <summary>True after the runtime has been disposed.</summary>
    public bool IsDisposed => disposed;

    public bool IsClrAccessEnabled => Options.ClrAccessEnabled;
    internal IClrAccessProvider? ClrAccessProvider => Options.ClrAccessProvider;

    public IReadOnlyList<JsAgent> Agents
    {
        get
        {
            ThrowIfDisposed();
            lock (agentsGate)
            {
                return agents.ToArray();
            }
        }
    }

    /// <summary>
    ///     Compatibility convenience during transition: engine-level realm view maps to the main agent's realms.
    /// </summary>
    public IReadOnlyList<JsRealm> Realms
    {
        get
        {
            ThrowIfDisposed();
            return MainAgent.Realms;
        }
    }

    /// <summary>Main realm of the main agent.</summary>
    public JsRealm MainRealm
    {
        get
        {
            ThrowIfDisposed();
            return MainAgent.MainRealm;
        }
    }

    /// <summary>Alias for <see cref="MainRealm" />.</summary>
    public JsRealm DefaultRealm => MainRealm;

    public void Dispose()
    {
        JsAgent[] snapshot;
        lock (agentsGate)
        {
            if (disposed)
                return;
            disposed = true;
            snapshot = agents.ToArray();
            agents.Clear();
        }

        for (var i = snapshot.Length - 1; i >= 0; i--)
            snapshot[i].Dispose();
    }

    /// <summary>Creates a builder for preferred runtime composition.</summary>
    public static JsRuntimeBuilder CreateBuilder()
    {
        return new();
    }

    /// <summary>Creates a default runtime with standard engine and host defaults.</summary>
    public static JsRuntime Create()
    {
        return new(new(), null, null, null, true);
    }

    /// <summary>Creates a runtime from a builder callback.</summary>
    public static JsRuntime Create(Action<JsRuntimeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = CreateBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>Creates a runtime from reusable options.</summary>
    public static JsRuntime Create(JsRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options, null, null, null, true);
    }

    public void Execute(JsScript script)
    {
        ThrowIfDisposed();
        MainRealm.Execute(script);
    }

    public void Execute(JsBytecodeFunction rootFunc)
    {
        ThrowIfDisposed();
        MainRealm.Execute(rootFunc);
    }

    public void Execute(string source, bool pumpJobsAfterRun = true)
    {
        ThrowIfDisposed();
        MainRealm.Execute(source, pumpJobsAfterRun);
    }

    public JsValue Evaluate(string source, bool pumpJobsAfterRun = true)
    {
        ThrowIfDisposed();
        return MainRealm.Evaluate(source, pumpJobsAfterRun);
    }

    public JsValue Eval(string source, bool pumpJobsAfterRun = true)
    {
        return Evaluate(source, pumpJobsAfterRun);
    }

    public JsRealm CreateRealm(Action<JsRealmOptions>? configure = null)
    {
        ThrowIfDisposed();
        return MainAgent.CreateRealm(CreateRealmOptions(configure));
    }

    public JsModuleLoadResult LoadModule(string specifier, string? referrer = null)
    {
        ThrowIfDisposed();
        return MainRealm.LoadModule(specifier, referrer);
    }

    public JsAgent CreateWorkerAgent(Action<JsAgentOptions>? configure = null)
    {
        lock (agentsGate)
        {
            ThrowIfDisposed();
            var agentOptions = Options.Agent.Clone();
            configure?.Invoke(agentOptions);
            var agent = new JsAgent(this, JsAgentKind.Worker, nextAgentId++, agentOptions, MainAgent);
            agents.Add(agent);
            return agent;
        }
    }

    public string LoadWorkerScript(string path, string? referrer = null)
    {
        ThrowIfDisposed();
        return WorkerScriptSourceLoader.LoadScript(path, referrer);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(JsRuntime));
    }

    private static JsRuntimeOptions CreateOptions(Action<JsRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new JsRuntimeOptions();
        configure(options);
        return options;
    }

    private static JsRealmOptions CreateRealmOptions(Action<JsRealmOptions>? configure)
    {
        var options = new JsRealmOptions();
        configure?.Invoke(options);
        return options;
    }

    private sealed class WorkerScriptSourceLoaderFromModuleSourceLoader(IModuleSourceLoader moduleLoader)
        : IWorkerScriptSourceLoader
    {
        public string LoadScript(string path, string? referrer = null)
        {
            var resolved = moduleLoader.ResolveSpecifier(path, referrer);
            return moduleLoader.LoadSource(resolved);
        }
    }
}
