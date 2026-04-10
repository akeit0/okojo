using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

internal static partial class Program
{
    private static void Main(string[] args)
    {
        var options = Test262Options.Parse(args);
        var resolvedRoot = ResolveRootPath(options.Root);
        var repoRoot = FindRepoRoot(resolvedRoot);
        if (options.QueryIncremental)
        {
            IncrementalProgressCli.Run(
                repoRoot,
                resolvedRoot,
                options.QueryIncrementalPath,
                options.Filter,
                options.Categories,
                options.Features,
                options.QueryStatuses,
                options.QueryReasonFilter,
                options.QueryUpdatedSince,
                options.QueryGroupBy,
                options.QueryList,
                options.QueryTop,
                options.MaxListed,
                options.ShowSkippedInQuery,
                options.FullPath);
            return;
        }

        var outputPath = ResolveOutputPath(options.OutputPath, repoRoot);
        var progressOutput = ResolveProgressOutput(options, repoRoot, resolvedRoot);
        var progressDocPath = progressOutput.DocPath;
        var progressJsonPath = progressOutput.JsonPath;
        var report = new StringBuilder();

        void Log(string line = "")
        {
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        if (!Directory.Exists(resolvedRoot))
        {
            Console.Error.WriteLine($"Root not found: {options.Root}");
            Console.Error.WriteLine($"Resolved path: {resolvedRoot}");
            Environment.Exit(2);
            return;
        }

        var harness = LoadHarness(resolvedRoot);
        var cachePath = GetMetadataCachePath(repoRoot, resolvedRoot);
        var allCandidates = LoadOrBuildMetadataCandidates(
            resolvedRoot,
            repoRoot,
            options,
            cachePath,
            static _ => { });
        var files = allCandidates
            .Where(c => MatchesFilter(c.Path, options.Filter))
            .Where(c => MatchesCategory(c.Path, resolvedRoot, options.Categories))
            .ToArray();

        var passed = new ConcurrentBag<string>();
        var failed = new ConcurrentBag<(string Path, string Message)>();
        var skipped = new ConcurrentBag<(string Path, string Reason)>();
        var runSw = Stopwatch.StartNew();
        var totalFiles = files.Length;
        var passedCachePath = GetPassedCachePath(repoRoot, resolvedRoot);
        var passedCache = options.SkipPassed ? LoadPassedCache(passedCachePath) : new(StringComparer.OrdinalIgnoreCase);

        var runnable = new List<TestFileCandidate>(files.Length);
        var collectedSkipped = 0;
        foreach (var candidate in files)
        {
            var path = candidate.Path;
            var meta = candidate.Metadata;
            if (ShouldSkip(options, meta, path, out var reason))
            {
                skipped.Add((path, reason));
                collectedSkipped++;
                continue;
            }

            if (options.SkipPassed && passedCache.Contains(NormalizeCachePath(path)))
            {
                skipped.Add((path, "already passed cache"));
                collectedSkipped++;
                continue;
            }

            runnable.Add(candidate);
        }

        if (options.MaxTests.HasValue && runnable.Count > options.MaxTests.Value)
            runnable = runnable.Take(options.MaxTests.Value).ToList();

        var runnableFiles = runnable.Count;
        Log("Test262 ES5 run");
        Log($"Root: {MakeDisplayPath(repoRoot, resolvedRoot, options.FullPath)}");
        if (options.UseMetadataCache)
            Log($"Metadata cache: {MakeDisplayPath(repoRoot, cachePath, options.FullPath)}");
        if (options.Categories.Count > 0)
            Log($"Categories: {string.Join(", ", options.Categories)}");
        if (options.Features.Count > 0)
            Log($"Feature include: {string.Join(", ", options.Features)}");
        if (options.ExcludedFeatures.Count > 0)
            Log($"Feature exclude: {string.Join(", ", options.ExcludedFeatures)}");
        if (options.AllowFeatureTests)
            Log("Feature tests: enabled");
        if (options.MaxTests.HasValue)
            Log($"Max tests: {options.MaxTests.Value}");
        if (options.TimeoutMs > 0)
            Log($"Per-test timeout: {options.TimeoutMs} ms");
        if (options.TimeoutTotalMs > 0)
            Log($"Total timeout: {options.TimeoutTotalMs} ms");
        if (options.StopOnLongTestSeconds > 0)
            Log($"Skip long test after: {options.StopOnLongTestSeconds}s");
        Log($"Parallelism: {options.Parallelism}");
        if (options.ProgressSeconds > 0)
            Log($"Progress interval: {options.ProgressSeconds}s");
        if (options.SkipPassed)
            Log($"Passed cache: {MakeDisplayPath(repoRoot, passedCachePath, options.FullPath)}");
        Log($"Collected: {files.Length}");
        Log($"Pre-skipped: {collectedSkipped}");
        Log($"Runnable: {runnableFiles}");
        Log();

        var progress = new RunnerProgressState(totalFiles, runnableFiles, collectedSkipped);
        var stoppedByTotalTimeout = RunCandidatesWithWorkerThreads(
            runnable,
            harness,
            progress,
            runSw,
            repoRoot,
            options,
            Log,
            passed,
            failed,
            skipped);

        if (options.SkipPassed)
            SavePassedCache(passedCachePath, passedCache.Concat(passed.Select(NormalizeCachePath)));

        var processedFiles = progress.Completed + collectedSkipped;
        var executedFiles = progress.Executed;

        Log("Test262 ES5 summary");
        Log($"Processed: {processedFiles}");
        Log($"Executed: {executedFiles}");
        Log($"Passed: {passed.Count}");
        Log($"Failed: {failed.Count}");
        Log($"Skipped: {skipped.Count}");

        if (stoppedByTotalTimeout)
        {
            Log();
            Log($"Stopped by total timeout: {options.TimeoutTotalMs} ms");
        }

        if (files.Length == 0)
        {
            Log();
            Log("No test files found.");
            Log("Hints:");
            Log("- Verify path: --root test262/test");
            Log("- Ensure submodule is initialized: git submodule update --init --recursive");
            Log("- Try without filter or use a different filter.");
        }

        if (failed.Count > 0)
        {
            Log();
            Log("Failed tests:");
            foreach (var item in failed.Take(options.MaxListed))
            {
                var displayPath = MakeDisplayPath(repoRoot, item.Path, options.FullPath);
                if (TryExtractLineColumnFromMessage(item.Message, out var line, out var column))
                    displayPath = $"{displayPath}:{line}:{column}";
                Log($"- {displayPath}");
                Log($"\t{item.Message.Replace("\n", "\n\t")}");
            }
        }

        if (options.ShowNotFailedTests && passed.Count > 0)
        {
            Log();
            Log("Passed tests:");
            foreach (var item in passed.Take(options.MaxListed))
                Log($"- {MakeDisplayPath(repoRoot, item, options.FullPath)}");
        }

        if (options.ShowNotFailedTests && skipped.Count > 0)
        {
            Log();
            Log("Skipped tests:");
            foreach (var item in skipped.Take(options.MaxListed))
            {
                Log($"- {MakeDisplayPath(repoRoot, item.Path, options.FullPath)}");
                Log($"  {item.Reason}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, report.ToString(), new UTF8Encoding(false));

        if (!string.IsNullOrWhiteSpace(progressDocPath))
        {
            var progressSnapshot = BuildProgressSnapshot(
                repoRoot,
                resolvedRoot,
                options,
                progressOutput,
                files,
                passedCache,
                passed,
                failed,
                skipped);

            WriteProgressDoc(
                progressDocPath,
                progressSnapshot);

            if (!string.IsNullOrWhiteSpace(progressJsonPath))
                WriteProgressJson(progressJsonPath, progressSnapshot);

            var incrementalOutput = ResolveIncrementalProgressOutput(repoRoot);
            var incrementalResult = BuildIncrementalProgressSnapshot(
                repoRoot,
                resolvedRoot,
                options,
                allCandidates,
                files,
                runnable,
                passedCache,
                passed,
                failed,
                skipped,
                incrementalOutput.JsonPath!);
            WriteProgressDoc(incrementalOutput.DocPath!, incrementalResult.Snapshot);
            WriteIncrementalProgressJson(incrementalOutput.JsonPath!, incrementalResult.Store);
        }

        Log();
        Log($"Report written: {MakeDisplayPath(repoRoot, outputPath, options.FullPath)}");
        if (!string.IsNullOrWhiteSpace(progressDocPath))
            Log($"Progress doc written: {MakeDisplayPath(repoRoot, progressDocPath, options.FullPath)}");
        if (!string.IsNullOrWhiteSpace(progressJsonPath))
            Log($"Progress json written: {MakeDisplayPath(repoRoot, progressJsonPath, options.FullPath)}");
        if (!string.IsNullOrWhiteSpace(progressDocPath))
        {
            var incrementalOutput = ResolveIncrementalProgressOutput(repoRoot);
            Log(
                $"Incremental progress doc written: {MakeDisplayPath(repoRoot, incrementalOutput.DocPath!, options.FullPath)}");
            Log(
                $"Incremental progress json written: {MakeDisplayPath(repoRoot, incrementalOutput.JsonPath!, options.FullPath)}");
        }

        if (executedFiles > 0)
            Log(
                $"Timing: wall={FormatRunnerTimingDuration(runSw.Elapsed)} parse={FormatRunnerTimingDuration(progress.ParseDuration)} compile={FormatRunnerTimingDuration(progress.CompileDuration)} run={FormatRunnerTimingDuration(progress.RunDuration)} total={FormatRunnerTimingDuration(progress.TotalDuration)} rate={executedFiles / Math.Max(0.001d, runSw.Elapsed.TotalSeconds):F2}/s");
    }
}
