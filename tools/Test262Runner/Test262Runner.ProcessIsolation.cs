using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private const int ChildExitGraceMilliseconds = 500;

    private enum CandidateOutcome
    {
        Passed,
        Failed,
        Skipped
    }

    private sealed record CandidateExecutionResult(
        CandidateOutcome Outcome,
        string Message,
        RunnerCaseTimings Timings);

    private sealed class WorkerRequest
    {
        public required string Path { get; init; }
    }

    private sealed class WorkerResponse
    {
        public required string Outcome { get; init; }
        public required string Message { get; init; }
        public long ParseTicks { get; init; }
        public long CompileTicks { get; init; }
        public long RunTicks { get; init; }
    }

    private sealed class WorkerChildProcess : IDisposable
    {
        private readonly Test262Options options;
        private readonly string repoRoot;
        private readonly string resolvedRoot;
        private Process? process;
        private Task<string>? stderrTask;

        public WorkerChildProcess(string resolvedRoot, string repoRoot, Test262Options options)
        {
            this.resolvedRoot = resolvedRoot;
            this.repoRoot = repoRoot;
            this.options = options;
        }

        public CandidateExecutionResult ExecuteCandidate(TestFileCandidate candidate)
        {
            EnsureStarted();
            if (process is null)
                return new(CandidateOutcome.Failed, "Failed to start Test262Runner worker process.", default);

            var request = JsonSerializer.Serialize(new WorkerRequest { Path = candidate.Path });
            try
            {
                process.StandardInput.WriteLine(request);
                process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                ResetProcess();
                return new(CandidateOutcome.Failed, $"Test262Runner worker write failed: {ex.Message}", default);
            }

            var timeoutMs = GetEffectiveCaseTimeoutMs(options);
            Task<string?> readTask;
            try
            {
                readTask = process.StandardOutput.ReadLineAsync();
            }
            catch (Exception ex)
            {
                ResetProcess();
                return new(CandidateOutcome.Failed, $"Test262Runner worker read setup failed: {ex.Message}", default);
            }

            if (timeoutMs > 0)
            {
                var completedTask = Task.WhenAny(readTask, Task.Delay(timeoutMs + ChildExitGraceMilliseconds))
                    .GetAwaiter()
                    .GetResult();
                if (!ReferenceEquals(completedTask, readTask))
                {
                    KillProcess();
                    return new(CandidateOutcome.Failed, BuildExternalTimeoutMessage(options, timeoutMs), default);
                }
            }

            string? line;
            try
            {
                line = readTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var detail = BuildWorkerFailureMessage(ex.Message);
                ResetProcess();
                return new(CandidateOutcome.Failed, detail, default);
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                var detail = BuildWorkerFailureMessage();
                ResetProcess();
                return new(CandidateOutcome.Failed, detail, default);
            }

            try
            {
                var response = JsonSerializer.Deserialize<WorkerResponse>(line);
                if (response is null)
                {
                    var detail = BuildWorkerFailureMessage("Worker returned no payload.");
                    ResetProcess();
                    return new(CandidateOutcome.Failed, detail, default);
                }

                return new(
                    ParseCandidateOutcome(response.Outcome),
                    response.Message,
                    new RunnerCaseTimings
                    {
                        ParseTicks = response.ParseTicks,
                        CompileTicks = response.CompileTicks,
                        RunTicks = response.RunTicks
                    });
            }
            catch (JsonException ex)
            {
                var detail = BuildWorkerFailureMessage($"Invalid worker payload: {ex.Message}");
                ResetProcess();
                return new(CandidateOutcome.Failed, detail, default);
            }
        }

        public void Dispose()
        {
            ResetProcess();
        }

        private void EnsureStarted()
        {
            if (process is not null && !process.HasExited)
                return;

            ResetProcess();
            process = Process.Start(CreateWorkerProcessStartInfo(resolvedRoot, repoRoot, options))
                      ?? throw new InvalidOperationException("Failed to start Test262Runner worker process.");
            stderrTask = process.StandardError.ReadToEndAsync();
        }

        private string BuildWorkerFailureMessage(string? message = null)
        {
            var stderr = GetStderrSnapshot();
            var builder = new StringBuilder();
            builder.Append("Test262Runner worker process failed");
            if (process is { HasExited: true })
                builder.Append($" with code {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(message))
                builder.Append($": {message}");
            else if (!string.IsNullOrWhiteSpace(stderr))
                builder.Append($": {ExtractLastNonEmptyLine(stderr)}");
            else
                builder.Append(".");
            return builder.ToString();
        }

        private string GetStderrSnapshot()
        {
            if (stderrTask is null)
                return string.Empty;

            if (!stderrTask.IsCompleted)
                return string.Empty;

            try
            {
                return stderrTask.GetAwaiter().GetResult();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void KillProcess()
        {
            if (process is null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
            }
            finally
            {
                ResetProcess();
            }
        }

        private void ResetProcess()
        {
            if (process is not null)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                }

                try
                {
                    if (!process.HasExited)
                    {
                        if (!process.WaitForExit(100))
                            process.Kill(true);
                        process.WaitForExit();
                    }
                }
                catch
                {
                }

                process.Dispose();
                process = null;
            }

            if (stderrTask is not null)
            {
                try
                {
                    _ = stderrTask.GetAwaiter().GetResult();
                }
                catch
                {
                }

                stderrTask = null;
            }
        }
    }

    private static void RunSingleTestProcess(string resolvedRoot, string repoRoot, Test262Options options)
    {
        var result = ExecuteCandidateByPath(options.SingleTestPath!, resolvedRoot, repoRoot, options);
        WriteWorkerResponse(result);
        Environment.ExitCode = result.Outcome switch
        {
            CandidateOutcome.Passed => 0,
            CandidateOutcome.Skipped => 2,
            _ => 1
        };
    }

    private static void RunWorkerProcess(string resolvedRoot, string repoRoot, Test262Options options)
    {
        var harness = LoadHarness(resolvedRoot);
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            CandidateExecutionResult result;
            if (string.IsNullOrWhiteSpace(line))
            {
                result = new(CandidateOutcome.Failed, "Worker request was empty.", default);
            }
            else
            {
                try
                {
                    var request = JsonSerializer.Deserialize<WorkerRequest>(line);
                    if (request?.Path is null)
                        result = new(CandidateOutcome.Failed, "Worker request payload was invalid.", default);
                    else
                        result = ExecuteCandidateByPath(request.Path, resolvedRoot, repoRoot, options, harness);
                }
                catch (JsonException ex)
                {
                    result = new(CandidateOutcome.Failed, $"Worker request payload was invalid: {ex.Message}", default);
                }
            }

            WriteWorkerResponse(result);
        }
    }

    private static CandidateExecutionResult ExecuteCandidateByPath(
        string candidatePath,
        string resolvedRoot,
        string repoRoot,
        Test262Options options,
        HarnessAssets? harness = null)
    {
        var path = Path.IsPathRooted(candidatePath)
            ? candidatePath
            : Path.GetFullPath(candidatePath, repoRoot);
        if (!File.Exists(path))
            return new(CandidateOutcome.Failed, $"Single test not found: {path}", default);

        var source = File.ReadAllText(path);
        var metadata = Test262Metadata.Parse(source);
        if (ShouldSkip(options, metadata, path, out var reason))
            return new(CandidateOutcome.Skipped, reason, default);

        harness ??= LoadHarness(resolvedRoot);
        return ExecuteCandidateCore(new(path, metadata), harness, repoRoot, options);
    }

    private static void WriteWorkerResponse(CandidateExecutionResult result)
    {
        var payload = new WorkerResponse
        {
            Outcome = result.Outcome switch
            {
                CandidateOutcome.Passed => "passed",
                CandidateOutcome.Skipped => "skipped",
                _ => "failed"
            },
            Message = result.Message,
            ParseTicks = result.Timings.ParseTicks,
            CompileTicks = result.Timings.CompileTicks,
            RunTicks = result.Timings.RunTicks
        };

        Console.WriteLine(JsonSerializer.Serialize(payload));
        Console.Out.Flush();
    }

    private static ProcessStartInfo CreateWorkerProcessStartInfo(
        string resolvedRoot,
        string repoRoot,
        Test262Options options)
    {
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(entryAssemblyPath) || !File.Exists(entryAssemblyPath))
            throw new InvalidOperationException("Unable to resolve Test262Runner entry assembly path.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(entryAssemblyPath);
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(resolvedRoot);
        startInfo.ArgumentList.Add("--worker-mode");
        if (options.TimeoutMs > 0)
        {
            startInfo.ArgumentList.Add("--timeout-ms");
            startInfo.ArgumentList.Add(options.TimeoutMs.ToString());
        }

        if (options.StopOnLongTestSeconds > 0)
        {
            startInfo.ArgumentList.Add("--stop-on-long-test-seconds");
            startInfo.ArgumentList.Add(options.StopOnLongTestSeconds.ToString());
        }

        if (options.UseRealTimers)
            startInfo.ArgumentList.Add("--real-timers");

        if (options.RegExpEngineMode != Test262RegExpEngineMode.Current)
        {
            startInfo.ArgumentList.Add("--regexp-engine");
            startInfo.ArgumentList.Add(options.RegExpEngineMode switch
            {
                Test262RegExpEngineMode.BuiltIn => "built-in",
                Test262RegExpEngineMode.Experimental => "experimental",
                _ => "current"
            });
        }

        return startInfo;
    }

    private static int GetEffectiveCaseTimeoutMs(Test262Options options)
    {
        var effectiveTimeoutMs = options.TimeoutMs;
        if (options.StopOnLongTestSeconds <= 0)
            return effectiveTimeoutMs;

        var longTimeoutMs = checked(options.StopOnLongTestSeconds * 1000);
        if (effectiveTimeoutMs <= 0 || longTimeoutMs < effectiveTimeoutMs)
            effectiveTimeoutMs = longTimeoutMs;
        return effectiveTimeoutMs;
    }

    private static string BuildExternalTimeoutMessage(Test262Options options, int timeoutMs)
    {
        if (options.StopOnLongTestSeconds > 0)
            return $"long-running test exceeded {options.StopOnLongTestSeconds}s";

        return $"Timeout after {timeoutMs} ms";
    }

    private static CandidateOutcome ParseCandidateOutcome(string outcome)
    {
        return outcome switch
        {
            "passed" => CandidateOutcome.Passed,
            "skipped" => CandidateOutcome.Skipped,
            _ => CandidateOutcome.Failed
        };
    }

    private static string? ExtractLastNonEmptyLine(string text)
    {
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(static line => !string.IsNullOrWhiteSpace(line));
    }
}
