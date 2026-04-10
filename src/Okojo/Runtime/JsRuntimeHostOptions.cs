using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     Host-facing runtime configuration for Okojo.
///     Keep this layer for embedding concerns such as time, module loading, and worker script loading.
/// </summary>
public sealed class JsRuntimeHostOptions
{
    public SourceMapRegistry? SourceMapRegistry { get; private set; }
    public TimeProvider? TimeProvider { get; private set; }
    public IModuleSourceLoader? ModuleSourceLoader { get; private set; }
    public IWorkerScriptSourceLoader? WorkerScriptSourceLoader { get; private set; }

    public JsRuntimeHostOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    public JsRuntimeHostOptions UseModuleSourceLoader(IModuleSourceLoader moduleLoader)
    {
        ArgumentNullException.ThrowIfNull(moduleLoader);
        ModuleSourceLoader = moduleLoader;
        return this;
    }

    public JsRuntimeHostOptions UseWorkerScriptSourceLoader(IWorkerScriptSourceLoader workerScriptLoader)
    {
        ArgumentNullException.ThrowIfNull(workerScriptLoader);
        WorkerScriptSourceLoader = workerScriptLoader;
        return this;
    }

    public JsRuntimeHostOptions UseSourceMapRegistry(SourceMapRegistry sourceMapRegistry)
    {
        ArgumentNullException.ThrowIfNull(sourceMapRegistry);
        SourceMapRegistry = sourceMapRegistry;
        return this;
    }

    internal JsRuntimeHostOptions Clone()
    {
        return new()
        {
            SourceMapRegistry = SourceMapRegistry,
            TimeProvider = TimeProvider,
            ModuleSourceLoader = ModuleSourceLoader,
            WorkerScriptSourceLoader = WorkerScriptSourceLoader
        };
    }
}
