using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Okojo.Compiler;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.RegExp;
using Okojo.Runtime;
using Okojo.WebPlatform;
using JsValue = Okojo.JsValue;

internal static partial class Program
{
    private static bool RunCandidatesWithWorkerThreads(
        IReadOnlyList<TestFileCandidate> runnable,
        HarnessAssets harness,
        RunnerProgressState progress,
        Stopwatch runSw,
        string repoRoot,
        Test262Options options,
        Action<string> log,
        ConcurrentBag<string> passed,
        ConcurrentBag<(string Path, string Message)> failed,
        ConcurrentBag<(string Path, string Reason)> skipped)
    {
        if (runnable.Count == 0)
            return false;

        var parallelCandidates = new List<TestFileCandidate>(runnable.Count);
        var exclusiveCandidates = new List<TestFileCandidate>();
        for (var i = 0; i < runnable.Count; i++)
        {
            var candidate = runnable[i];
            var normalizedPath = candidate.Path.Replace('\\', '/');
            if (normalizedPath.Contains("/built-ins/Atomics/wait/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("/built-ins/Atomics/waitAsync/", StringComparison.OrdinalIgnoreCase))
            {
                var candidateSource = File.ReadAllText(candidate.Path);
                if (RequiresExclusiveExecution(candidate.Path, candidateSource))
                {
                    exclusiveCandidates.Add(candidate);
                    continue;
                }
            }

            parallelCandidates.Add(candidate);
        }

        var exclusiveCandidatePaths = exclusiveCandidates.Select(static candidate => candidate.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var workerCount = Math.Max(1, Math.Min(options.Parallelism, Math.Max(1, parallelCandidates.Count)));
        var logGate = new object();
        var nextIndex = -1;
        var stopRequested = 0;
        var stoppedByTotalTimeout = false;
        var workers = new Thread[workerCount];

        void ExecuteCandidate(TestFileCandidate candidate)
        {
            var path = candidate.Path;
            var meta = candidate.Metadata;
            var normalizedPath = NormalizeCachePath(path);
            progress.MarkWorking(normalizedPath);

            if (options.VerboseProgress)
                lock (logGate)
                {
                    log(
                        $"[{progress.Completed + 1}/{progress.SelectedFiles}] {MakeDisplayPath(repoRoot, path, options.FullPath)}");
                }

            try
            {
                var harnessSource = BuildHarnessSource(harness, meta);
                var source = File.ReadAllText(path);
                var isExclusiveCandidate = exclusiveCandidatePaths.Contains(path);
                var isAtomicsCandidate = path.Replace('\\', '/')
                    .Contains("/built-ins/Atomics/", StringComparison.OrdinalIgnoreCase);
                var isLegacySuite = path.Contains($"{Path.DirectorySeparatorChar}suite{Path.DirectorySeparatorChar}",
                                        StringComparison.OrdinalIgnoreCase) ||
                                    path.Contains("/suite/", StringComparison.OrdinalIgnoreCase) ||
                                    path.Contains("\\suite\\", StringComparison.OrdinalIgnoreCase);
                var isModuleCase = IsModuleCase(meta, path);
                var strictModes = isModuleCase
                    ? new[] { true }
                    : meta.Flags.Contains("onlyStrict")
                        ? new[] { true }
                        : meta.Flags.Contains("noStrict")
                            ? new[] { false }
                            : isLegacySuite
                                ? new[] { false }
                                : new[] { false, true };

                var allPassed = true;
                var failReason = string.Empty;
                var fileTimings = default(RunnerCaseTimings);

                void ResetRunState()
                {
                    allPassed = true;
                    failReason = string.Empty;
                    fileTimings = default;
                }

                void RunModes()
                {
                    foreach (var strict in strictModes)
                    {
                        var effectiveTimeoutMs = options.TimeoutMs;
                        if (options.StopOnLongTestSeconds > 0)
                        {
                            var longTimeoutMs = checked(options.StopOnLongTestSeconds * 1000);
                            if (effectiveTimeoutMs <= 0 || longTimeoutMs < effectiveTimeoutMs)
                                effectiveTimeoutMs = longTimeoutMs;
                        }

                        if (!RunCase(source, harnessSource, strict, meta.IsNegative, meta.Flags.Contains("async"),
                                isModuleCase, path,
                                effectiveTimeoutMs, repoRoot, options.FullPath, options, out var message,
                                out var caseTimings))
                        {
                            fileTimings.Add(caseTimings);
                            if (message.Contains("With statements are not supported in Okojo"))
                            {
                                skipped.Add((path, "with statement not supported"));
                                progress.RecordTimings(fileTimings);
                                progress.IncrementCompleted();
                                progress.IncrementExecuted();
                                allPassed = false;
                                failReason = "__SKIPPED__";
                                return;
                            }

                            allPassed = false;
                            failReason = $"{(strict ? "strict" : "sloppy")}: {message}";
                            if (options.StopOnLongTestSeconds > 0 &&
                                message.StartsWith("Timeout after ", StringComparison.Ordinal))
                            {
                                failed.Add((path, $"long-running test exceeded {options.StopOnLongTestSeconds}s"));
                                progress.RecordTimings(fileTimings);
                                progress.IncrementFailed();
                                progress.IncrementExecuted();
                                progress.IncrementCompleted();
                                failReason = "__FAILED_RECORDED__";
                            }

                            return;
                        }

                        fileTimings.Add(caseTimings);
                    }
                }

                RunModes();

                if ((isExclusiveCandidate || isAtomicsCandidate) &&
                    !allPassed &&
                    failReason is not "__SKIPPED__" and not "__FAILED_RECORDED__")
                {
                    ResetRunState();
                    RunModes();
                }

                if (failReason == "__SKIPPED__" || failReason == "__FAILED_RECORDED__")
                    return;

                if (allPassed)
                {
                    passed.Add(path);
                    progress.IncrementPassed();
                }
                else
                {
                    failed.Add((path, failReason));
                    progress.IncrementFailed();
                }

                progress.RecordTimings(fileTimings);
                progress.IncrementExecuted();
                progress.IncrementCompleted();

                if (!options.VerboseProgress &&
                    progress.Completed == progress.SelectedFiles)
                    lock (logGate)
                    {
                        log(
                            $"[{progress.Completed}/{progress.SelectedFiles}] {MakeDisplayPath(repoRoot, path, options.FullPath)}");
                    }
            }
            finally
            {
                progress.MarkDone(normalizedPath);
            }
        }

        void WorkerLoop()
        {
            while (Volatile.Read(ref stopRequested) == 0)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= parallelCandidates.Count)
                    return;

                if (Volatile.Read(ref stopRequested) != 0)
                    return;

                ExecuteCandidate(parallelCandidates[index]);
            }
        }

        void LogProgressSnapshot()
        {
            var working = progress.GetWorkingSnapshot()
                .Where(static item => item.Elapsed >= TimeSpan.FromSeconds(1))
                .Take(workerCount)
                .ToArray();
            var elapsedSeconds = Math.Max(1d, runSw.Elapsed.TotalSeconds);
            var rate = progress.Executed / elapsedSeconds;
            lock (logGate)
            {
                log(
                    $"[progress {runSw.Elapsed:hh\\:mm\\:ss}] completed={progress.Completed}/{progress.SelectedFiles} executed={progress.Executed} passed={progress.Passed} failed={progress.Failed} skipped={progress.Skipped} rate={rate:F2}/s working={working.Length}");
                foreach (var item in working)
                    log(
                        $"  - {MakeDisplayPath(repoRoot, item.Path, options.FullPath)} ({item.Elapsed.TotalSeconds:F1}s)");
            }
        }

        void ManagerLoop()
        {
            var progressInterval = options.ProgressSeconds > 0
                ? TimeSpan.FromSeconds(options.ProgressSeconds)
                : Timeout.InfiniteTimeSpan;
            var effectivePerTestTimeoutMs = options.TimeoutMs;
            if (options.StopOnLongTestSeconds > 0)
            {
                var longTimeoutMs = checked(options.StopOnLongTestSeconds * 1000);
                if (effectivePerTestTimeoutMs <= 0 || longTimeoutMs < effectivePerTestTimeoutMs)
                    effectivePerTestTimeoutMs = longTimeoutMs;
            }

            var nextProgressAt = progressInterval == Timeout.InfiniteTimeSpan
                ? Timeout.InfiniteTimeSpan
                : progressInterval;

            while (true)
            {
                if (!stoppedByTotalTimeout &&
                    options.TimeoutTotalMs > 0 &&
                    runSw.ElapsedMilliseconds > options.TimeoutTotalMs)
                {
                    stoppedByTotalTimeout = true;
                    Interlocked.Exchange(ref stopRequested, 1);
                }

                if (effectivePerTestTimeoutMs > 0)
                {
                    var timedOut = progress.GetWorkingSnapshot()
                        .Where(item => item.Elapsed.TotalMilliseconds > effectivePerTestTimeoutMs)
                        .OrderByDescending(item => item.Elapsed)
                        .ToArray();
                    if (timedOut.Length != 0)
                    {
                        lock (logGate)
                        {
                            log($"Hard timeout: worker exceeded {effectivePerTestTimeoutMs} ms.");
                            foreach (var item in timedOut)
                                log(
                                    $"  - {MakeDisplayPath(repoRoot, item.Path, options.FullPath)} ({item.Elapsed.TotalSeconds:F1}s)");
                        }

                        Environment.Exit(124);
                    }
                }

                if (progressInterval != Timeout.InfiniteTimeSpan &&
                    runSw.Elapsed >= nextProgressAt)
                {
                    LogProgressSnapshot();
                    nextProgressAt += progressInterval;
                }

                var anyAlive = false;
                for (var i = 0; i < workers.Length; i++)
                    if (workers[i].IsAlive)
                    {
                        anyAlive = true;
                        break;
                    }

                if (!anyAlive)
                    return;

                Thread.Sleep(50);
            }
        }

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = new(WorkerLoop)
            {
                IsBackground = true,
                Name = $"Test262RunnerWorker-{i + 1}"
            };
            workers[i].Start();
        }

        var managerThread = new Thread(ManagerLoop)
        {
            IsBackground = true,
            Name = "Test262RunnerManager"
        };
        managerThread.Start();
        managerThread.Join();

        for (var i = 0; i < workers.Length; i++)
            workers[i].Join();

        if (Volatile.Read(ref stopRequested) == 0)
        {
            var logExclusivePhase = options.UseRealTimers;
            if (logExclusivePhase && exclusiveCandidates.Count != 0)
                lock (logGate)
                {
                    log($"[exclusive] running {exclusiveCandidates.Count} timing-sensitive case(s) sequentially");
                }

            for (var i = 0; i < exclusiveCandidates.Count; i++)
            {
                if (Volatile.Read(ref stopRequested) != 0)
                    break;

                if (logExclusivePhase && !options.VerboseProgress)
                    lock (logGate)
                    {
                        log(
                            $"[exclusive {i + 1}/{exclusiveCandidates.Count}] {MakeDisplayPath(repoRoot, exclusiveCandidates[i].Path, options.FullPath)}");
                    }

                ExecuteCandidate(exclusiveCandidates[i]);
            }
        }

        return stoppedByTotalTimeout;
    }

    private static bool RunCase(
        string source,
        HarnessSourceBundle harnessSource,
        bool strict,
        bool negativeExpected,
        bool isAsyncTest,
        bool isModuleCase,
        string sourcePath,
        int timeoutMs,
        string repoRoot,
        bool fullPath,
        Test262Options options,
        out string message,
        out RunnerCaseTimings timings)
    {
        return RunCaseCore(source, harnessSource, strict, negativeExpected, isAsyncTest, isModuleCase, sourcePath,
            timeoutMs, repoRoot,
            fullPath, options, out message, out timings);
    }

    private static bool RunCaseCore(
        string source,
        HarnessSourceBundle harnessSource,
        bool strict,
        bool negativeExpected,
        bool isAsyncTest,
        bool isModuleCase,
        string sourcePath,
        int timeoutMs,
        string repoRoot,
        bool fullPath,
        Test262Options options,
        out string message,
        out RunnerCaseTimings timings)
    {
        timings = default;
        var timeoutSw = timeoutMs > 0 ? Stopwatch.StartNew() : null;
        var useRealTimers = options.UseRealTimers;
        var runnerTime = useRealTimers ? null : new Test262RunnerTimeProvider();

        bool HasTimedOut()
        {
            return timeoutSw is not null && timeoutSw.ElapsedMilliseconds > timeoutMs;
        }

        bool PumpUntilAsyncDone(JsRealm realm, Task<JsValue> doneTask, out string? timeoutMessage)
        {
            return Test262RunnerPump.PumpUntil(
                realm,
                () => doneTask.IsCompleted,
                HasTimedOut,
                timeoutMs,
                runnerTime,
                out timeoutMessage);
        }

        var sourceForScriptPath = source;
        if (!isModuleCase)
        {
            var fullSource = new StringBuilder(harnessSource.Source.Length + source.Length + 32);
            if (strict)
                // Must be first statement in combined source to be a directive prologue.
                fullSource.AppendLine("'use strict';");

            if (!string.IsNullOrEmpty(harnessSource.Source))
                fullSource.AppendLine(harnessSource.Source);
            fullSource.Append(source);
            sourceForScriptPath = fullSource.ToString();
        }

        var entryPath = Path.GetFullPath(sourcePath);
        var useExternalRegExpEngine = options.UseExternalRegExpEngine;
        var engine = isModuleCase
            ? JsRuntime.Create(engineOptions =>
            {
                if (useExternalRegExpEngine)
                    engineOptions.UseRegExpEngine(RegExpEngine.Default);
                engineOptions.UseWorkerGlobals();
                engineOptions.UseWebRuntimeGlobals();
                engineOptions.ConfigureOptions(options =>
                    options.UseSharedWaiterControllerFactory(Test262RunnerSharedWaiterControllerFactory.Shared));
                if (runnerTime is not null)
                    engineOptions.UseTimeProvider(runnerTime);
                engineOptions.UseModuleSourceLoader(new RunnerModuleLoader(entryPath, source));
            })
            : JsRuntime.Create(engineOptions =>
            {
                if (useExternalRegExpEngine)
                    engineOptions.UseRegExpEngine(RegExpEngine.Default);
                engineOptions.UseWorkerGlobals();
                engineOptions.UseWebRuntimeGlobals();
                engineOptions.ConfigureOptions(options =>
                    options.UseSharedWaiterControllerFactory(Test262RunnerSharedWaiterControllerFactory.Shared));

                if (runnerTime is not null)
                    engineOptions.UseTimeProvider(runnerTime);
            });
        using var hostContext = new Test262HostContext(engine.TimeProvider);
        var vm = engine.DefaultRealm;
        InstallOkojoHarnessGlobals(vm, hostContext);
        var asyncDone = new TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (isAsyncTest)
            vm.Global["$DONE"] = JsValue.FromObject(new JsHostFunction(vm, (in info) =>
            {
                var args = info.Arguments;
                var value = args.Length > 0 ? args[0] : JsValue.Undefined;
                asyncDone.TrySetResult(value);
                return JsValue.Undefined;
            }, "$DONE", 1));

        try
        {
            if (isModuleCase)
            {
                if (!string.IsNullOrEmpty(harnessSource.Source))
                {
                    // Module tests should run harness helpers as classic script globals.
                    var harnessParseStart = Stopwatch.GetTimestamp();
                    var harnessProgram = JavaScriptParser.ParseScript(harnessSource.Source);
                    var harnessParseEnd = Stopwatch.GetTimestamp();
                    timings.AddParse(harnessParseStart, harnessParseEnd);

                    var harnessCompileStart = Stopwatch.GetTimestamp();
                    Intrinsics.PrepareGlobalScriptDeclarationInstantiation(vm, harnessProgram);
                    var harnessScript = new JsCompiler(vm).Compile(harnessProgram);
                    var harnessCompileEnd = Stopwatch.GetTimestamp();
                    timings.AddCompile(harnessCompileStart, harnessCompileEnd);

                    var harnessRunStart = Stopwatch.GetTimestamp();
                    vm.Execute(harnessScript);
                    var harnessRunEnd = Stopwatch.GetTimestamp();
                    timings.AddRun(harnessRunStart, harnessRunEnd);
                }

                var moduleRunStart = Stopwatch.GetTimestamp();
                _ = vm.Import(entryPath);
                var moduleRunEnd = Stopwatch.GetTimestamp();
                timings.AddRun(moduleRunStart, moduleRunEnd);
            }
            else
            {
                var parseStart = Stopwatch.GetTimestamp();
                var program = JavaScriptParser.ParseScript(sourceForScriptPath, entryPath);
                var parseEnd = Stopwatch.GetTimestamp();
                timings.AddParse(parseStart, parseEnd);

                var compileStart = Stopwatch.GetTimestamp();
                Intrinsics.PrepareGlobalScriptDeclarationInstantiation(vm, program);
                var script = new JsCompiler(vm).Compile(program);
                var compileEnd = Stopwatch.GetTimestamp();
                timings.AddCompile(compileStart, compileEnd);

                var runStart = Stopwatch.GetTimestamp();
                vm.Execute(script);
                var runActive = true;

                try
                {
                    if (HasTimedOut())
                    {
                        message = $"Timeout after {timeoutMs} ms";
                        return false;
                    }

                    if (isAsyncTest)
                    {
                        if (!PumpUntilAsyncDone(vm, asyncDone.Task, out var timeoutMessage))
                        {
                            message = timeoutMessage!;
                            return false;
                        }

                        if (HasTimedOut())
                        {
                            message = $"Timeout after {timeoutMs} ms";
                            return false;
                        }

                        vm.PumpJobs();
                        var doneValue = asyncDone.Task.Result;
                        if (!doneValue.IsUndefined && !doneValue.IsNull)
                        {
                            message = "JavaScript throw: " + doneValue;
                            return false;
                        }
                    }
                }
                finally
                {
                    if (runActive)
                    {
                        var runEnd = Stopwatch.GetTimestamp();
                        timings.AddRun(runStart, runEnd);
                    }
                }

                if (negativeExpected)
                {
                    message = "Expected failure, but execution succeeded.";
                    return false;
                }

                message = "ok";
                return true;
            }

            if (HasTimedOut())
            {
                message = $"Timeout after {timeoutMs} ms";
                return false;
            }

            if (isAsyncTest)
            {
                var asyncRunStart = Stopwatch.GetTimestamp();
                var asyncTimingActive = true;
                if (!PumpUntilAsyncDone(vm, asyncDone.Task, out var timeoutMessage))
                {
                    message = timeoutMessage!;
                    return false;
                }

                try
                {
                    if (HasTimedOut())
                    {
                        message = $"Timeout after {timeoutMs} ms";
                        return false;
                    }

                    vm.PumpJobs();
                    var doneValue = asyncDone.Task.Result;
                    if (!doneValue.IsUndefined && !doneValue.IsNull)
                    {
                        message = "JavaScript throw: " + doneValue;
                        return false;
                    }
                }
                finally
                {
                    if (asyncTimingActive)
                    {
                        var asyncRunEnd = Stopwatch.GetTimestamp();
                        timings.AddRun(asyncRunStart, asyncRunEnd);
                    }
                }
            }

            if (negativeExpected)
            {
                message = "Expected failure, but execution succeeded.";
                return false;
            }

            message = "ok";
            return true;
        }
        catch (Exception ex)
        {
            if (negativeExpected)
            {
                message = "ok (negative expected)";
                return true;
            }

            if (ex is JsRuntimeException runtimeEx)
            {
                message = FormatRuntimeExceptionMessage(runtimeEx);
                var mappedLocation =
                    SelectRuntimeExceptionLocation(runtimeEx, sourcePath, harnessSource, strict, isModuleCase);
                if (mappedLocation is not null)
                    message += Environment.NewLine +
                               $" at {ToDisplayPath(repoRoot, mappedLocation.Value.Path, fullPath)}:{mappedLocation.Value.Line}:{mappedLocation.Value.Column}";
                if (runtimeEx.Kind == JsErrorKind.InternalError &&
                    TryExtractManagedSourceLocationForRuntimeException(runtimeEx, repoRoot, fullPath,
                        out var managedSourceLocation))
                    message += Environment.NewLine + $" [csharp {managedSourceLocation}]";

                var okojoStack = runtimeEx.FormatOkojoStackTrace();
                if (!string.IsNullOrEmpty(okojoStack))
                    message += Environment.NewLine + okojoStack;
                return false;
            }

            if (ex is JsParseException parseEx)
            {
                var baseMessage = StripParseLocationSuffix(parseEx.Message);
                var mapped = isModuleCase
                    ? (sourcePath.Replace('\\', '/'), parseEx.Line, parseEx.Column)
                    : MapSourceLocation(sourcePath, harnessSource, strict, parseEx.Line, parseEx.Column);
                if (mapped is not null)
                    message =
                        $"JsParseException: {baseMessage} at {ToDisplayPath(repoRoot, mapped.Value.Path, fullPath)}:{mapped.Value.Line}:{mapped.Value.Column} (position {parseEx.Position}).";
                else
                    message =
                        $"JsParseException: {baseMessage} at line {parseEx.Line}, column {parseEx.Column} (position {parseEx.Position}).";
                return false;
            }

            message = ex.GetType().Name + ": " + ex.Message;
            if (TryExtractManagedSourceLocation(ex, repoRoot, fullPath, out var managedLocation))
                message += $" at {managedLocation}";
            return false;
        }
    }

    private static bool RequiresExclusiveExecution(string sourcePath, string source)
    {
        var normalizedPath = sourcePath.Replace('\\', '/');
        var isTimingSensitiveAtomicsWaitPath =
            normalizedPath.Contains("/built-ins/Atomics/wait/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/built-ins/Atomics/waitAsync/", StringComparison.OrdinalIgnoreCase);
        if (!isTimingSensitiveAtomicsWaitPath)
            return false;

        return source.Contains("$262.agent.monotonicNow()", StringComparison.Ordinal) &&
               (source.Contains("Atomics.wait(", StringComparison.Ordinal) ||
                source.Contains("Atomics.waitAsync(", StringComparison.Ordinal));
    }

    private static string FormatRunnerTimingDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1d)
            return $"{duration.TotalMilliseconds:F3}ms";
        if (duration.TotalMilliseconds < 1000d)
            return $"{duration.TotalMilliseconds:F1}ms";
        return $"{duration.TotalSeconds:F3}s";
    }
}
