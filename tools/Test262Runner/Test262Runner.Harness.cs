using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using JsValue = Okojo.JsValue;

internal static partial class Program
{
    private static HarnessAssets LoadHarness(string resolvedRoot)
    {
        var repoRoot = FindRepoRoot(resolvedRoot);
        // Prefer top-level test262/harness over test262/test/harness when names collide
        // (e.g. promiseHelper.js) because the former contains the canonical helper sources.
        var harnessDirs = new[]
        {
            Path.Combine(repoRoot, "test262", "harness"),
            Path.Combine(repoRoot, "test262", "test", "harness")
        };

        var harnessFiles = new Dictionary<string, HarnessFileAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in harnessDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.js", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrEmpty(name)) continue;

                if (!harnessFiles.ContainsKey(name)) harnessFiles[name] = new(file, File.ReadAllText(file));
            }
        }

        var pieces = new[]
            {
                "sta.js",
                "assert.js"
            }
            .Where(harnessFiles.ContainsKey)
            .Select(name => harnessFiles[name].Source)
            .ToArray();

        if (pieces.Length == 0)
            return new(string.Empty, harnessFiles);

        return new(string.Join(Environment.NewLine, pieces) + Environment.NewLine, harnessFiles);
    }

    private static HarnessSourceBundle BuildHarnessSource(HarnessAssets assets, Test262Metadata metadata)
    {
        const string assertThrowsOverrideSource = """
                                                  assert.throws = function (expectedErrorConstructor, func, message) {
                                                    var expectedName, actualName;
                                                    if (typeof func !== "function") {
                                                      throw new Test262Error("assert.throws requires two arguments: the error constructor and a function to run");
                                                    }
                                                    if (message === undefined) {
                                                      message = "";
                                                    } else {
                                                      message += " ";
                                                    }

                                                    try {
                                                      func();
                                                    } catch (thrown) {
                                                      if (typeof thrown !== "object" || thrown === null) {
                                                        throw new Test262Error(message + "Thrown value was not an object!");
                                                      } else if (thrown.constructor !== expectedErrorConstructor) {
                                                        expectedName = expectedErrorConstructor.name;
                                                        actualName = thrown.constructor.name;
                                                        if (expectedName === actualName) {
                                                          throw new Test262Error(message + "Expected a " + expectedName + " but got a different error constructor with the same name");
                                                        }
                                                        throw new Test262Error(message + "Expected a " + expectedName + " but got a " + actualName);
                                                      }
                                                      return;
                                                    }

                                                    throw new Test262Error(message + "Expected a " + expectedErrorConstructor.name + " to be thrown but no exception was thrown at all");
                                                  };
                                                  """;

        static int CountAppendedLines(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : CountLinesWhenAppendedWithAppendLine(text);
        }

        if (metadata.Includes.Count == 0)
        {
            var source = assets.BaseSource + assertThrowsOverrideSource + Environment.NewLine;
            return new(
                source,
                new(),
                CountAppendedLines(source));
        }

        var sb = new StringBuilder(assets.BaseSource.Length + 4096);
        var segments = new List<HarnessSourceSegment>(metadata.Includes.Count + 1);
        var lineCursor = 1;
        var baseLines = CountAppendedLines(assets.BaseSource);
        if (!string.IsNullOrEmpty(assets.BaseSource))
        {
            sb.AppendLine(assets.BaseSource);
            segments.Add(new("<harness-base>", lineCursor, lineCursor + baseLines - 1));
            lineCursor += baseLines;
        }

        foreach (var include in metadata.Includes)
            if (assets.HarnessFiles.TryGetValue(include, out var includeSource))
            {
                sb.AppendLine(includeSource.Source);
                var includeLines = CountAppendedLines(includeSource.Source);
                segments.Add(new(includeSource.Path.Replace('\\', '/'), lineCursor,
                    lineCursor + includeLines - 1));
                lineCursor += includeLines;
            }

        sb.AppendLine(assertThrowsOverrideSource);
        lineCursor += CountAppendedLines(assertThrowsOverrideSource);

        return new(sb.ToString(), segments, lineCursor - 1);
    }

    private static void InstallOkojoHarnessGlobals(JsRealm vm, Test262HostContext hostContext)
    {
        static JsRuntimeException Test262Exception(string message)
        {
            return new(JsErrorKind.InternalError, message, "TEST262_ASSERT");
        }

        var test262Proto = new JsPlainObject(vm);
        var test262Error = new JsHostFunction(vm, (in info) =>
        {
            var innerVm = info.Realm;
            var args = info.Arguments;
            var err = new JsPlainObject(innerVm, false)
            {
                Prototype = test262Proto
            };
            var msg = args.Length > 0 ? args[0].ToString() : string.Empty;
            err.SetProperty("name", JsValue.FromString("Test262Error"));
            err.SetProperty("message", JsValue.FromString(msg));
            return JsValue.FromObject(err);
        }, "Test262Error", 1, true);
        test262Proto.SetProperty("constructor", JsValue.FromObject(test262Error));
        test262Error.SetProperty("prototype", JsValue.FromObject(test262Proto));
        test262Error.SetProperty("thrower", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var message = args.Length > 0 ? args[0].ToString() : string.Empty;
            throw Test262Exception(message);
        }, "thrower", 1)));
        vm.Global["Test262Error"] = JsValue.FromObject(test262Error);

        vm.Global["$ERROR"] = JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var message = args.Length > 0 ? args[0].ToString() : string.Empty;
            throw Test262Exception(message);
        }, "$ERROR", 1));
        vm.Global["$FAIL"] = vm.Global["$ERROR"];
        vm.Global["$PRINT"] =
            JsValue.FromObject(new JsHostFunction(vm, (in info) => { return JsValue.Undefined; }, "$PRINT", 0));
        vm.Global["print"] = vm.Global["$PRINT"];
        vm.Global["test262update"] =
            JsValue.FromObject(new JsHostFunction(vm, (in info) => { return JsValue.Undefined; }, "test262update", 0));
        vm.Global["fnGlobalObject"] = JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var innerVm = info.Realm;
            return innerVm.Global["globalThis"];
        }, "fnGlobalObject", 0));
        vm.Global["$262"] = JsValue.FromObject(CreateTest262HostObject(vm, hostContext));
        vm.Global["NotEarlyError"] = JsValue.FromString("NotEarlyError");
        vm.Global["$DONOTEVALUATE"] = JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "Test262: This statement should not be evaluated.", "TEST262_DONOTEVALUATE");
        }, "$DONOTEVALUATE", 0));

        var assertFn = new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var ok = args.Length > 0 && Test262AssertHelpers.IsTruthy(args[0]);
            if (!ok)
            {
                var message = args.Length > 1 && !args[1].IsUndefined
                    ? args[1].ToString()
                    : "Expected true but got " + (args.Length > 0 ? args[0].ToString() : "undefined");
                throw Test262Exception(message);
            }

            return JsValue.Undefined;
        }, "assert", 2);

        assertFn.SetProperty("sameValue", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var actual = args.Length > 0 ? args[0] : JsValue.Undefined;
            var expected = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (!Test262AssertHelpers.SameValue(actual, expected))
            {
                var prefix = args.Length > 2 && !args[2].IsUndefined ? args[2] + " " : string.Empty;
                throw Test262Exception(prefix + "Expected SameValue(«" + FormatAssertValue(actual) + "», «" +
                                       FormatAssertValue(expected) + "») to be true");
            }

            return JsValue.Undefined;
        }, "sameValue", 3)));
        assertFn.SetProperty("notSameValue", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var actual = args.Length > 0 ? args[0] : JsValue.Undefined;
            var unexpected = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (Test262AssertHelpers.SameValue(actual, unexpected))
            {
                var prefix = args.Length > 2 && !args[2].IsUndefined ? args[2] + " " : string.Empty;
                throw Test262Exception(prefix + "Expected SameValue(«" + FormatAssertValue(actual) + "», «" +
                                       FormatAssertValue(unexpected) + "») to be false");
            }

            return JsValue.Undefined;
        }, "notSameValue", 3)));
        assertFn.SetProperty("throws", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var innerVm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[1].TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
                throw Test262Exception("assert.throws requires constructor and function");
            var expectedCtor = args[0];
            if (!expectedCtor.TryGetObject(out var expectedCtorObj))
                throw Test262Exception("assert.throws requires an error constructor object as first argument");

            try
            {
                _ = innerVm.Call(fn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
            }
            catch (JsRuntimeException ex)
            {
                var thrown = ex.ThrownValue ?? JsValue.Undefined;
                if (!thrown.TryGetObject(out var thrownObj))
                {
                    var expectedByKindName = ex.Kind switch
                    {
                        JsErrorKind.TypeError => "TypeError",
                        JsErrorKind.ReferenceError => "ReferenceError",
                        JsErrorKind.RangeError => "RangeError",
                        JsErrorKind.SyntaxError => "SyntaxError",
                        _ => "Error"
                    };
                    var expectedName = expectedCtorObj is JsFunction expectedCtorFn
                        ? expectedCtorFn.Name ?? "<anonymous>"
                        : "<non-function>";
                    if (!string.Equals(expectedName, expectedByKindName, StringComparison.Ordinal))
                        throw Test262Exception($"Expected a {expectedName} but got a {expectedByKindName}");
                    return JsValue.Undefined;
                }

                JsObject? actualCtorObj = null;
                var sameCtor = thrownObj.TryGetProperty("constructor", out var ctorValue) &&
                               ctorValue.TryGetObject(out actualCtorObj) &&
                               ReferenceEquals(actualCtorObj, expectedCtorObj);
                if (!sameCtor)
                {
                    var expectedName = expectedCtorObj is JsFunction expectedCtorFn
                        ? expectedCtorFn.Name ?? "<anonymous>"
                        : "<non-function>";
                    var actualName = actualCtorObj is JsFunction actualCtorFn
                        ? actualCtorFn.Name ?? "<anonymous>"
                        : "<non-function>";
                    throw Test262Exception($"Expected a {expectedName} but got a {actualName}");
                }

                return JsValue.Undefined;
            }

            throw Test262Exception("Expected throw");
        }, "throws", 1)));
        assertFn.SetProperty("compareArray", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var actual = args.Length > 0 ? args[0] : JsValue.Undefined;
            var expected = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (!Test262AssertHelpers.CompareArrayLikeValues(actual, expected))
            {
                var message = args.Length > 2 && !args[2].IsUndefined
                    ? args[2].ToString()
                    : "Actual and expected array-likes should have the same contents";
                throw Test262Exception(message);
            }

            return JsValue.Undefined;
        }, "compareArray", 3)));

        vm.Global["assert"] = JsValue.FromObject(assertFn);
        vm.Global["compareArray"] = JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var actual = args.Length > 0 ? args[0] : JsValue.Undefined;
            var expected = args.Length > 1 ? args[1] : JsValue.Undefined;
            return Test262AssertHelpers.CompareArrayLikeValues(actual, expected) ? JsValue.True : JsValue.False;
        }, "compareArray", 2));
    }

    private static JsPlainObject CreateTest262HostObject(JsRealm vm, Test262HostContext hostContext)
    {
        var test262Obj = new JsPlainObject(vm);
        test262Obj.SetProperty("evalScript", JsValue.FromObject(CreateEvalScriptFunction(vm)));
        test262Obj.SetProperty("detachArrayBuffer", JsValue.FromObject(CreateDetachArrayBufferFunction(vm)));
        test262Obj.SetProperty("collectWeakTarget", JsValue.FromObject(CreateCollectWeakTargetFunction(vm)));
        test262Obj.SetProperty("createRealm", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var childRealm = vm.Agent.CreateRealm();
            InstallOkojoHarnessGlobals(childRealm, hostContext);

            var realmObject = new JsPlainObject(childRealm);
            realmObject.SetProperty("global", childRealm.Global["globalThis"]);
            realmObject.SetProperty("evalScript", JsValue.FromObject(CreateEvalScriptFunction(childRealm)));
            realmObject.SetProperty("detachArrayBuffer",
                JsValue.FromObject(CreateDetachArrayBufferFunction(childRealm)));
            realmObject.SetProperty("collectWeakTarget",
                JsValue.FromObject(CreateCollectWeakTargetFunction(childRealm)));
            return JsValue.FromObject(realmObject);
        }, "createRealm", 0)));
        test262Obj.SetProperty("agent", JsValue.FromObject(CreateTest262AgentHostObject(vm, hostContext)));
        return test262Obj;
    }

    private static JsPlainObject CreateTest262AgentHostObject(JsRealm vm, Test262HostContext hostContext)
    {
        var agentObj = new JsPlainObject(vm);
        agentObj.SetProperty("start", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            hostContext.ThrowIfFaulted();

            var args = info.Arguments;
            var source = args.Length > 0 ? args[0].ToString() : string.Empty;
            hostContext.StartWorker(info.Realm, source);
            return JsValue.Undefined;
        }, "start", 1)));
        agentObj.SetProperty("broadcast", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            hostContext.ThrowIfFaulted();

            var args = info.Arguments;
            var payload = args.Length > 0 ? args[0] : JsValue.Undefined;
            hostContext.Broadcast(info.Realm, payload);
            return JsValue.Undefined;
        }, "broadcast", 1)));
        agentObj.SetProperty("getReport", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            hostContext.ThrowIfFaulted();
            info.Realm.PumpJobs();
            return hostContext.TryDequeueReport(out var report) ? report : JsValue.Null;
        }, "getReport", 0)));
        agentObj.SetProperty("sleep", JsValue.FromObject(new JsHostFunction(vm, (in info) =>
        {
            var args = info.Arguments;
            var delay = args.Length > 0 ? args[0] : JsValue.Undefined;
            var milliseconds = delay.IsUndefined
                ? 0d
                : delay.IsInt32
                    ? delay.Int32Value
                    : delay.IsFloat64
                        ? delay.Float64Value
                        : delay.IsTrue
                            ? 1d
                            : 0d;
            hostContext.SleepMilliseconds(milliseconds);
            return JsValue.Undefined;
        }, "sleep", 1)));
        agentObj.SetProperty("monotonicNow",
            JsValue.FromObject(new JsHostFunction(vm, (in _) => { return new(hostContext.MonotonicNowMilliseconds); },
                "monotonicNow", 0)));
        return agentObj;
    }

    private static JsHostFunction CreateEvalScriptFunction(JsRealm vm)
    {
        return new(vm, (in info) =>
        {
            var innerVm = info.Realm;
            var args = info.Arguments;
            var source = args.Length > 0 ? args[0].ToString() : string.Empty;
            try
            {
                var program = JavaScriptParser.ParseScript(source);
                return innerVm.ExecuteProgramInline(program);
            }
            catch (JsParseException ex)
            {
                throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "TEST262_EVALSCRIPT_PARSE");
            }
        }, "evalScript", 1);
    }

    private static JsHostFunction CreateDetachArrayBufferFunction(JsRealm vm)
    {
        return new(vm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachArrayBuffer requires an ArrayBuffer",
                    "TEST262_DETACHARRAYBUFFER");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachArrayBuffer", 1);
    }

    private static JsHostFunction CreateCollectWeakTargetFunction(JsRealm vm)
    {
        return new(vm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || (!args[0].TryGetObject(out _) && !args[0].IsSymbol))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "collectWeakTarget requires an object or symbol",
                    "TEST262_COLLECTWEAKTARGET");

            return vm.Agent.NotifyWeakTargetCollected(args[0])
                ? JsValue.True
                : JsValue.False;
        }, "collectWeakTarget", 1);
    }


    private sealed class RunnerModuleLoader(string entryPath, string entrySource) : IModuleSourceLoader
    {
        private readonly string entryPath = Path.GetFullPath(entryPath);

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (Path.IsPathRooted(specifier))
                return Path.GetFullPath(specifier);

            var baseDir = referrer is null
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(referrer) ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(baseDir, specifier));
        }

        public string LoadSource(string resolvedId)
        {
            var full = Path.GetFullPath(resolvedId);
            if (string.Equals(full, entryPath, StringComparison.OrdinalIgnoreCase))
                return entrySource;
            return File.ReadAllText(full);
        }
    }


    private sealed record HarnessAssets(
        string BaseSource,
        Dictionary<string, HarnessFileAsset> HarnessFiles);

    private sealed record HarnessFileAsset(
        string Path,
        string Source);

    private sealed record HarnessSourceSegment(
        string DisplayPath,
        int StartLine,
        int EndLine);

    private sealed record HarnessSourceBundle(
        string Source,
        List<HarnessSourceSegment> Segments,
        int TotalLines);

    private sealed class Test262HostContext(TimeProvider timeProvider) : IDisposable
    {
        private const string WorkerLeaveMessage = "!";
        private readonly object faultGate = new();
        private readonly long monotonicStartTimestamp = timeProvider.GetTimestamp();
        private readonly ConcurrentQueue<JsValue> reports = new();
        private readonly ConcurrentDictionary<int, Test262WorkerHost> workers = new();
        private volatile bool disposed;
        private Exception? fault;

        public double MonotonicNowMilliseconds =>
            timeProvider.GetElapsedTime(monotonicStartTimestamp).TotalMilliseconds;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            foreach (var worker in workers.Values)
                worker.Dispose();
            workers.Clear();
        }

        public void SleepMilliseconds(double milliseconds)
        {
            ThrowIfFaulted();
            if (double.IsNaN(milliseconds) || milliseconds <= 0d)
                return;

            var due = milliseconds >= int.MaxValue
                ? TimeSpan.FromMilliseconds(int.MaxValue)
                : TimeSpan.FromMilliseconds(milliseconds);

            if (timeProvider is FakeTimeProvider fakeTime)
            {
                fakeTime.Advance(due);
                Thread.Yield();
                return;
            }

            if (timeProvider is Test262RunnerTimeProvider runnerTime)
            {
                runnerTime.AdvanceForSleep(due);
                PumpWorkersForFakeTime();
                return;
            }

            Thread.Sleep(due);
        }

        private void PumpWorkersForFakeTime()
        {
            var liveWorkers = workers.Values.ToArray();
            for (var pass = 0; pass < 4; pass++)
            {
                for (var i = 0; i < liveWorkers.Length; i++)
                    liveWorkers[i].PumpOnce();
                Thread.Yield();
            }
        }

        public void StartWorker(JsRealm mainRealm, string source)
        {
            ThrowIfFaulted();
            ThrowIfDisposed();

            var knownAgentIds = mainRealm.Engine.Agents.Select(static a => a.Id).ToHashSet();
            var createWorker = RequireFunction(mainRealm, "createWorker");
            var handleValue = mainRealm.Call(createWorker, JsValue.Undefined);
            if (!handleValue.TryGetObject(out var handleObj) || handleObj is not JsPlainObject handle)
                throw new JsRuntimeException(JsErrorKind.TypeError, "createWorker did not return a worker handle",
                    "TEST262_AGENT_START");

            var workerAgent = mainRealm.Engine.Agents.FirstOrDefault(agent => !knownAgentIds.Contains(agent.Id));
            if (workerAgent is null)
                throw new JsRuntimeException(JsErrorKind.InternalError, "Failed to resolve created worker agent",
                    "TEST262_AGENT_START");

            var worker = new Test262WorkerHost(this, mainRealm, workerAgent, handle);
            workers[worker.Id] = worker;
            worker.Start(source);
        }

        public void Broadcast(JsRealm mainRealm, JsValue payload)
        {
            ThrowIfFaulted();
            ThrowIfDisposed();

            var liveWorkers = workers.Values.ToArray();
            for (var i = 0; i < liveWorkers.Length; i++)
                liveWorkers[i].Broadcast(mainRealm, payload);
        }

        public bool TryDequeueReport(out JsValue value)
        {
            ThrowIfFaulted();
            return reports.TryDequeue(out value);
        }

        public void EnqueueReport(JsValue value)
        {
            reports.Enqueue(value);
        }

        public void CompleteWorker(int workerId)
        {
            workers.TryRemove(workerId, out _);
        }

        public void FaultWorker(Exception ex)
        {
            lock (faultGate)
            {
                fault ??= ex;
            }
        }

        public void ThrowIfFaulted()
        {
            Exception? captured;
            lock (faultGate)
            {
                captured = fault;
            }

            if (captured is null)
                return;

            throw new JsRuntimeException(
                JsErrorKind.InternalError,
                $"Test262 worker failure: {captured.GetType().Name}: {captured.Message}",
                "TEST262_AGENT_WORKER",
                innerException: captured);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Test262HostContext));
        }

        private static JsFunction RequireFunction(JsRealm realm, string globalName)
        {
            if (!realm.Global.TryGetValue(globalName, out var value) ||
                !value.TryGetObject(out var fnObj) ||
                fnObj is not JsFunction fn)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"Required host function '{globalName}' is not callable",
                    "TEST262_AGENT_HOST_FUNCTION");

            return fn;
        }
    }

    private sealed class Test262WorkerHost : IDisposable
    {
        private const string WorkerLeaveMessage = "!";
        private readonly Test262HostContext context;
        private readonly JsPlainObject handle;
        private readonly object lifecycleGate = new();
        private readonly JsRealm mainRealm;
        private readonly JsFunction postMessageFn;
        private readonly JsAgent workerAgent;
        private readonly JsRealm workerRealm;
        private volatile bool disposed;
        private volatile bool leaving;
        private Thread? workerThread;

        public Test262WorkerHost(Test262HostContext context, JsRealm mainRealm, JsAgent workerAgent,
            JsPlainObject handle)
        {
            this.context = context;
            this.mainRealm = mainRealm;
            this.workerAgent = workerAgent;
            workerRealm = workerAgent.MainRealm;
            this.handle = handle;
            Id = workerAgent.Id;
            postMessageFn = RequireHandleFunction("postMessage");

            handle.SetProperty("onmessage", JsValue.FromObject(new JsHostFunction(mainRealm, (in info) =>
            {
                var args = info.Arguments;
                if (args.Length == 0 || !args[0].TryGetObject(out var evtObj))
                    return JsValue.Undefined;

                if (!evtObj.TryGetProperty("data", out var dataValue) || !dataValue.IsString)
                    return JsValue.Undefined;

                DecodeMessage(dataValue.AsString());
                return JsValue.Undefined;
            }, "onmessage", 1)));
        }

        public int Id { get; }

        public void Dispose()
        {
            Thread? threadToJoin;
            lock (lifecycleGate)
            {
                if (disposed)
                    return;
                disposed = true;
                threadToJoin = workerThread;
                workerThread = null;
            }

            try
            {
                workerAgent.Terminate();
            }
            catch
            {
            }

            if (threadToJoin is not null && !ReferenceEquals(threadToJoin, Thread.CurrentThread))
                try
                {
                    threadToJoin.Join(1000);
                }
                catch
                {
                }
        }

        public void Start(string source)
        {
            workerRealm.Global["__test262SleepHost"] = JsValue.FromObject(new JsHostFunction(workerRealm,
                (in info) =>
                {
                    var args = info.Arguments;
                    var milliseconds = args.Length == 0
                        ? 0d
                        : args[0].IsInt32
                            ? args[0].Int32Value
                            : args[0].IsFloat64
                                ? args[0].Float64Value
                                : args[0].IsTrue
                                    ? 1d
                                    : 0d;
                    context.SleepMilliseconds(milliseconds);
                    return JsValue.Undefined;
                }, "__test262SleepHost", 1));
            workerRealm.Global["__test262MonotonicNowHost"] = JsValue.FromObject(new JsHostFunction(workerRealm,
                (in _) => new(context.MonotonicNowMilliseconds),
                "__test262MonotonicNowHost", 0));
            workerRealm.Eval(BuildWorkerBootstrapSource() + source);
            workerThread = new(Run)
            {
                IsBackground = true,
                Name = $"Test262AgentWorker-{Id}"
            };
            workerThread.Start();
        }

        public void Broadcast(JsRealm realm, JsValue payload)
        {
            if (leaving || disposed)
                return;
            realm.Call(postMessageFn, JsValue.FromObject(handle), payload);
        }

        public void PumpOnce()
        {
            if (disposed || leaving)
                return;

            workerRealm.PumpJobs();
        }

        private void Run()
        {
            try
            {
                var runnerTime = workerRealm.Engine.TimeProvider as Test262RunnerTimeProvider;
                Test262RunnerPump.RunWorkerLoop(workerRealm, () => leaving || disposed, runnerTime);
            }
            catch (Exception ex)
            {
                context.FaultWorker(ex);
            }
            finally
            {
                leaving = true;
                try
                {
                    workerAgent.Terminate();
                }
                catch
                {
                }

                context.CompleteWorker(Id);
            }
        }

        private void DecodeMessage(string encoded)
        {
            if (encoded == WorkerLeaveMessage)
            {
                leaving = true;
                return;
            }

            if (encoded.Length == 0)
            {
                context.EnqueueReport(JsValue.FromString(string.Empty));
                return;
            }

            var tag = encoded[0];
            var payload = encoded.Length > 1 ? encoded[1..] : string.Empty;
            if (tag == 'e')
            {
                context.FaultWorker(new InvalidOperationException(payload));
                return;
            }

            context.EnqueueReport(tag switch
            {
                's' => JsValue.FromString(payload),
                'n' => new(double.Parse(payload, CultureInfo.InvariantCulture)),
                'i' => JsValue.FromBigInt(new(BigInteger.Parse(payload, CultureInfo.InvariantCulture))),
                'b' => payload == "1" ? JsValue.True : JsValue.False,
                'u' => JsValue.Undefined,
                'l' => JsValue.Null,
                _ => JsValue.FromString(encoded)
            });
        }

        private JsFunction RequireHandleFunction(string name)
        {
            if (!handle.TryGetProperty(name, out var value) ||
                !value.TryGetObject(out var fnObj) ||
                fnObj is not JsFunction fn)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"Worker handle member '{name}' is not callable",
                    "TEST262_AGENT_HANDLE");

            return fn;
        }

        private static string BuildWorkerBootstrapSource()
        {
            return """
                   globalThis.$262 ??= {};
                   const __test262Sleep = (ms) => {
                     if (typeof __test262SleepHost === "function") {
                       __test262SleepHost(ms);
                       return;
                     }
                     const i32 = new Int32Array(new SharedArrayBuffer(4));
                     Atomics.wait(i32, 0, 0, ms);
                   };
                   $262.agent = {
                     receiveBroadcast(callback) {
                       globalThis.onmessage = function(evt) {
                         try {
                           return callback(evt.data);
                         } catch (error) {
                           postMessage("e" + String(error && error.stack ? error.stack : error));
                           throw error;
                         }
                       };
                     },
                     report(value) {
                       postMessage("s" + String(value));
                     },
                     leaving() {
                       postMessage("!");
                     },
                     sleep(ms) {
                       __test262Sleep(ms);
                     },
                     monotonicNow() {
                       if (typeof __test262MonotonicNowHost === "function")
                         return __test262MonotonicNowHost();
                       return Date.now();
                     }
                   };
                   """;
        }
    }
}
