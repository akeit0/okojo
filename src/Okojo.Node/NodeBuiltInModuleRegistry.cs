using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeBuiltInModuleRegistry
{
    private const int PathJoinSlot = 0;
    private const int PathNormalizeSlot = 1;
    private const int PathDirnameSlot = 2;
    private const int PathBasenameSlot = 3;
    private const int PathExtnameSlot = 4;
    private const int PathResolveSlot = 5;
    private const int PathRelativeSlot = 6;
    private const int PathSepSlot = 7;
    private const int PathDelimiterSlot = 8;

    private const int BufferFromSlot = 0;
    private const int BufferAllocSlot = 1;
    private const int BufferIsBufferSlot = 2;
    private const int BufferByteLengthSlot = 3;

    private const int BufferModuleBufferSlot = 0;
    private const int BufferModuleMaxLengthSlot = 1;
    private const int BufferModuleStringMaxLengthSlot = 2;

    private const int ProcessCwdSlot = 0;
    private const int ProcessEnvSlot = 1;
    private const int ProcessArgvSlot = 2;
    private const int ProcessPlatformSlot = 3;
    private const int ProcessVersionSlot = 4;
    private const int ProcessVersionsSlot = 5;
    private const int ProcessNextTickSlot = 6;
    private const int ProcessStdinSlot = 7;
    private const int ProcessStdoutSlot = 8;
    private const int ProcessStderrSlot = 9;

    private const int ProcessVersionsNodeSlot = 0;
    private const int ProcessVersionsOkojoSlot = 1;
    private readonly NodeAssertBuiltIn assertBuiltIn;
    private readonly NodeChildProcessBuiltIn childProcessBuiltIn;
    private readonly NodeConsoleBuiltIn consoleBuiltIn;
    private readonly NodeEventsBuiltIn eventsBuiltIn;
    private readonly NodeFsBuiltIn fsBuiltIn;
    private readonly ConditionalWeakTable<JsRealm, RealmImmediateState> immediateStates = new();
    private readonly NodeModuleBuiltIn moduleBuiltIn;
    private readonly Dictionary<string, JsValue> moduleCache = new(StringComparer.Ordinal);
    private readonly NodeOsBuiltIn osBuiltIn;
    private readonly NodePerformanceBuiltIn performanceBuiltIn;
    private readonly NodeReplBuiltIn replBuiltIn;
    private readonly string reportedNodeVersion;
    private readonly string reportedOkojoVersion;
    private readonly string reportedProcessVersion;

    private readonly NodeRuntime runtime;
    private readonly NodeStreamBuiltIn streamBuiltIn;
    private readonly NodeTimersBuiltIn timersBuiltIn;
    private readonly NodeTtyBuiltIn ttyBuiltIn;
    private readonly NodeUrlBuiltIn urlBuiltIn;
    private readonly NodeUtilBuiltIn utilBuiltIn;
    private int atomAlloc = -1;

    private int atomArgv = -1;
    private int atomBasename = -1;
    private int atomBuffer = -1;
    private int atomByteLength = -1;
    private int atomCwd = -1;
    private int atomDelimiter = -1;
    private int atomDirname = -1;
    private int atomEnv = -1;
    private int atomExtname = -1;
    private int atomFrom = -1;
    private int atomIsBuffer = -1;
    private int atomJoin = -1;
    private int atomKMaxLength = -1;
    private int atomKStringMaxLength = -1;
    private int atomNextTick = -1;
    private int atomNode = -1;
    private int atomNormalize = -1;
    private int atomOkojo = -1;
    private int atomPath = -1;
    private int atomPlatform = -1;
    private int atomProcess = -1;
    private int atomRelative = -1;
    private int atomResolve = -1;
    private int atomSep = -1;
    private int atomStderr = -1;
    private int atomStdin = -1;
    private int atomStdout = -1;
    private int atomVersion = -1;
    private int atomVersions = -1;
    private JsPlainObject? bufferModule;
    private StaticNamedPropertyLayout? bufferModuleShape;
    private JsPlainObject? bufferObject;
    private StaticNamedPropertyLayout? bufferShape;
    private JsPlainObject? pathModule;
    private StaticNamedPropertyLayout? pathShape;
    private JsPlainObject? processObject;
    private StaticNamedPropertyLayout? processShape;
    private JsPlainObject? processVersionsObject;
    private StaticNamedPropertyLayout? processVersionsShape;

    public NodeBuiltInModuleRegistry(NodeRuntime runtime, NodeTerminalOptions terminalOptions)
    {
        this.runtime = runtime;
        reportedNodeVersion = GetReportedNodeVersion();
        reportedProcessVersion = "v" + reportedNodeVersion;
        reportedOkojoVersion = reportedNodeVersion;
        assertBuiltIn = new(runtime);
        childProcessBuiltIn = new(runtime);
        eventsBuiltIn = new(runtime);
        fsBuiltIn = new(runtime);
        moduleBuiltIn = new(runtime);
        osBuiltIn = new(runtime);
        performanceBuiltIn = new(runtime);
        timersBuiltIn = new(runtime);
        streamBuiltIn = new(runtime, eventsBuiltIn);
        utilBuiltIn = new(runtime);
        urlBuiltIn = new(runtime);
        ttyBuiltIn = new(
            runtime,
            eventsBuiltIn,
            terminalOptions.Stdout,
            terminalOptions.Stderr,
            terminalOptions.StdinIsTty,
            terminalOptions.StdoutIsTty,
            terminalOptions.StderrIsTty,
            terminalOptions.StdoutColumns,
            terminalOptions.StdoutRows,
            terminalOptions.StderrColumns,
            terminalOptions.StderrRows);
        consoleBuiltIn = new(runtime, ttyBuiltIn);
        replBuiltIn = new(runtime);
    }

    public bool TryGetBuiltInModule(string specifier, out JsValue exports)
    {
        if (moduleCache.TryGetValue(specifier, out exports))
            return true;

        exports = specifier switch
        {
            "node:assert" or "assert" => JsValue.FromObject(assertBuiltIn.GetModule()),
            "node:console" or "console" => JsValue.FromObject(consoleBuiltIn.GetModule()),
            "node:process" or "process" => JsValue.FromObject(GetProcessObject()),
            "node:path" or "path" => JsValue.FromObject(GetPathModule()),
            "node:os" or "os" => JsValue.FromObject(osBuiltIn.GetModule()),
            "node:perf_hooks" or "perf_hooks" => JsValue.FromObject(performanceBuiltIn.GetModule()),
            "node:fs" or "fs" => JsValue.FromObject(fsBuiltIn.GetModule()),
            "node:child_process" or "child_process" => JsValue.FromObject(childProcessBuiltIn.GetModule()),
            "node:buffer" or "buffer" => JsValue.FromObject(GetBufferModule()),
            "node:events" or "events" => JsValue.FromObject(eventsBuiltIn.GetModule()),
            "node:module" or "module" => JsValue.FromObject(moduleBuiltIn.GetModule()),
            "node:stream" or "stream" => JsValue.FromObject(streamBuiltIn.GetModule()),
            "node:timers" or "timers" => JsValue.FromObject(timersBuiltIn.GetTimersModule()),
            "node:timers/promises" or "timers/promises" => JsValue.FromObject(timersBuiltIn.GetTimersPromisesModule()),
            "node:tty" or "tty" => JsValue.FromObject(ttyBuiltIn.GetTtyModule()),
            "node:url" or "url" => JsValue.FromObject(urlBuiltIn.GetModule()),
            "node:util" or "util" => JsValue.FromObject(utilBuiltIn.GetModule()),
            "node:repl" or "repl" => JsValue.FromObject(replBuiltIn.GetModule()),
            _ => JsValue.Undefined
        };

        if (exports.IsUndefined)
            return false;

        moduleCache[specifier] = exports;
        return true;
    }

    public JsPlainObject GetProcessObject()
    {
        if (processObject is not null)
            return processObject;

        var realm = runtime.MainRealm;
        var shape = processShape ??= CreateProcessShape(realm);
        var process = new JsPlainObject(shape);
        process.SetNamedSlotUnchecked(ProcessCwdSlot, JsValue.FromObject(CreateCwdFunction(realm)));
        process.SetNamedSlotUnchecked(ProcessEnvSlot, JsValue.FromObject(CreateProcessEnvObject(realm)));
        process.SetNamedSlotUnchecked(ProcessArgvSlot, JsValue.FromObject(CreateArgvArray(realm, null, null)));
        process.SetNamedSlotUnchecked(ProcessPlatformSlot, JsValue.FromString(GetPlatformString()));
        process.SetNamedSlotUnchecked(ProcessVersionSlot, JsValue.FromString(reportedProcessVersion));
        process.SetNamedSlotUnchecked(ProcessVersionsSlot, JsValue.FromObject(GetProcessVersionsObject()));
        process.SetNamedSlotUnchecked(ProcessNextTickSlot, JsValue.FromObject(CreateNextTickFunction(realm)));
        process.SetNamedSlotUnchecked(ProcessStdinSlot, JsValue.FromObject(ttyBuiltIn.GetStdinObject()));
        process.SetNamedSlotUnchecked(ProcessStdoutSlot, JsValue.FromObject(ttyBuiltIn.GetStdoutObject()));
        process.SetNamedSlotUnchecked(ProcessStderrSlot, JsValue.FromObject(ttyBuiltIn.GetStderrObject()));
        processObject = process;
        return process;
    }

    public void SetProcessArgv(string entryPath, IReadOnlyList<string>? argv)
    {
        var realm = runtime.MainRealm;
        var nextArgv = JsValue.FromObject(CreateArgvArray(realm, entryPath, argv));
        if (processObject is null)
        {
            GetProcessObject().SetNamedSlotUnchecked(ProcessArgvSlot, nextArgv);
            return;
        }

        processObject.SetNamedSlotUnchecked(ProcessArgvSlot, nextArgv);
    }

    internal JsPlainObject GetPathModule()
    {
        if (pathModule is not null)
            return pathModule;

        var realm = runtime.MainRealm;
        var shape = pathShape ??= CreatePathShape(realm);
        var pathModuleObject = new JsPlainObject(shape);
        pathModuleObject.SetNamedSlotUnchecked(PathJoinSlot, JsValue.FromObject(CreateJoinFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathNormalizeSlot, JsValue.FromObject(CreateNormalizeFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathDirnameSlot, JsValue.FromObject(CreateDirnameFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathBasenameSlot, JsValue.FromObject(CreateBasenameFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathExtnameSlot, JsValue.FromObject(CreateExtnameFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathResolveSlot, JsValue.FromObject(CreateResolveFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathRelativeSlot, JsValue.FromObject(CreateRelativeFunction(realm)));
        pathModuleObject.SetNamedSlotUnchecked(PathSepSlot, JsValue.FromString(Path.DirectorySeparatorChar.ToString()));
        pathModuleObject.SetNamedSlotUnchecked(PathDelimiterSlot, JsValue.FromString(Path.PathSeparator.ToString()));
        pathModule = pathModuleObject;
        return pathModuleObject;
    }

    public JsPlainObject GetBufferObject()
    {
        if (bufferObject is not null)
            return bufferObject;

        var realm = runtime.MainRealm;
        var shape = bufferShape ??= CreateBufferShape(realm);
        var buffer = new JsPlainObject(shape);
        buffer.SetNamedSlotUnchecked(BufferFromSlot, JsValue.FromObject(CreateBufferFromFunction(realm)));
        buffer.SetNamedSlotUnchecked(BufferAllocSlot, JsValue.FromObject(CreateBufferAllocFunction(realm)));
        buffer.SetNamedSlotUnchecked(BufferIsBufferSlot, JsValue.FromObject(CreateBufferIsBufferFunction(realm)));
        buffer.SetNamedSlotUnchecked(BufferByteLengthSlot, JsValue.FromObject(CreateBufferByteLengthFunction(realm)));
        bufferObject = buffer;
        return buffer;
    }

    public JsPlainObject GetBufferModule()
    {
        if (bufferModule is not null)
            return bufferModule;

        var realm = runtime.MainRealm;
        var shape = bufferModuleShape ??= CreateBufferModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(BufferModuleBufferSlot, JsValue.FromObject(GetBufferObject()));
        module.SetNamedSlotUnchecked(BufferModuleMaxLengthSlot, JsValue.FromInt32(int.MaxValue));
        module.SetNamedSlotUnchecked(BufferModuleStringMaxLengthSlot, JsValue.FromInt32(536870888));
        bufferModule = module;
        return module;
    }

    public JsHostFunction CreateSetImmediateFunction(JsRealm realm)
    {
        return new(realm, "setImmediate", 1, (in info) =>
        {
            var callbackValue = info.GetArgument(0);
            if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
                throw new JsRuntimeException(JsErrorKind.TypeError, "setImmediate callback must be callable");

            var args = info.Arguments.Length <= 1 ? [] : info.Arguments[1..].ToArray();
            return JsValue.FromInt32(CreateImmediateRequest(info.Realm, callback, args));
        }, false);
    }

    public JsHostFunction CreateClearImmediateFunction(JsRealm realm)
    {
        return new(realm, "clearImmediate", 1, (in info) =>
        {
            if (info.Arguments.Length != 0)
                CancelImmediateRequest(info.Realm, ToImmediateRequestId(info.Arguments[0]));
            return JsValue.Undefined;
        }, false);
    }

    public JsPlainObject GetBuiltInEventsModule()
    {
        return eventsBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInOsModule()
    {
        return osBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInPerformanceModule()
    {
        return performanceBuiltIn.GetModule();
    }

    public JsPlainObject GetPerformanceObject()
    {
        return performanceBuiltIn.GetPerformanceObject();
    }

    public JsPlainObject GetBuiltInChildProcessModule()
    {
        return childProcessBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInFsModule()
    {
        return fsBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInTtyModule()
    {
        return ttyBuiltIn.GetTtyModule();
    }

    public JsPlainObject GetBuiltInStreamModule()
    {
        return streamBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInModuleModule()
    {
        return moduleBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInUtilModule()
    {
        return utilBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInUrlModule()
    {
        return urlBuiltIn.GetModule();
    }

    public JsPlainObject GetBuiltInAssertModule()
    {
        return assertBuiltIn.GetModule();
    }

    public JsPlainObject GetConsoleObject()
    {
        return consoleBuiltIn.GetGlobalConsoleObject();
    }

    private JsPlainObject GetProcessVersionsObject()
    {
        if (processVersionsObject is not null)
            return processVersionsObject;

        var realm = runtime.MainRealm;
        var shape = processVersionsShape ??= CreateProcessVersionsShape(realm);
        var versions = new JsPlainObject(shape);
        versions.SetNamedSlotUnchecked(ProcessVersionsNodeSlot, JsValue.FromString(reportedNodeVersion));
        versions.SetNamedSlotUnchecked(ProcessVersionsOkojoSlot, JsValue.FromString(reportedOkojoVersion));
        processVersionsObject = versions;
        return versions;
    }

    private static string GetReportedNodeVersion()
    {
        var assembly = typeof(NodeRuntime).Assembly;
        var informationalVersion = assembly
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return NormalizeReportedVersion(informationalVersion!);

        var version = assembly.GetName().Version;
        return version is not null ? NormalizeReportedVersion(version.ToString()) : "0.0.0-local";
    }

    private static string NormalizeReportedVersion(string version)
    {
        var normalized = version.Trim();
        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
            normalized = normalized[..plusIndex];
        return normalized;
    }

    private JsPlainObject CreateProcessEnvObject(JsRealm realm)
    {
        var env = new JsPlainObject(realm, useDictionaryMode: true);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key || entry.Value is null)
                continue;

            env[key] = JsValue.FromString(entry.Value.ToString() ?? string.Empty);
        }

        return env;
    }

    private JsArray CreateArgvArray(JsRealm realm, string? entryPath, IReadOnlyList<string>? args)
    {
        var argvArray = new JsArray(realm);
        argvArray[0] = JsValue.FromString("okojo");
        if (!string.IsNullOrEmpty(entryPath))
            argvArray[1] = JsValue.FromString(entryPath);
        if (args is not null)
            for (var i = 0; i < args.Count; i++)
                argvArray[(uint)(i + 2)] = JsValue.FromString(args[i]);
        return argvArray;
    }

    private StaticNamedPropertyLayout CreateProcessShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomCwd, JsShapePropertyFlags.Open, out var cwdInfo);
        shape = shape.GetOrAddTransition(atomEnv, JsShapePropertyFlags.Open, out var envInfo);
        shape = shape.GetOrAddTransition(atomArgv, JsShapePropertyFlags.Open, out var argvInfo);
        shape = shape.GetOrAddTransition(atomPlatform, JsShapePropertyFlags.Open, out var platformInfo);
        shape = shape.GetOrAddTransition(atomVersion, JsShapePropertyFlags.Open, out var versionInfo);
        shape = shape.GetOrAddTransition(atomVersions, JsShapePropertyFlags.Open, out var versionsInfo);
        shape = shape.GetOrAddTransition(atomNextTick, JsShapePropertyFlags.Open, out var nextTickInfo);
        shape = shape.GetOrAddTransition(atomStdin, JsShapePropertyFlags.Open, out var stdinInfo);
        shape = shape.GetOrAddTransition(atomStdout, JsShapePropertyFlags.Open, out var stdoutInfo);
        shape = shape.GetOrAddTransition(atomStderr, JsShapePropertyFlags.Open, out var stderrInfo);
        Debug.Assert(cwdInfo.Slot == ProcessCwdSlot);
        Debug.Assert(envInfo.Slot == ProcessEnvSlot);
        Debug.Assert(argvInfo.Slot == ProcessArgvSlot);
        Debug.Assert(platformInfo.Slot == ProcessPlatformSlot);
        Debug.Assert(versionInfo.Slot == ProcessVersionSlot);
        Debug.Assert(versionsInfo.Slot == ProcessVersionsSlot);
        Debug.Assert(nextTickInfo.Slot == ProcessNextTickSlot);
        Debug.Assert(stdinInfo.Slot == ProcessStdinSlot);
        Debug.Assert(stdoutInfo.Slot == ProcessStdoutSlot);
        Debug.Assert(stderrInfo.Slot == ProcessStderrSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateProcessVersionsShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomNode, JsShapePropertyFlags.Open, out var nodeInfo);
        shape = shape.GetOrAddTransition(atomOkojo, JsShapePropertyFlags.Open, out var okojoInfo);
        Debug.Assert(nodeInfo.Slot == ProcessVersionsNodeSlot);
        Debug.Assert(okojoInfo.Slot == ProcessVersionsOkojoSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreatePathShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomJoin, JsShapePropertyFlags.Open, out var joinInfo);
        shape = shape.GetOrAddTransition(atomNormalize, JsShapePropertyFlags.Open, out var normalizeInfo);
        shape = shape.GetOrAddTransition(atomDirname, JsShapePropertyFlags.Open, out var dirnameInfo);
        shape = shape.GetOrAddTransition(atomBasename, JsShapePropertyFlags.Open, out var basenameInfo);
        shape = shape.GetOrAddTransition(atomExtname, JsShapePropertyFlags.Open, out var extnameInfo);
        shape = shape.GetOrAddTransition(atomResolve, JsShapePropertyFlags.Open, out var resolveInfo);
        shape = shape.GetOrAddTransition(atomRelative, JsShapePropertyFlags.Open, out var relativeInfo);
        shape = shape.GetOrAddTransition(atomSep, JsShapePropertyFlags.Open, out var sepInfo);
        shape = shape.GetOrAddTransition(atomDelimiter, JsShapePropertyFlags.Open, out var delimiterInfo);
        Debug.Assert(joinInfo.Slot == PathJoinSlot);
        Debug.Assert(normalizeInfo.Slot == PathNormalizeSlot);
        Debug.Assert(dirnameInfo.Slot == PathDirnameSlot);
        Debug.Assert(basenameInfo.Slot == PathBasenameSlot);
        Debug.Assert(extnameInfo.Slot == PathExtnameSlot);
        Debug.Assert(resolveInfo.Slot == PathResolveSlot);
        Debug.Assert(relativeInfo.Slot == PathRelativeSlot);
        Debug.Assert(sepInfo.Slot == PathSepSlot);
        Debug.Assert(delimiterInfo.Slot == PathDelimiterSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateBufferShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomFrom, JsShapePropertyFlags.Open, out var fromInfo);
        shape = shape.GetOrAddTransition(atomAlloc, JsShapePropertyFlags.Open, out var allocInfo);
        shape = shape.GetOrAddTransition(atomIsBuffer, JsShapePropertyFlags.Open, out var isBufferInfo);
        shape = shape.GetOrAddTransition(atomByteLength, JsShapePropertyFlags.Open, out var byteLengthInfo);
        Debug.Assert(fromInfo.Slot == BufferFromSlot);
        Debug.Assert(allocInfo.Slot == BufferAllocSlot);
        Debug.Assert(isBufferInfo.Slot == BufferIsBufferSlot);
        Debug.Assert(byteLengthInfo.Slot == BufferByteLengthSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateBufferModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomBuffer, JsShapePropertyFlags.Open, out var bufferInfo);
        shape = shape.GetOrAddTransition(atomKMaxLength, JsShapePropertyFlags.Open, out var maxLengthInfo);
        shape = shape.GetOrAddTransition(atomKStringMaxLength, JsShapePropertyFlags.Open, out var maxStringLengthInfo);
        Debug.Assert(bufferInfo.Slot == BufferModuleBufferSlot);
        Debug.Assert(maxLengthInfo.Slot == BufferModuleMaxLengthSlot);
        Debug.Assert(maxStringLengthInfo.Slot == BufferModuleStringMaxLengthSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomArgv = EnsureAtom(realm, atomArgv, "argv");
        atomBasename = EnsureAtom(realm, atomBasename, "basename");
        atomCwd = EnsureAtom(realm, atomCwd, "cwd");
        atomDelimiter = EnsureAtom(realm, atomDelimiter, "delimiter");
        atomDirname = EnsureAtom(realm, atomDirname, "dirname");
        atomEnv = EnsureAtom(realm, atomEnv, "env");
        atomExtname = EnsureAtom(realm, atomExtname, "extname");
        atomFrom = EnsureAtom(realm, atomFrom, "from");
        atomAlloc = EnsureAtom(realm, atomAlloc, "alloc");
        atomIsBuffer = EnsureAtom(realm, atomIsBuffer, "isBuffer");
        atomByteLength = EnsureAtom(realm, atomByteLength, "byteLength");
        atomBuffer = EnsureAtom(realm, atomBuffer, "Buffer");
        atomJoin = EnsureAtom(realm, atomJoin, "join");
        atomNormalize = EnsureAtom(realm, atomNormalize, "normalize");
        atomKMaxLength = EnsureAtom(realm, atomKMaxLength, "kMaxLength");
        atomKStringMaxLength = EnsureAtom(realm, atomKStringMaxLength, "kStringMaxLength");
        atomNode = EnsureAtom(realm, atomNode, "node");
        atomOkojo = EnsureAtom(realm, atomOkojo, "okojo");
        atomPath = EnsureAtom(realm, atomPath, "path");
        atomPlatform = EnsureAtom(realm, atomPlatform, "platform");
        atomProcess = EnsureAtom(realm, atomProcess, "process");
        atomNextTick = EnsureAtom(realm, atomNextTick, "nextTick");
        atomStdin = EnsureAtom(realm, atomStdin, "stdin");
        atomStdout = EnsureAtom(realm, atomStdout, "stdout");
        atomStderr = EnsureAtom(realm, atomStderr, "stderr");
        atomResolve = EnsureAtom(realm, atomResolve, "resolve");
        atomRelative = EnsureAtom(realm, atomRelative, "relative");
        atomSep = EnsureAtom(realm, atomSep, "sep");
        atomVersion = EnsureAtom(realm, atomVersion, "version");
        atomVersions = EnsureAtom(realm, atomVersions, "versions");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static string GetPlatformString()
    {
        if (OperatingSystem.IsWindows())
            return "win32";
        if (OperatingSystem.IsMacOS())
            return "darwin";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "unknown";
    }

    private static JsHostFunction CreateCwdFunction(JsRealm realm)
    {
        return new(realm, "cwd", 0, static (in info) => { return JsValue.FromString(Environment.CurrentDirectory); },
            false);
    }

    private static JsHostFunction CreateNextTickFunction(JsRealm realm)
    {
        return new(realm, "nextTick", 1, static (in info) =>
        {
            var callbackValue = info.GetArgument(0);
            if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
                throw new JsRuntimeException(JsErrorKind.TypeError, "process.nextTick callback must be callable");

            var args = info.Arguments.Length <= 1 ? [] : info.Arguments[1..].ToArray();
            info.Realm.Agent.EnqueueHostPriorityMicrotask(static state =>
            {
                var invocation = (NextTickInvocation)state!;
                _ = invocation.Realm.Call(invocation.Callback, JsValue.Undefined, invocation.Args);
            }, new NextTickInvocation(info.Realm, callback, args));
            return JsValue.Undefined;
        }, false);
    }

    private int CreateImmediateRequest(JsRealm realm, JsFunction callback, JsValue[] args)
    {
        var state = immediateStates.GetValue(realm, static _ => new());
        ImmediateRequestState request;
        lock (state.Gate)
        {
            var requestId = ++state.NextRequestId;
            request = new()
            {
                OwnerState = state,
                Realm = realm,
                PublicRequestId = requestId,
                Callback = callback,
                Arguments = args
            };
            state.Requests.Add(requestId, request);
        }

        realm.QueueHostTask(
            NodeTaskQueueKeys.Check,
            CreateImmediateDriver(realm), JsValue.FromInt32(request.PublicRequestId));
        return request.PublicRequestId;
    }

    private void CancelImmediateRequest(JsRealm realm, int requestId)
    {
        var state = immediateStates.GetValue(realm, static _ => new());
        lock (state.Gate)
        {
            if (!state.Requests.Remove(requestId, out var request))
                return;

            request.Active = false;
        }
    }

    private JsHostFunction CreateImmediateDriver(JsRealm realm)
    {
        return new(realm, "setImmediate callback", 1, (in info) =>
        {
            var registry = (NodeBuiltInModuleRegistry)((JsHostFunction)info.Function).UserData!;
            registry.InvokeImmediate(info.Realm, ToImmediateRequestId(info.GetArgument(0)));
            return JsValue.Undefined;
        }, false)
        {
            UserData = this
        };
    }

    private void InvokeImmediate(JsRealm realm, int requestId)
    {
        var state = immediateStates.GetValue(realm, static _ => new());
        ImmediateRequestState? request;
        lock (state.Gate)
        {
            if (!state.Requests.Remove(requestId, out request))
                return;

            request.Active = false;
        }

        _ = realm.Call(request.Callback, JsValue.Undefined, request.Arguments);
    }

    private static int ToImmediateRequestId(in JsValue value)
    {
        return value.IsInt32 ? value.Int32Value : (int)value.NumberValue;
    }

    private static JsHostFunction CreateJoinFunction(JsRealm realm)
    {
        return new(realm, "join", 0, static (in info) =>
        {
            if (info.Arguments.Length == 0)
                return JsValue.FromString(".");

            var parts = new string[info.Arguments.Length];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = info.GetArgumentString(i);
            return JsValue.FromString(Path.Combine(parts));
        }, false);
    }

    private static JsHostFunction CreateDirnameFunction(JsRealm realm)
    {
        return new(realm, "dirname", 1, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            var directory = Path.GetDirectoryName(path);
            return JsValue.FromString(string.IsNullOrEmpty(directory) ? "." : directory);
        }, false);
    }

    private static JsHostFunction CreateNormalizeFunction(JsRealm realm)
    {
        return new(realm, "normalize", 1,
            static (in info) => { return JsValue.FromString(NormalizeNodePath(info.GetArgumentString(0))); }, false);
    }

    private static JsHostFunction CreateBasenameFunction(JsRealm realm)
    {
        return new(realm, "basename", 2, static (in info) =>
        {
            var path = info.GetArgumentString(0);
            var basename = Path.GetFileName(path);
            if (info.Arguments.Length > 1)
            {
                var suffix = info.GetArgumentString(1);
                if (!string.IsNullOrEmpty(suffix) && basename.EndsWith(suffix, StringComparison.Ordinal))
                    basename = basename[..^suffix.Length];
            }

            return JsValue.FromString(basename);
        }, false);
    }

    private static JsHostFunction CreateExtnameFunction(JsRealm realm)
    {
        return new(realm, "extname", 1,
            static (in info) => { return JsValue.FromString(Path.GetExtension(info.GetArgumentString(0))); }, false);
    }

    private static JsHostFunction CreateResolveFunction(JsRealm realm)
    {
        return new(realm, "resolve", 0, static (in info) =>
        {
            var resolved = Environment.CurrentDirectory;
            if (info.Arguments.Length == 0)
                return JsValue.FromString(Path.GetFullPath(resolved));

            for (var i = 0; i < info.Arguments.Length; i++)
            {
                var segment = info.GetArgumentString(i);
                resolved = Path.IsPathRooted(segment)
                    ? segment
                    : Path.Combine(resolved, segment);
            }

            return JsValue.FromString(Path.GetFullPath(resolved));
        }, false);
    }

    private static JsHostFunction CreateRelativeFunction(JsRealm realm)
    {
        return new(realm, "relative", 2, static (in info) =>
        {
            var from = Path.GetFullPath(info.GetArgumentString(0));
            var to = Path.GetFullPath(info.GetArgumentString(1));
            var relative = Path.GetRelativePath(from, to);
            return JsValue.FromString(NormalizeNodePath(relative));
        }, false);
    }

    private static string NormalizeNodePath(string value)
    {
        if (string.IsNullOrEmpty(value))
            return ".";

        var separator = Path.DirectorySeparatorChar;
        var input = value.Replace(Path.AltDirectorySeparatorChar, separator);
        var isAbsolute = Path.IsPathRooted(input);
        var root = isAbsolute ? Path.GetPathRoot(input) ?? string.Empty : string.Empty;
        var remainder = isAbsolute ? input[root.Length..] : input;
        var trailingSeparator = remainder.Length > 0 && remainder[^1] == separator;

        var parts = new List<string>();
        foreach (var segment in remainder.Split(separator))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            if (segment == "..")
            {
                if (parts.Count > 0 && parts[^1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                if (!isAbsolute)
                    parts.Add(segment);

                continue;
            }

            parts.Add(segment);
        }

        var joined = string.Join(separator, parts);
        string result;
        if (isAbsolute)
            result = string.IsNullOrEmpty(joined) ? root : root + joined;
        else
            result = string.IsNullOrEmpty(joined) ? "." : joined;

        if (trailingSeparator && result.Length > 0 && result[^1] != separator)
            result += separator;

        return result;
    }

    private static JsHostFunction CreateBufferFromFunction(JsRealm realm)
    {
        return new(realm, "from", 1, static (in info) =>
        {
            if (info.Arguments.Length == 0)
                return JsValue.FromObject(new JsTypedArrayObject(info.Realm, 0));

            var source = info.Arguments[0];
            if (source.IsString)
            {
                var bytes = Encoding.UTF8.GetBytes(source.AsString());
                return JsValue.FromObject(CreateUint8Array(info.Realm, bytes));
            }

            if (source.TryGetObject(out var sourceObj) && sourceObj is JsArrayBufferObject arrayBuffer)
            {
                var byteOffset = info.Arguments.Length > 1 ? ToNonNegativeUint(info.Realm, info.Arguments[1]) : 0u;
                var available = arrayBuffer.ByteLength;
                if (byteOffset > available)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "Buffer.from offset is out of range");

                var length = info.Arguments.Length > 2
                    ? ToNonNegativeUint(info.Realm, info.Arguments[2])
                    : available - byteOffset;
                if (length > available - byteOffset)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "Buffer.from length is out of range");

                return JsValue.FromObject(new JsTypedArrayObject(
                    info.Realm,
                    arrayBuffer,
                    byteOffset,
                    length,
                    TypedArrayElementKind.Uint8,
                    info.Realm.Uint8ArrayPrototype));
            }

            if (source.TryGetObject(out var typedArraySourceObj) &&
                typedArraySourceObj is JsTypedArrayObject typedArray)
            {
                var bytes = new byte[typedArray.ByteLength];
                for (uint i = 0; i < typedArray.Length; i++)
                    bytes[i] = CoerceByte(info.Realm, typedArray.GetDirectElementValue(i));
                return JsValue.FromObject(CreateUint8Array(info.Realm, bytes));
            }

            if (!info.Realm.TryToObject(source, out sourceObj))
                return JsValue.FromObject(new JsTypedArrayObject(info.Realm, 0));

            var arrayLikeLength = info.Realm.GetArrayLikeLengthLong(sourceObj);
            if (arrayLikeLength < 0)
                arrayLikeLength = 0;
            var arrayLikeBytes = new byte[arrayLikeLength];
            for (var i = 0; i < arrayLikeLength; i++)
                if (sourceObj.TryGetProperty(i.ToString(), out var element))
                    arrayLikeBytes[i] = CoerceByte(info.Realm, element);

            return JsValue.FromObject(CreateUint8Array(info.Realm, arrayLikeBytes));
        }, false);
    }

    private static JsHostFunction CreateBufferAllocFunction(JsRealm realm)
    {
        return new(realm, "alloc", 2, static (in info) =>
        {
            var length = 0;
            if (info.Arguments.Length != 0)
            {
                var requestedLength = info.Realm.ToIntegerOrInfinity(info.Arguments[0]);
                if (!double.IsPositiveInfinity(requestedLength))
                    length = (int)Math.Max(0d, Math.Min(int.MaxValue, requestedLength));
            }

            byte fill = 0;
            if (info.Arguments.Length > 1)
            {
                var fillValue = info.Arguments[1];
                fill = fillValue.IsString && fillValue.AsString().Length != 0
                    ? (byte)fillValue.AsString()[0]
                    : CoerceByte(info.Realm, fillValue);
            }

            var bytes = new byte[length];
            if (fill != 0)
                Array.Fill(bytes, fill);
            return JsValue.FromObject(CreateUint8Array(info.Realm, bytes));
        }, false);
    }

    private static JsHostFunction CreateBufferIsBufferFunction(JsRealm realm)
    {
        return new(realm, "isBuffer", 1, static (in info) =>
        {
            var result = info.Arguments.Length != 0 &&
                         info.Arguments[0].TryGetObject(out var obj) &&
                         obj is JsTypedArrayObject typedArray &&
                         typedArray.Kind == TypedArrayElementKind.Uint8;
            return result ? JsValue.True : JsValue.False;
        }, false);
    }

    private static JsHostFunction CreateBufferByteLengthFunction(JsRealm realm)
    {
        return new(realm, "byteLength", 1, static (in info) =>
        {
            if (info.Arguments.Length == 0)
                return JsValue.FromInt32(0);

            var value = info.Arguments[0];
            if (value.IsString)
                return JsValue.FromInt32(Encoding.UTF8.GetByteCount(value.AsString()));
            if (value.TryGetObject(out var obj) && obj is JsTypedArrayObject typedArray)
                return JsValue.FromInt32((int)typedArray.ByteLength);

            if (info.Realm.TryToObject(value, out obj))
                return JsValue.FromInt32((int)info.Realm.GetArrayLikeLengthLong(obj));

            return JsValue.FromInt32(0);
        }, false);
    }

    private static JsTypedArrayObject CreateUint8Array(JsRealm realm, byte[] bytes)
    {
        var array = new JsTypedArrayObject(realm, (uint)bytes.Length, TypedArrayElementKind.Uint8,
            realm.Uint8ArrayPrototype);
        for (uint i = 0; i < bytes.Length; i++)
            array.TrySetNormalizedElement(i, JsValue.FromInt32(bytes[i]));
        return array;
    }

    private static byte CoerceByte(JsRealm realm, in JsValue value)
    {
        return unchecked((byte)(int)realm.ToIntegerOrInfinity(value));
    }

    private static uint ToNonNegativeUint(JsRealm realm, in JsValue value)
    {
        var number = realm.ToIntegerOrInfinity(value);
        if (double.IsNaN(number) || number <= 0d)
            return 0;
        if (double.IsPositiveInfinity(number) || number > uint.MaxValue)
            return uint.MaxValue;
        return (uint)number;
    }

    private sealed class NextTickInvocation(JsRealm realm, JsFunction callback, JsValue[] args)
    {
        public JsRealm Realm { get; } = realm;
        public JsFunction Callback { get; } = callback;
        public JsValue[] Args { get; } = args;
    }

    private sealed class RealmImmediateState
    {
        public readonly object Gate = new();
        public readonly Dictionary<int, ImmediateRequestState> Requests = [];
        public int NextRequestId;
    }

    private sealed class ImmediateRequestState
    {
        public required RealmImmediateState OwnerState { get; init; }
        public required JsRealm Realm { get; init; }
        public required int PublicRequestId { get; init; }
        public required JsFunction Callback { get; init; }
        public required JsValue[] Arguments { get; init; }
        public bool Active { get; set; } = true;
    }
}
