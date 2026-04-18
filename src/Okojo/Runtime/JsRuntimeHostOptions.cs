using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     Host-facing runtime configuration for Okojo.
///     Keep this layer for embedding concerns such as time, module loading, and worker script loading.
/// </summary>
public sealed class JsRuntimeHostOptions
{
    private readonly List<Func<IModuleSourceLoader, IModuleSourceLoader>> moduleSourceLoaderDecorators = [];

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

    public JsRuntimeHostOptions DecorateModuleSourceLoader(Func<IModuleSourceLoader, IModuleSourceLoader> decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);
        moduleSourceLoaderDecorators.Add(decorator);
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

    internal IModuleSourceLoader ApplyModuleSourceLoaderDecorators(IModuleSourceLoader baseLoader)
    {
        ArgumentNullException.ThrowIfNull(baseLoader);
        var current = baseLoader;
        for (var i = 0; i < moduleSourceLoaderDecorators.Count; i++)
            current = moduleSourceLoaderDecorators[i](current);
        return current;
    }

    internal JsRuntimeHostOptions Clone()
    {
        var clone = new JsRuntimeHostOptions
        {
            SourceMapRegistry = SourceMapRegistry,
            TimeProvider = TimeProvider,
            ModuleSourceLoader = ModuleSourceLoader,
            WorkerScriptSourceLoader = WorkerScriptSourceLoader
        };

        clone.moduleSourceLoaderDecorators.AddRange(moduleSourceLoaderDecorators);
        return clone;
    }
}
