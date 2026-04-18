using Okojo.Compiler;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.SourceMaps;
using Okojo.WebPlatform;

namespace Okojo.Node;

public sealed class NodeRuntime : IDisposable
{
    private const string NodeHostImportBridgeTempName = "_nodeHostImport";
    private const string NodeHostImportBridgeSymbolKey = "node.host.import";

    private readonly Dictionary<string, CommonJsModuleRecord> commonJsCache =
        new(StringComparer.Ordinal);

    private readonly NodeCommonJsResolver commonJsResolver;
    private readonly NodeModuleFormatResolver moduleFormatResolver;
    private readonly NodeModuleSourceLoader moduleLoader;
    private readonly JsPlainObject requireCacheObject;
    private readonly NodeTerminalOptions terminalOptions;
    private readonly WebRuntimeApiModule webRuntimeApiModule;
    private bool disposed;

    internal NodeRuntime(
        JsRuntime runtime,
        NodeModuleSourceLoader moduleLoader,
        NodeTerminalOptions? terminalOptions = null,
        bool installNodeGlobals = true)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
        this.terminalOptions = terminalOptions?.Clone() ?? new NodeTerminalOptions();
        commonJsResolver = new(
            this.moduleLoader.ResolveSpecifier,
            this.moduleLoader.LoadRawSource);
        BuiltIns = new(this, this.terminalOptions);
        moduleFormatResolver = new(this.moduleLoader.LoadRawSource);
        requireCacheObject = new(MainRealm);
        webRuntimeApiModule = CreateWebRuntimeApiModule(Runtime.Options.LowLevelHost.HostTaskScheduler);
        if (installNodeGlobals)
            InstallNodeGlobals(MainRealm);
    }

    public JsRuntime Runtime { get; }
    public JsRealm MainRealm => Runtime.MainRealm;
    public SourceMapRegistry? SourceMapRegistry => Runtime.SourceMapRegistry;
    internal NodeBuiltInModuleRegistry BuiltIns { get; }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Runtime.Dispose();
    }

    public static NodeRuntimeBuilder CreateBuilder()
    {
        return new();
    }

    public JsValue RunMainModule(string path, params string[] argv)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);

        var resolvedId = commonJsResolver.ResolveMain(path, null);
        BuiltIns.SetProcessArgv(resolvedId, argv);
        return moduleFormatResolver.DetermineFormat(resolvedId) switch
        {
            NodeModuleFormat.CommonJs => LoadCommonJsModule(resolvedId),
            NodeModuleFormat.EsModule => Runtime.MainAgent.EvaluateModule(MainRealm, resolvedId),
            _ => throw new InvalidOperationException("Unsupported Node module format.")
        };
    }

    public string PrepareMainModuleForDebugging(string path, params string[] argv)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);

        var resolvedId = commonJsResolver.ResolveMain(path, null);
        BuiltIns.SetProcessArgv(resolvedId, argv);

        switch (moduleFormatResolver.DetermineFormat(resolvedId))
        {
            case NodeModuleFormat.CommonJs:
                _ = GetOrCreateCommonJsRecord(resolvedId);
                break;
            case NodeModuleFormat.EsModule:
                _ = Runtime.LoadModule(resolvedId);
                break;
            default:
                throw new InvalidOperationException("Unsupported Node module format.");
        }

        return resolvedId;
    }

    public JsValue Require(string specifier, string? referrer = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(specifier);

        if (!specifier.StartsWith("node:", StringComparison.Ordinal) &&
            TryGetRequireCacheExports(specifier, out var cachedExports))
            return cachedExports;

        if (BuiltIns.TryGetBuiltInModule(specifier, out var builtInExports))
            return builtInExports;

        var resolvedId = ResolveCommonJsSpecifier(specifier, referrer);
        if (TryGetRequireCacheExports(resolvedId, out cachedExports))
            return cachedExports;

        if (commonJsCache.ContainsKey(resolvedId))
            commonJsCache.Remove(resolvedId);

        return moduleFormatResolver.DetermineFormat(resolvedId) switch
        {
            NodeModuleFormat.CommonJs => LoadCommonJsModule(resolvedId),
            NodeModuleFormat.EsModule => LoadRequiredEsModule(resolvedId),
            _ => throw new InvalidOperationException("Unsupported Node module format.")
        };
    }

    private JsValue LoadCommonJsModule(string resolvedId)
    {
        var record = GetOrCreateCommonJsRecord(resolvedId);
        if (record.IsLoaded)
            return GetModuleExports(record.ModuleObject);

        try
        {
            _ = MainRealm.Call(
                record.WrapperFunction!,
                JsValue.Undefined,
                JsValue.FromObject(GetModuleExports(record.ModuleObject).AsObject()),
                JsValue.FromObject(record.RequireFunction!),
                JsValue.FromObject(record.ModuleObject),
                JsValue.FromString(resolvedId),
                JsValue.FromString(GetDirectoryName(resolvedId)));

            record.ModuleObject.DefineDataProperty("loaded", JsValue.True, JsShapePropertyFlags.Open);
            record.IsLoaded = true;
            return GetModuleExports(record.ModuleObject);
        }
        catch
        {
            RemoveRequireCacheEntry(resolvedId);
            throw;
        }
    }

    private CommonJsModuleRecord GetOrCreateCommonJsRecord(string resolvedId)
    {
        if (commonJsCache.TryGetValue(resolvedId, out var cached))
            return cached;

        var source = moduleLoader.LoadRawSource(resolvedId);
        moduleLoader.TryRegisterSourceMap(resolvedId, source);
        var realm = MainRealm;
        var exportsObject = new JsPlainObject(realm);
        var moduleObject = CreateModuleObject(realm, resolvedId, exportsObject);
        var record = new CommonJsModuleRecord(resolvedId, moduleObject);
        commonJsCache.Add(resolvedId, record);
        requireCacheObject.DefineDataProperty(resolvedId, JsValue.FromObject(moduleObject), JsShapePropertyFlags.Open);

        try
        {
            if (resolvedId.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var jsonExports = realm.ParseJsonModuleSource(source);
                moduleObject.DefineDataProperty("exports", jsonExports, JsShapePropertyFlags.Open);
                moduleObject.DefineDataProperty("loaded", JsValue.True, JsShapePropertyFlags.Open);
                record.IsLoaded = true;
                return record;
            }

            record.RequireFunction = CreateRequireFunction(realm, resolvedId);
            record.WrapperFunction = CompileCommonJsWrapper(realm, source, resolvedId);
            return record;
        }
        catch
        {
            RemoveRequireCacheEntry(resolvedId);
            throw;
        }
    }

    private JsValue LoadRequiredEsModule(string resolvedId)
    {
        var module = Runtime.LoadModule(resolvedId);
        var completion = FinalizeModuleLoad(module);
        if (completion.TryGetObject(out var completionObject) &&
            ReferenceEquals(completionObject, module.Object))
            return JsValue.FromObject(module.Object);

        throw new InvalidOperationException(
            $"Cannot require async ECMAScript module '{resolvedId}' because it has not completed evaluation.");
    }

    private JsValue FinalizeModuleLoad(JsModuleLoadResult module)
    {
        if (module.IsCompleted)
            return module.CompletionValue;

        if (module.CompletionValue.TryGetObject(out var completionObject) &&
            completionObject is JsPromiseObject promise &&
            promise.State == JsPromiseObject.PromiseState.Pending)
        {
            for (var i = 0; i < 16 && promise.State == JsPromiseObject.PromiseState.Pending; i++)
                MainRealm.PumpJobs();

            if (promise.State == JsPromiseObject.PromiseState.Fulfilled)
                return JsValue.FromObject(module.Object);
        }

        return module.CompletionValue;
    }

    public void InstallNodeGlobals(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        realm.HostDefined = new NodeRealmHostState(this);
        webRuntimeApiModule.Install(realm);
        AbortApiModule.Shared.Install(realm);
        realm.Global["global"] = JsValue.FromObject(realm.GlobalObject);
        realm.Global["performance"] = JsValue.FromObject(BuiltIns.GetPerformanceObject());
        realm.Global["process"] = JsValue.FromObject(BuiltIns.GetProcessObject());
        realm.Global["Buffer"] = JsValue.FromObject(BuiltIns.GetBufferObject());
        realm.Global["setImmediate"] = JsValue.FromObject(BuiltIns.CreateSetImmediateFunction(realm));
        realm.Global["clearImmediate"] = JsValue.FromObject(BuiltIns.CreateClearImmediateFunction(realm));
        realm.Global[NodeHostImportBridgeTempName] = JsValue.FromObject(CreateNodeHostImportBridge(realm));
        _ = realm.Eval($$"""
                         globalThis[Symbol.for("{{NodeHostImportBridgeSymbolKey}}")] = globalThis.{{NodeHostImportBridgeTempName}};
                         delete globalThis.{{NodeHostImportBridgeTempName}};
                         """);
        if (!realm.GlobalObject.TryGetProperty("console", out _))
            realm.Global["console"] = JsValue.FromObject(BuiltIns.GetConsoleObject());
    }

    private static WebRuntimeApiModule CreateWebRuntimeApiModule(IHostTaskScheduler hostTaskScheduler)
    {
        ArgumentNullException.ThrowIfNull(hostTaskScheduler);
        if (hostTaskScheduler is not IQueuedHostDelayScheduler queuedDelayScheduler)
            return WebRuntimeApiModule.Shared;

        return new(_ => queuedDelayScheduler, WebTaskQueueKeys.Timers);
    }

    private JsFunction CompileCommonJsWrapper(JsRealm realm, string source, string resolvedId)
    {
        const string wrapperPrefix = "(function (exports, require, module, __filename, __dirname) {\n";
        const string wrapperSuffix = "\n})";
        var wrappedSource = wrapperPrefix + source + wrapperSuffix;

        var parsed = JavaScriptParser.ParseScript(
            wrappedSource,
            resolvedId,
            -wrapperPrefix.Length,
            source);
        if (parsed.Statements.Count != 1 ||
            parsed.Statements[0] is not JsExpressionStatement { Expression: JsFunctionExpression wrapperExpression })
            throw new InvalidOperationException("CommonJS wrapper did not parse as a single function expression.");

        using var compiler = new JsCompiler(realm);
        return compiler.CompileHoistedFunctionTemplate(
            wrapperExpression,
            string.Empty,
            wrappedSource,
            resolvedId,
            parsed.IdentifierTable);
    }

    private JsHostFunction CreateRequireFunction(JsRealm realm, string resolvedId)
    {
        var requireFunction = new JsHostFunction(realm, "require", 1, static (in info) =>
        {
            var state = (RequireFunctionState)((JsHostFunction)info.Function).UserData!;
            var specifier = info.GetArgumentString(0);
            return state.Runtime.Require(specifier, state.Referrer);
        }, false)
        {
            UserData = new RequireFunctionState(this, resolvedId)
        };

        requireFunction.DefineDataProperty("cache", JsValue.FromObject(requireCacheObject), JsShapePropertyFlags.Open);
        return requireFunction;
    }

    private JsHostFunction CreateNodeHostImportBridge(JsRealm realm)
    {
        return new(realm, NodeHostImportBridgeTempName, 1, static (in info) =>
        {
            var hostState = (NodeRealmHostState?)info.Realm.HostDefined;
            if (hostState is null)
                throw new InvalidOperationException("Node host bridge is not available for this realm.");

            var specifier = info.GetArgumentString(0);
            return hostState.Runtime.LoadNodeHostModule(specifier);
        }, false)
        {
            UserData = null
        };
    }

    private static JsPlainObject CreateModuleObject(JsRealm realm, string resolvedId, JsPlainObject exportsObject)
    {
        var moduleObject = new JsPlainObject(realm);
        moduleObject.DefineDataProperty("exports", JsValue.FromObject(exportsObject), JsShapePropertyFlags.Open);
        moduleObject.DefineDataProperty("filename", JsValue.FromString(resolvedId), JsShapePropertyFlags.Open);
        moduleObject.DefineDataProperty("id", JsValue.FromString(resolvedId), JsShapePropertyFlags.Open);
        moduleObject.DefineDataProperty("loaded", JsValue.False, JsShapePropertyFlags.Open);
        moduleObject.DefineDataProperty("path", JsValue.FromString(GetDirectoryName(resolvedId)),
            JsShapePropertyFlags.Open);
        return moduleObject;
    }

    private static JsValue GetModuleExports(JsPlainObject moduleObject)
    {
        if (moduleObject.TryGetProperty("exports", out var exports))
            return exports;
        return JsValue.Undefined;
    }

    private bool TryGetRequireCacheExports(string key, out JsValue exports)
    {
        if (!requireCacheObject.TryGetProperty(key, out var cacheEntry) ||
            !cacheEntry.TryGetObject(out var cacheObject))
        {
            exports = JsValue.Undefined;
            return false;
        }

        if (cacheObject!.TryGetProperty("exports", out exports))
            return true;

        exports = JsValue.Undefined;
        return false;
    }

    private void RemoveRequireCacheEntry(string resolvedId)
    {
        commonJsCache.Remove(resolvedId);
        requireCacheObject[resolvedId] = JsValue.Undefined;
    }

    private static string GetDirectoryName(string resolvedId)
    {
        var normalized = resolvedId.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash <= 0)
            return slash == 0 ? "/" : ".";
        return normalized[..slash];
    }

    private string ResolveCommonJsSpecifier(string specifier, string? referrer)
    {
        return commonJsResolver.ResolveRequire(specifier, referrer);
    }

    private JsValue LoadNodeHostModule(string resolvedId)
    {
        if (BuiltIns.TryGetBuiltInModule(resolvedId, out var builtInExports))
            return builtInExports;

        return LoadCommonJsModule(resolvedId);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(NodeRuntime));
    }

    private sealed class CommonJsModuleRecord(string resolvedId, JsPlainObject moduleObject)
    {
        public string ResolvedId { get; } = resolvedId;
        public JsPlainObject ModuleObject { get; } = moduleObject;
        public JsFunction? WrapperFunction { get; set; }
        public JsHostFunction? RequireFunction { get; set; }
        public bool IsLoaded { get; set; }
    }

    private sealed class RequireFunctionState(NodeRuntime runtime, string referrer)
    {
        public NodeRuntime Runtime { get; } = runtime;
        public string Referrer { get; } = referrer;
    }

    private sealed class NodeRealmHostState(NodeRuntime runtime)
    {
        public NodeRuntime Runtime { get; } = runtime;
    }
}
