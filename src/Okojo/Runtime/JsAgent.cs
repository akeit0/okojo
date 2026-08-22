using System.Reflection;
using System.Runtime.CompilerServices;
using Okojo.Bytecode;
using Okojo.SourceMaps;

namespace Okojo.Runtime;

public sealed partial class JsAgent : IDisposable
{
    private static readonly Action<object?> SInvokeActionJob = static state =>
        ((Action)state!).Invoke();

    private static readonly Action<object?> SPostMessageTask = static state =>
    {
        var taskState = (PostMessageTaskState)state!;
        var handler = taskState.Target.MessageReceived;
        handler?.Invoke(taskState.Source, taskState.Payload);
    };

    private readonly JsBreakpointRegistry breakpointRegistry = new();
    private readonly Dictionary<string, Symbol> globalSymbolRegistry = new(StringComparer.Ordinal);
    private readonly AutoResetEvent jobsAvailable = new(false);
    private readonly object jobsGate = new();

    private readonly Dictionary<string, JsModuleNamespaceObject> jsonModuleNamespaceCache = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, JsModuleNamespaceObject> textModuleNamespaceCache = new(
        StringComparer.Ordinal
    );

    private readonly object lifecycleGate = new();
    private readonly Queue<PendingJob> scriptJobs = new();
    private readonly object moduleCacheGate = new();

    private readonly Dictionary<string, string> moduleSourceCache = new(StringComparer.Ordinal);

    private readonly Queue<PendingJob> promiseJobs = new();
    private readonly List<JsRealm> realms = new();
    private readonly object realmsGate = new();
    private readonly HashSet<JsScript> registeredScripts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, Symbol> registeredSymbolByAtom = new();
    private readonly object scriptRegistryGate = new();
    private readonly Dictionary<string, HashSet<JsScript>> scriptsBySourcePath = new(
        SourcePathComparer.Instance
    );
    private readonly HashSet<JsScript> scriptsWithoutSourcePath = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly object stepGate = new();
    private readonly object symbolRegistryGate = new();
    private readonly object runtimeIdentity;
    private readonly Func<string, string?, string> loadWorkerScript;
    private readonly Func<Action<JsAgentOptions>?, JsAgent> createWorkerAgent;
    private readonly Func<IReadOnlyList<Assembly>> getClrAssemblies;
    private readonly Func<int> getClrAssembliesVersion;
    private readonly Action<Assembly[]> addClrAssemblies;
    private Func<int> getPendingHostJobCount = static () => 0;
    private Func<string, int> getHostJobCount = static _ => 0;
    private Action<string, Action<object?>, object?> enqueueHostJob = static (_, _, _) => { };
    private Action<HostTaskQueueKey, Action<object?>, object?> enqueueHostTask = static (_, _, _) => { };
    private Func<string, int> runHostJobs = static _ => 0;
    private Func<bool> runOneHostJob = static () => false;
    private Action pumpHostJobs = static () => { };
    private Action clearHostJobs = static () => { };
    private volatile bool disposed;
    internal ulong ExecutionCheckCountdown;
    internal volatile int ExecutionCheckpointHookBits;
    private bool isPumpingJobs;

    private int nextPrivateBrandId;
    private DebuggerStepRequest? stepRequest;
    private volatile bool terminated;

    internal JsAgent(
        JsAgentKind kind,
        int id,
        JsAgentOptions options,
        JsAgent? parentAgent,
        object runtimeIdentity,
        TimeProvider timeProvider,
        IModuleSourceLoader moduleSourceLoader,
        Func<string, string?, string> loadWorkerScript,
        SourceMapRegistry? sourceMapRegistry,
        IReadOnlyList<IRealmApiModule> realmApiModules,
        Func<IReadOnlyList<Assembly>> getClrAssemblies,
        Func<int> getClrAssembliesVersion,
        Action<Assembly[]> addClrAssemblies,
        bool isClrAccessEnabled,
        IClrAccessProvider? clrAccessProvider,
        ISharedWaiterControllerFactory sharedWaiterControllerFactory,
        IBackgroundScheduler backgroundScheduler,
        IHostMessageSerializer messageSerializer,
        IWorkerHost workerHost,
        HostTaskQueueKey workerMessageQueueKey,
        Func<Action<JsAgentOptions>?, JsAgent> createWorkerAgent
    )
    {
        Kind = kind;
        Id = id;
        ParentAgent = parentAgent;
        this.runtimeIdentity = runtimeIdentity;
        TimeProvider = timeProvider;
        ModuleSourceLoader = moduleSourceLoader;
        this.loadWorkerScript = loadWorkerScript;
        SourceMapRegistry = sourceMapRegistry;
        RealmApiModules = realmApiModules;
        this.getClrAssemblies = getClrAssemblies;
        this.getClrAssembliesVersion = getClrAssembliesVersion;
        this.addClrAssemblies = addClrAssemblies;
        IsClrAccessEnabled = isClrAccessEnabled;
        ClrAccessProvider = clrAccessProvider;
        SharedWaiterControllerFactory = sharedWaiterControllerFactory;
        BackgroundScheduler = backgroundScheduler;
        MessageSerializer = messageSerializer;
        WorkerHost = workerHost;
        WorkerMessageQueueKey = workerMessageQueueKey;
        this.createWorkerAgent = createWorkerAgent;
        Options = options.Clone();
        Atoms = new();
        ModuleGraph = new(this);
        ModuleLinker = new(() => ModuleSourceLoader);
        ExecutionCheckPolicy = new(Options, TimeProvider);
        ExecutionCheckInterval = Math.Min(Options.CheckInterval, Options.MaxInstructions);
        ExecutionCheckpointHookBits = (int)Options.ExecutionCheckpointHooks;
        ExecutionCheckCountdown = ExecutionCheckPolicy.HasPeriodicChecks
            ? ExecutionCheckInterval
            : ulong.MaxValue;
        HostDefined = Options.HostDefined;
        var realm = new JsRealm(this, realms.Count, Options.Realm);
        lock (realmsGate)
        {
            realms.Add(realm);
        }

    }

    internal void AttachHostJobQueue(
        Func<int> getPendingHostJobCount,
        Func<string, int> getHostJobCount,
        Action<string, Action<object?>, object?> enqueueHostJob,
        Action<HostTaskQueueKey, Action<object?>, object?> enqueueHostTask,
        Func<string, int> runHostJobs,
        Func<bool> runOneHostJob,
        Action pumpHostJobs,
        Action clearHostJobs
    )
    {
        this.getPendingHostJobCount = getPendingHostJobCount;
        this.getHostJobCount = getHostJobCount;
        this.enqueueHostJob = enqueueHostJob;
        this.enqueueHostTask = enqueueHostTask;
        this.runHostJobs = runHostJobs;
        this.runOneHostJob = runOneHostJob;
        this.pumpHostJobs = pumpHostJobs;
        this.clearHostJobs = clearHostJobs;
    }

    internal void Initialize()
    {
        MainRealm.Initialize();
        Options.Initialize?.Invoke(this);
    }

    internal ExecutionCheckPolicy ExecutionCheckPolicy { get; }
    internal ulong ExecutionCheckInterval { get; private set; }

    internal object RuntimeIdentity => runtimeIdentity;
    public TimeProvider TimeProvider { get; }
    public IModuleSourceLoader ModuleSourceLoader { get; }
    public SourceMapRegistry? SourceMapRegistry { get; }
    internal IReadOnlyList<IRealmApiModule> RealmApiModules { get; }
    public bool IsClrAccessEnabled { get; }
    internal IClrAccessProvider? ClrAccessProvider { get; }
    internal ISharedWaiterControllerFactory SharedWaiterControllerFactory { get; }
    internal IBackgroundScheduler BackgroundScheduler { get; }
    internal IHostMessageSerializer MessageSerializer { get; }
    internal IWorkerHost WorkerHost { get; }
    internal HostTaskQueueKey WorkerMessageQueueKey { get; }
    public JsAgentKind Kind { get; }
    public int Id { get; }
    public JsAgent? ParentAgent { get; }
    public AtomTable Atoms { get; }
    public JsAgentOptions Options { get; }
    public object? HostDefined { get; }

    public bool IsTerminated => terminated;

    public IReadOnlyList<JsRealm> Realms
    {
        get
        {
            lock (realmsGate)
            {
                return realms.ToArray();
            }
        }
    }

    public JsRealm MainRealm
    {
        get
        {
            lock (realmsGate)
            {
                return realms[0];
            }
        }
    }

    internal int PendingJobCount
    {
        get
        {
            if (terminated)
                return 0;
            lock (jobsGate)
            {
                return scriptJobs.Count + promiseJobs.Count + getPendingHostJobCount();
            }
        }
    }

    /// <summary>Number of pending jobs in a named queue. Returns 0 for unknown queue names.</summary>
    public int GetJobCount(string queueName)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        if (terminated)
            return 0;
        lock (jobsGate)
        {
            return queueName switch
            {
                JobQueueName.ScriptJobs => scriptJobs.Count,
                JobQueueName.PromiseJobs => promiseJobs.Count,
                JobQueueName.HostJobs => getHostJobCount(JobQueueName.HostJobs),
                JobQueueName.HostPriorityJobs => getHostJobCount(JobQueueName.HostPriorityJobs),
                _ => 0,
            };
        }
    }

    public IReadOnlyList<Assembly> ClrAssemblies => getClrAssemblies();
    internal int ClrAssembliesVersion => getClrAssembliesVersion();

    public bool IsDebuggerStatementHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.DebuggerStatement) != 0;

    public bool IsBreakpointHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Breakpoint) != 0;

    public bool IsCaughtExceptionHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.CaughtException) != 0;

    public bool IsCallHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Call) != 0;
    public bool IsReturnHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Return) != 0;
    public bool IsPumpHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Pump) != 0;

    public bool IsSuspendGeneratorHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.SuspendGenerator) != 0;

    public bool IsResumeGeneratorHookEnabled =>
        (ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.ResumeGenerator) != 0;

    public JsScriptDebugRegistry ScriptDebugRegistry => new(this);

    internal int CachedModuleCount
    {
        get
        {
            lock (moduleCacheGate)
            {
                return moduleSourceCache.Count;
            }
        }
    }

    internal int CachedModuleRecordCount
    {
        get
        {
            lock (moduleCacheGate)
            {
                return ModuleGraph.Count;
            }
        }
    }

    internal ModuleGraph ModuleGraph { get; }

    internal ModuleLinker ModuleLinker { get; }

    internal WaitHandle JobsAvailableWaitHandle => jobsAvailable;

    public void Dispose()
    {
        Terminate();
        if (disposed)
            return;
        lock (lifecycleGate)
        {
            if (disposed)
                return;
            disposed = true;
        }

        jobsAvailable.Dispose();
    }

    internal JsRealm CreateRealm(JsRealmOptions? options = null)
    {
        lock (realmsGate)
        {
            var realm = new JsRealm(this, realms.Count, options ?? Options.Realm);
            realm.Initialize();
            realms.Add(realm);
            return realm;
        }
    }

    public JsAgent CreateWorkerAgent(Action<JsAgentOptions>? configure = null)
    {
        return createWorkerAgent(configure);
    }

    internal string LoadWorkerScript(string path, string? referrer = null)
    {
        return loadWorkerScript(path, referrer);
    }

    internal void AddClrAssemblies(params Assembly[] assemblies)
    {
        addClrAssemblies(assemblies);
    }

    internal Symbol GetOrCreateRegisteredSymbol(string key)
    {
        lock (symbolRegistryGate)
        {
            if (globalSymbolRegistry.TryGetValue(key, out var existing))
                return existing;

            var atom = Atoms.InternSymbolString(key);
            var symbol = new Symbol(atom, key, isRegistered: true);
            globalSymbolRegistry[key] = symbol;
            registeredSymbolByAtom[atom] = symbol;
            return symbol;
        }
    }

    internal bool TryGetRegisteredSymbolKey(Symbol symbol, out string key)
    {
        lock (symbolRegistryGate)
        {
            foreach (var pair in globalSymbolRegistry)
                if (ReferenceEquals(pair.Value, symbol))
                {
                    key = pair.Key;
                    return true;
                }
        }

        key = string.Empty;
        return false;
    }

    internal bool TryGetRegisteredSymbolByAtom(int atom, out Symbol symbol)
    {
        lock (symbolRegistryGate)
        {
            return registeredSymbolByAtom.TryGetValue(atom, out symbol!);
        }
    }

    // ECMAScript: running execution context is the top of the agent's execution context stack.
    // In Okojo, call frames are the execution contexts; snapshot is derived from realm frames on demand.
    // https://tc39.es/ecma262/#sec-execution-contexts
    public int GetExecutionContextDepth(JsRealm realm)
    {
        return realm.GetExecutionContextDepth();
    }

    public IReadOnlyList<JsExecutionContext> GetExecutionContextsSnapshot(JsRealm realm)
    {
        return realm.GetExecutionContextsSnapshot();
    }

    public event Action<JsAgent, object?>? MessageReceived;

    public JsBreakpointHandle AddBreakpoint(JsScript script, int pc)
    {
        return breakpointRegistry.AddBreakpoint(this, script, pc);
    }

    public JsBreakpointHandle AddBreakpoint(string sourcePath, int line)
    {
        return breakpointRegistry.AddBreakpoint(this, sourcePath, line);
    }

    internal void SubscribeBreakpointResolved(Action<JsBreakpointHandle> handler)
    {
        breakpointRegistry.SubscribeBreakpointResolved(handler);
    }

    internal void UnsubscribeBreakpointResolved(Action<JsBreakpointHandle> handler)
    {
        breakpointRegistry.UnsubscribeBreakpointResolved(handler);
    }

    internal void RegisterScript(JsScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        lock (scriptRegistryGate)
        {
            RegisterScriptRecursive(script);
        }
    }

    internal bool IsRegisteredScript(JsScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        lock (scriptRegistryGate)
        {
            return registeredScripts.Contains(script);
        }
    }

    internal IReadOnlyCollection<JsScript> GetRegisteredScripts(string? sourcePath)
    {
        lock (scriptRegistryGate)
        {
            if (
                sourcePath is { Length: > 0 }
                && scriptsBySourcePath.TryGetValue(sourcePath, out var scripts)
            )
                return scripts.ToArray();

            if (sourcePath is null && scriptsWithoutSourcePath.Count != 0)
                return scriptsWithoutSourcePath.ToArray();

            return Array.Empty<JsScript>();
        }
    }

    internal IReadOnlyCollection<JsScript> GetAllRegisteredScripts()
    {
        lock (scriptRegistryGate)
        {
            return registeredScripts.ToArray();
        }
    }

    internal void ArmBreakpoints(JsScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        lock (scriptRegistryGate)
        {
            if (
                script.SourcePath is { Length: > 0 } sourcePath
                && scriptsBySourcePath.TryGetValue(sourcePath, out var scripts)
            )
            {
                foreach (var registeredScript in scripts)
                    breakpointRegistry.ArmPendingBreakpoints(this, registeredScript);
                return;
            }

            if (script.SourcePath is null && scriptsWithoutSourcePath.Contains(script)) { }

            breakpointRegistry.ArmPendingBreakpoints(this, script);
        }
    }

    internal bool TryRestoreBreakpointForHit(
        JsScript script,
        int pc,
        out string? sourcePath,
        out int line,
        out int column
    )
    {
        return breakpointRegistry.TryRestoreBreakpointForHit(
            script,
            pc,
            out sourcePath,
            out line,
            out column
        );
    }

    internal bool TryGetOriginalBreakpointInstruction(
        JsScript script,
        int pc,
        out JsBreakpointRegistry.OriginalInstructionInfo instruction
    )
    {
        return breakpointRegistry.TryGetOriginalInstruction(script, pc, out instruction);
    }

    public void AttachDebugger(IDebuggerSession debugger)
    {
        ExecutionCheckPolicy.AttachDebugger(debugger);
    }

    public void DetachDebugger()
    {
        ExecutionCheckPolicy.DetachDebugger();
    }

    public void AddConstraint(IExecutionConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        Options.AddConstraint(constraint);
        ExecutionCheckPolicy.AddConstraint(constraint);
        RefreshExecutionCheckScheduling(true);
    }

    public void SetCheckInterval(ulong checkInterval)
    {
        Options.SetCheckInterval(checkInterval);
        ExecutionCheckPolicy.SetCheckInterval(checkInterval);
        ExecutionCheckInterval = checkInterval;
        RefreshExecutionCheckScheduling(true);
    }

    public void SetMaxInstructions(ulong maxInstructions)
    {
        Options.SetMaxInstructions(maxInstructions);
        ExecutionCheckPolicy.SetMaxInstructions(maxInstructions);
        ExecutionCheckInterval = Math.Min(Options.CheckInterval, Options.MaxInstructions);
        RefreshExecutionCheckScheduling(false);
    }

    public void ResetExecutedInstructions()
    {
        ExecutionCheckPolicy.ResetExecutedInstructions();
        RefreshExecutionCheckScheduling(true);
    }

    public void SetExecutionTimeout(TimeSpan timeout)
    {
        Options.SetExecutionTimeout(timeout);
        ExecutionCheckPolicy.SetExecutionTimeout(TimeProvider, timeout);
        RefreshExecutionCheckScheduling(false);
    }

    public void ClearExecutionTimeout()
    {
        Options.ClearExecutionTimeout();
        ExecutionCheckPolicy.ClearExecutionTimeout();
        RefreshExecutionCheckScheduling(false);
    }

    public bool ResetExecutionTimeout()
    {
        var reset = ExecutionCheckPolicy.ResetExecutionTimeout(TimeProvider);
        if (reset)
            RefreshExecutionCheckScheduling(false);
        return reset;
    }

    public void ResetExecutionTimeout(TimeSpan timeout)
    {
        SetExecutionTimeout(timeout);
    }

    public void SetExecutionCancellationToken(CancellationToken cancellationToken)
    {
        Options.SetExecutionCancellationToken(cancellationToken);
        ExecutionCheckPolicy.SetExecutionCancellationToken(cancellationToken);
        RefreshExecutionCheckScheduling(false);
    }

    public void ClearExecutionCancellationToken()
    {
        Options.ClearExecutionCancellationToken();
        ExecutionCheckPolicy.ClearExecutionCancellationToken();
        RefreshExecutionCheckScheduling(false);
    }

    public void EnableCallHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.Call;
    }

    public void DisableCallHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.Call;
    }

    public void EnableReturnHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.Return;
    }

    public void DisableReturnHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.Return;
    }

    public void EnablePumpHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.Pump;
    }

    public void DisablePumpHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.Pump;
    }

    public void EnableSuspendGeneratorHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.SuspendGenerator;
    }

    public void DisableSuspendGeneratorHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.SuspendGenerator;
    }

    public void EnableResumeGeneratorHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.ResumeGenerator;
    }

    public void DisableResumeGeneratorHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.ResumeGenerator;
    }

    public void EnableDebuggerStatementHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.DebuggerStatement;
    }

    public void DisableDebuggerStatementHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.DebuggerStatement;
    }

    public void EnableBreakpointHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.Breakpoint;
    }

    public void DisableBreakpointHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.Breakpoint;
    }

    public void EnableCaughtExceptionHook()
    {
        ExecutionCheckpointHookBits |= (int)ExecutionCheckpointHooks.CaughtException;
    }

    public void DisableCaughtExceptionHook()
    {
        ExecutionCheckpointHookBits &= ~(int)ExecutionCheckpointHooks.CaughtException;
    }

    internal void RequestStepInto(
        int startStackDepth,
        CheckpointSourceLocation? startLocation = null
    )
    {
        SetStepRequest(DebuggerStepMode.Into, startStackDepth, startLocation);
    }

    internal void RequestStepOver(
        int startStackDepth,
        CheckpointSourceLocation? startLocation = null
    )
    {
        SetStepRequest(DebuggerStepMode.Over, startStackDepth, startLocation);
    }

    internal void RequestStepOut(
        int startStackDepth,
        CheckpointSourceLocation? startLocation = null
    )
    {
        SetStepRequest(DebuggerStepMode.Out, startStackDepth, startLocation);
    }

    internal void ClearStepRequest()
    {
        lock (stepGate)
        {
            stepRequest = null;
        }
    }

    internal bool TryConsumeStepRequest(
        in ExecutionCheckpoint checkpoint,
        out ExecutionCheckpointKind stepKind
    )
    {
        lock (stepGate)
        {
            if (stepRequest is not { } request)
            {
                stepKind = default;
                return false;
            }

            var snapshot = checkpoint.ToPausedSnapshot();
            if (!snapshot.MatchesStep(request.Mode, request.StartStackDepth, request.StartLocation))
            {
                stepKind = default;
                return false;
            }

            stepRequest = null;
            stepKind = ExecutionCheckpointKind.Step;
            return true;
        }
    }

    private void SetStepRequest(
        DebuggerStepMode mode,
        int startStackDepth,
        CheckpointSourceLocation? startLocation
    )
    {
        lock (stepGate)
        {
            stepRequest = new DebuggerStepRequest(mode, startStackDepth, startLocation);
        }
    }

    private void RefreshExecutionCheckScheduling(bool resetCountdown)
    {
        if (!ExecutionCheckPolicy.HasPeriodicChecks)
        {
            ExecutionCheckCountdown = ulong.MaxValue;
            return;
        }

        var interval = ExecutionCheckPolicy.CheckInterval;
        ExecutionCheckInterval = interval;
        if (
            resetCountdown
            || ExecutionCheckCountdown == ulong.MaxValue
            || ExecutionCheckCountdown > interval
        )
            ExecutionCheckCountdown = interval;
    }

    private void RegisterScriptRecursive(JsScript script)
    {
        if (!registeredScripts.Add(script))
            return;

        script.Agent = this;

        if (script.SourcePath is { Length: > 0 } sourcePath)
        {
            if (!scriptsBySourcePath.TryGetValue(sourcePath, out var scripts))
            {
                scripts = new(ReferenceEqualityComparer.Instance);
                scriptsBySourcePath[sourcePath] = scripts;
            }

            scripts.Add(script);
        }
        else
        {
            scriptsWithoutSourcePath.Add(script);
        }

        for (var i = 0; i < script.ObjectConstants.Length; i++)
            if (script.ObjectConstants[i] is JsBytecodeFunction function)
                RegisterScriptRecursive(function.Script);

        breakpointRegistry.ArmPendingBreakpoints(this, script);
    }

    internal string LoadModuleSourceByResolvedId(string resolvedId)
    {
        lock (moduleCacheGate)
        {
            if (moduleSourceCache.TryGetValue(resolvedId, out var cachedSource))
                return cachedSource;
        }

        var source = ModuleSourceLoader.LoadModule(resolvedId).GetRequiredSourceText();
        lock (moduleCacheGate)
        {
            moduleSourceCache.TryAdd(resolvedId, source);
            return moduleSourceCache[resolvedId];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int AllocatePrivateBrandId()
    {
        return Interlocked.Increment(ref nextPrivateBrandId);
    }

    internal void ClearModuleCaches()
    {
        lock (moduleCacheGate)
        {
            moduleSourceCache.Clear();
            ModuleGraph.Clear();
            jsonModuleNamespaceCache.Clear();
            textModuleNamespaceCache.Clear();
        }
    }

    internal bool InvalidateModuleByResolvedId(string resolvedId)
    {
        lock (moduleCacheGate)
        {
            var removedSource = moduleSourceCache.Remove(resolvedId);
            var removedNode = ModuleGraph.Remove(resolvedId);
            var removedJson = jsonModuleNamespaceCache.Remove(resolvedId);
            var removedText = textModuleNamespaceCache.Remove(resolvedId);
            return removedSource || removedNode || removedJson || removedText;
        }
    }

    internal JsAgentModuleApi.ModuleInvalidationResult InvalidateModuleByResolvedId(
        string resolvedId,
        JsAgentModuleApi.ModuleInvalidationScope scope
    )
    {
        lock (moduleCacheGate)
        {
            var invalidatedIds = new HashSet<string>(StringComparer.Ordinal) { resolvedId };
            if ((scope & JsAgentModuleApi.ModuleInvalidationScope.Importers) != 0)
                ModuleGraph.CollectImporterClosure(resolvedId, invalidatedIds);
            if ((scope & JsAgentModuleApi.ModuleInvalidationScope.Dependencies) != 0)
                ModuleGraph.CollectDependencyClosure(resolvedId, invalidatedIds);

            var removedCount = 0;
            foreach (var invalidatedId in invalidatedIds)
            {
                if (moduleSourceCache.Remove(invalidatedId))
                    removedCount++;
                if (ModuleGraph.Remove(invalidatedId))
                    removedCount++;
                if (jsonModuleNamespaceCache.Remove(invalidatedId))
                    removedCount++;
                if (textModuleNamespaceCache.Remove(invalidatedId))
                    removedCount++;
            }

            return new JsAgentModuleApi.ModuleInvalidationResult(
                resolvedId,
                scope,
                removedCount,
                invalidatedIds.Count == 0
                    ? []
                    : invalidatedIds
                        .OrderBy(static value => value, StringComparer.Ordinal)
                        .ToArray()
            );
        }
    }

    internal bool TryGetCachedModuleNamespaceByResolvedId(
        string resolvedId,
        out JsValue namespaceValue
    )
    {
        lock (moduleCacheGate)
        {
            if (jsonModuleNamespaceCache.TryGetValue(resolvedId, out var jsonNamespace))
            {
                namespaceValue = JsValue.FromObject(jsonNamespace);
                return true;
            }

            if (textModuleNamespaceCache.TryGetValue(resolvedId, out var textNamespace))
            {
                namespaceValue = JsValue.FromObject(textNamespace);
                return true;
            }

            if (!ModuleGraph.TryGet(resolvedId, out var node))
            {
                namespaceValue = JsValue.Undefined;
                return false;
            }

            namespaceValue = JsValue.FromObject(node.ExportsObject);
            return true;
        }
    }

    internal JsAgentModuleApi.ModuleStateSnapshot GetModuleStateSnapshotByResolvedId(
        string resolvedId,
        bool includeError
    )
    {
        lock (moduleCacheGate)
        {
            var hasSourceCache = moduleSourceCache.ContainsKey(resolvedId);
            if (!ModuleGraph.TryGet(resolvedId, out var node))
                return new(
                    resolvedId,
                    false,
                    JsAgentModuleApi.ModuleStateKind.Uninitialized,
                    false,
                    hasSourceCache,
                    null
                );

            JsAgentModuleApi.ModuleErrorSnapshot? errorSnapshot = null;
            if (includeError && node.LastError is not null)
            {
                var err = node.LastError;
                if (err is JsRuntimeException runtimeEx)
                    errorSnapshot = new JsAgentModuleApi.ModuleErrorSnapshot(
                        runtimeEx.DetailCode,
                        runtimeEx.Message,
                        runtimeEx.GetType().FullName ?? runtimeEx.GetType().Name,
                        runtimeEx.InnerException?.GetType().FullName
                            ?? runtimeEx.InnerException?.GetType().Name
                    );
                else
                    errorSnapshot = new JsAgentModuleApi.ModuleErrorSnapshot(
                        null,
                        err.Message,
                        err.GetType().FullName ?? err.GetType().Name,
                        err.InnerException?.GetType().FullName ?? err.InnerException?.GetType().Name
                    );
            }

            return new(
                resolvedId,
                true,
                MapModuleState(node.State),
                node.LinkPlan is not null,
                hasSourceCache,
                errorSnapshot
            );
        }
    }

    private static JsAgentModuleApi.ModuleStateKind MapModuleState(ModuleEvalState state)
    {
        return state switch
        {
            ModuleEvalState.Uninitialized => JsAgentModuleApi.ModuleStateKind.Uninitialized,
            ModuleEvalState.Instantiating => JsAgentModuleApi.ModuleStateKind.Instantiating,
            ModuleEvalState.Evaluating => JsAgentModuleApi.ModuleStateKind.Evaluating,
            ModuleEvalState.Evaluated => JsAgentModuleApi.ModuleStateKind.Evaluated,
            ModuleEvalState.Failed => JsAgentModuleApi.ModuleStateKind.Failed,
            _ => JsAgentModuleApi.ModuleStateKind.Uninitialized,
        };
    }

    /// <summary>Enqueues a ScriptJobs queue job (script/module evaluation).</summary>
    public void EnqueueScriptJob(Action job)
    {
        EnqueueScriptJob(SInvokeActionJob, job);
    }

    /// <summary>Enqueues a ScriptJobs queue job (script/module evaluation).</summary>
    public void EnqueueScriptJob(Action<object?> callback, object? state)
    {
        if (terminated)
            return;
        lock (jobsGate)
        {
            scriptJobs.Enqueue(new(callback, state));
        }

        SignalJobsAvailable();
    }

    /// <summary>Enqueues a PromiseJobs queue job (promise reaction, async continuation, queueMicrotask).</summary>
    public void EnqueuePromiseJob(Action job)
    {
        EnqueuePromiseJob(SInvokeActionJob, job);
    }

    /// <summary>Enqueues a PromiseJobs queue job (promise reaction, async continuation, queueMicrotask).</summary>
    public void EnqueuePromiseJob(Action<object?> callback, object? state)
    {
        if (terminated)
            return;
        lock (jobsGate)
        {
            promiseJobs.Enqueue(new(callback, state));
        }

        SignalJobsAvailable();
    }

    /// <summary>
    ///     Enqueues a job into a host-defined job queue. The engine owns only
    ///     <see cref="JobQueueName.ScriptJobs"/> and <see cref="JobQueueName.PromiseJobs"/>;
    ///     this seam lets hosts define additional named job classes (timers, I/O, rendering).
    /// </summary>
    public void EnqueueJob(string queueName, Action job)
    {
        EnqueueJob(queueName, SInvokeActionJob, job);
    }

    /// <summary>
    ///     Enqueues a job into a host-defined job queue. Unknown queue names default to the
    ///     <see cref="JobQueueName.HostJobs"/> queue.
    /// </summary>
    public void EnqueueJob(string queueName, Action<object?> callback, object? state)
    {
        if (terminated)
            return;

        if (queueName is not JobQueueName.ScriptJobs and not JobQueueName.PromiseJobs)
        {
            enqueueHostJob(queueName, callback, state);
            return;
        }

        lock (jobsGate)
        {
            switch (queueName)
            {
                case JobQueueName.ScriptJobs:
                    scriptJobs.Enqueue(new(callback, state));
                    break;
                case JobQueueName.PromiseJobs:
                    promiseJobs.Enqueue(new(callback, state));
                    break;
            }
        }

        SignalJobsAvailable();
    }

    /// <summary>Enqueues a host task that runs before Promise jobs (e.g. Node nextTick).</summary>
    internal void EnqueueHostPriorityJob(Action job)
    {
        EnqueueHostPriorityJob(SInvokeActionJob, job);
    }

    /// <summary>Enqueues a host task that runs before Promise jobs (e.g. Node nextTick).</summary>
    internal void EnqueueHostPriorityJob(Action<object?> callback, object? state)
    {
        enqueueHostJob(JobQueueName.HostPriorityJobs, callback, state);
    }

    /// <summary>Enqueues a host task. Routes through the host scheduler when one is queued.</summary>
    internal void EnqueueHostTask(Action job)
    {
        EnqueueHostTask(SInvokeActionJob, job);
    }

    /// <summary>Enqueues a host task. Routes through the host scheduler when one is queued.</summary>
    internal void EnqueueHostTask(Action<object?> callback, object? state)
    {
        EnqueueHostTask(InternalHostTaskQueueDefaults.Default, callback, state);
    }

    /// <summary>Enqueues a host task onto a specific host queue, or the default host queue.</summary>
    internal void EnqueueHostTask(
        HostTaskQueueKey queueKey,
        Action<object?> callback,
        object? state
    )
    {
        enqueueHostTask(queueKey, callback, state);
    }

    // Cross-agent messaging follows host-task semantics (message events are host tasks).
    public void PostMessage(JsAgent target, object? payload)
    {
        PostMessage(target, payload, InternalHostTaskQueueDefaults.Default);
    }

    public void PostMessage(JsAgent target, object? payload, HostTaskQueueKey queueKey)
    {
        if (!ReferenceEquals(target.RuntimeIdentity, RuntimeIdentity))
            throw new InvalidOperationException("Cross-engine postMessage is not supported.");
        if (terminated || target.terminated)
            return;

        var clonedPayload = MessageSerializer.CloneCrossAgentPayload(payload);
        target.EnqueueHostTask(
            queueKey,
            SPostMessageTask,
            new PostMessageTaskState(this, target, clonedPayload)
        );
    }

    /// <summary>
    ///     Runs one iteration of the default job-order policy: host priority jobs, then
    ///     Promise jobs (microtask checkpoint), then one host task. Hosts that need precise
    ///     control should use <see cref="RunJobs"/> / <see cref="RunPromiseJobs"/> instead.
    /// </summary>
    internal void PumpJobs()
    {
        if (terminated)
            return;
        var ownsPumpLoop = false;
        lock (jobsGate)
        {
            if (!isPumpingJobs)
            {
                isPumpingJobs = true;
                ownsPumpLoop = true;
            }
        }

        if (!ownsPumpLoop)
            return;

        try
        {
            pumpHostJobs();
        }
        finally
        {
            if (ownsPumpLoop)
                lock (jobsGate)
                {
                    isPumpingJobs = false;
                }
        }
    }

    /// <summary>Runs all pending jobs in a named queue, FIFO. Returns the number executed.</summary>
    public int RunJobs(string queueName)
    {
        if (terminated)
            return 0;
        if (queueName is not JobQueueName.ScriptJobs and not JobQueueName.PromiseJobs)
            return runHostJobs(queueName);

        var executed = 0;
        while (TryDequeueJob(queueName, out var job))
        {
            job.Invoke();
            executed++;
        }

        return executed;
    }

    /// <summary>Runs all pending ScriptJobs queue jobs, FIFO.</summary>
    public int RunScriptJobs() => RunJobs(JobQueueName.ScriptJobs);

    /// <summary>Runs all pending PromiseJobs queue jobs, FIFO (the microtask checkpoint).</summary>
    public int RunPromiseJobs() => RunJobs(JobQueueName.PromiseJobs);

    /// <summary>Runs all pending host priority jobs, FIFO.</summary>
    internal int RunHostPriorityJobs() => RunJobs(JobQueueName.HostPriorityJobs);

    /// <summary>Runs one pending host job.</summary>
    internal bool RunOneHostJob()
    {
        return runOneHostJob();
    }

    /// <summary>Runs all pending host jobs, FIFO.</summary>
    internal int RunHostJobs() => runHostJobs(JobQueueName.HostJobs);

    public void Terminate()
    {
        if (terminated)
            return;

        lock (lifecycleGate)
        {
            if (terminated)
                return;
            terminated = true;
        }

        lock (jobsGate)
        {
            scriptJobs.Clear();
            promiseJobs.Clear();
            isPumpingJobs = false;
        }
        clearHostJobs();

        lock (moduleCacheGate)
        {
            moduleSourceCache.Clear();
            ModuleGraph.Clear();
        }

        ClearKeptObjects();

        SignalJobsAvailable();
    }

    private bool TryDequeueJob(string queueName, out PendingJob job)
    {
        lock (jobsGate)
        {
            var queue = queueName switch
            {
                JobQueueName.ScriptJobs => scriptJobs,
                JobQueueName.PromiseJobs => promiseJobs,
                _ => null,
            };
            if (queue is null)
            {
                job = default;
                return false;
            }
            if (queue.Count == 0)
            {
                job = default;
                return false;
            }

            job = queue.Dequeue();
            return true;
        }
    }

    internal void SignalJobsAvailable()
    {
        if (disposed)
            return;

        jobsAvailable.Set();
    }

    private readonly struct PendingJob(Action<object?> callback, object? state)
    {
        public readonly Action<object?> Callback = callback;
        public readonly object? State = state;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke()
        {
            Callback(State);
        }
    }

    private sealed class PostMessageTaskState(JsAgent source, JsAgent target, object? payload)
    {
        public readonly object? Payload = payload;
        public readonly JsAgent Source = source;
        public readonly JsAgent Target = target;
    }

    public sealed class JsScriptDebugRegistry
    {
        private readonly JsAgent agent;

        internal JsScriptDebugRegistry(JsAgent agent)
        {
            this.agent = agent;
        }

        public bool IsRegisteredScript(JsScript script)
        {
            return agent.IsRegisteredScript(script);
        }

        public IReadOnlyCollection<JsScript> GetRegisteredScripts(string? sourcePath)
        {
            return agent.GetRegisteredScripts(sourcePath);
        }

        public IReadOnlyCollection<JsScript> GetAllRegisteredScripts()
        {
            return agent.GetAllRegisteredScripts();
        }
    }

    private readonly record struct DebuggerStepRequest(
        DebuggerStepMode Mode,
        int StartStackDepth,
        CheckpointSourceLocation? StartLocation
    );
}
