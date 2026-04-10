using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private static ProgressSnapshot BuildProgressSnapshot(
        string repoRoot,
        string resolvedRoot,
        Test262Options options,
        ProgressOutput progressOutput,
        IReadOnlyList<TestFileCandidate> files,
        IReadOnlyCollection<string> passedCache,
        ConcurrentBag<string> passed,
        ConcurrentBag<(string Path, string Message)> failed,
        ConcurrentBag<(string Path, string Reason)> skipped)
    {
        var now = DateTimeOffset.Now;
        var selected = files
            .Select(candidate => new ProgressItem(
                NormalizeCachePath(candidate.Path),
                Path.GetRelativePath(resolvedRoot, candidate.Path).Replace('\\', '/'),
                candidate.Metadata.Features.ToArray()))
            .ToArray();
        var updatedAtByPath =
            selected.ToDictionary(static item => item.Path, _ => now, StringComparer.OrdinalIgnoreCase);

        var selectedSet = selected.Select(static item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var passedSet = passed.Select(NormalizeCachePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (options.SkipPassed)
            foreach (var item in passedCache)
                if (selectedSet.Contains(item))
                    passedSet.Add(item);

        var failedSet = failed.Select(static item => NormalizeCachePath(item.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skipReasonsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, reason) in skipped)
        {
            var normalized = NormalizeCachePath(path);
            if (string.Equals(reason, "already passed cache", StringComparison.OrdinalIgnoreCase))
                continue;
            skipReasonsByPath[normalized] = reason;
        }

        var reasonGroups = skipReasonsByPath.Values
            .GroupBy(static reason => reason, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new ProgressReasonRow(group.Key, group.Count()))
            .ToArray();

        return new(
            now,
            progressOutput.IsFullScope ? "full" : "partial",
            progressOutput.ScopeLabel,
            MakeDisplayPath(repoRoot, resolvedRoot, false),
            selected.Length,
            options.Filter ?? "(none)",
            options.Categories.Count == 0
                ? ["(all)"]
                : options.Categories.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.Features.Count == 0
                ? ["(all)"]
                : options.Features.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.ExcludedFeatures.Count == 0
                ? ["(none)"]
                : options.ExcludedFeatures.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.MaxTests,
            options.SkipPassed,
            BuildProgressRows(selected, static item => GetCategoryKey(item.RelativePath), passedSet, failedSet,
                skipReasonsByPath, updatedAtByPath),
            BuildProgressRows(selected, static item => GetFolderKey(item.RelativePath), passedSet, failedSet,
                skipReasonsByPath, updatedAtByPath),
            BuildFeatureProgressRows(selected, static item => item.Features, passedSet, failedSet, skipReasonsByPath,
                updatedAtByPath),
            reasonGroups.Length == 0 ? [new("(none)", 0)] : reasonGroups);
    }

    private static IncrementalBuildResult BuildIncrementalProgressSnapshot(
        string repoRoot,
        string resolvedRoot,
        Test262Options options,
        IReadOnlyList<TestFileCandidate> allCandidates,
        IReadOnlyList<TestFileCandidate> files,
        IReadOnlyList<TestFileCandidate> runnable,
        IReadOnlyCollection<string> passedCache,
        ConcurrentBag<string> passed,
        ConcurrentBag<(string Path, string Message)> failed,
        ConcurrentBag<(string Path, string Reason)> skipped,
        string incrementalJsonPath)
    {
        var universe = allCandidates
            .Where(candidate => IsProgressTrackedTestPath(candidate.Path, resolvedRoot))
            .Select(candidate => new ProgressItem(
                GetProgressRelativePath(candidate.Path, resolvedRoot),
                GetProgressRelativePath(candidate.Path, resolvedRoot),
                candidate.Metadata.Features.ToArray()))
            .ToArray();

        var entryByPath = LoadIncrementalEntries(incrementalJsonPath, resolvedRoot);
        foreach (var item in universe)
            if (!entryByPath.ContainsKey(item.Path))
                entryByPath[item.Path] = new(item.Path, item.RelativePath, item.Features.ToArray(), "not-yet", null,
                    DateTimeOffset.MinValue);

        var currentStatuses = BuildCurrentStatusMap(files, runnable, skipped, passedCache, passed, failed,
            options.SkipPassed, resolvedRoot);
        var now = DateTimeOffset.Now;
        foreach (var (path, status) in currentStatuses)
        {
            if (!entryByPath.TryGetValue(path, out var existing))
                continue;

            entryByPath[path] = new(
                path,
                existing.RelativePath,
                existing.Features,
                status.Status,
                status.SkipReason,
                now);
        }

        var entries = entryByPath.Values.ToArray();
        var updatedAtByPath = entries.ToDictionary(static x => x.Path, static x => x.LastUpdated,
            StringComparer.OrdinalIgnoreCase);
        var passedSet = entries.Where(static x => string.Equals(x.Status, "passed", StringComparison.Ordinal))
            .Select(static x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedSet = entries.Where(static x => string.Equals(x.Status, "failed", StringComparison.Ordinal))
            .Select(static x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipReasonsByPath = entries
            .Where(static x => string.Equals(x.Status, "skipped", StringComparison.Ordinal) &&
                               !string.IsNullOrWhiteSpace(x.SkipReason))
            .ToDictionary(static x => x.Path, static x => x.SkipReason!, StringComparer.OrdinalIgnoreCase);
        var reasonGroups = skipReasonsByPath.Values
            .GroupBy(static reason => reason, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new ProgressReasonRow(group.Key, group.Count()))
            .ToArray();

        var snapshot = new ProgressSnapshot(
            now,
            "incremental-full",
            BuildProgressScopeLabel(options, repoRoot, resolvedRoot),
            MakeDisplayPath(repoRoot, resolvedRoot, false),
            universe.Length,
            options.Filter ?? "(none)",
            options.Categories.Count == 0
                ? ["(all)"]
                : options.Categories.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.Features.Count == 0
                ? ["(all)"]
                : options.Features.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.ExcludedFeatures.Count == 0
                ? ["(none)"]
                : options.ExcludedFeatures.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            options.MaxTests,
            options.SkipPassed,
            BuildProgressRows(universe, static item => GetCategoryKey(item.RelativePath), passedSet, failedSet,
                skipReasonsByPath, updatedAtByPath),
            BuildProgressRows(universe, static item => GetFolderKey(item.RelativePath), passedSet, failedSet,
                skipReasonsByPath, updatedAtByPath),
            BuildFeatureProgressRows(universe, static item => item.Features, passedSet, failedSet, skipReasonsByPath,
                updatedAtByPath),
            reasonGroups.Length == 0 ? [new("(none)", 0)] : reasonGroups);
        return new(snapshot, new(entries));
    }

    private static Dictionary<string, CurrentProgressStatus> BuildCurrentStatusMap(
        IReadOnlyList<TestFileCandidate> files,
        IReadOnlyList<TestFileCandidate> runnable,
        ConcurrentBag<(string Path, string Reason)> skipped,
        IReadOnlyCollection<string> passedCache,
        ConcurrentBag<string> passed,
        ConcurrentBag<(string Path, string Message)> failed,
        bool skipPassed,
        string resolvedRoot)
    {
        var statuses = new Dictionary<string, CurrentProgressStatus>(StringComparer.OrdinalIgnoreCase);
        var runnableSet = runnable.Select(x => GetProgressRelativePath(x.Path, resolvedRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var passedCacheSet = passedCache
            .Select(path => NormalizeExistingProgressPath(path, resolvedRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in files)
        {
            var path = GetProgressRelativePath(candidate.Path, resolvedRoot);
            if (runnableSet.Contains(path) || (skipPassed && passedCacheSet.Contains(path)))
                statuses[path] = new("not-yet", null);
        }

        foreach (var path in passed)
            statuses[GetProgressRelativePath(path, resolvedRoot)] = new("passed", null);

        foreach (var (path, _) in failed)
            statuses[GetProgressRelativePath(path, resolvedRoot)] = new("failed", null);

        foreach (var (path, reason) in skipped)
        {
            var normalized = GetProgressRelativePath(path, resolvedRoot);
            if (string.Equals(reason, "already passed cache", StringComparison.OrdinalIgnoreCase))
            {
                statuses[normalized] = new("passed", null);
                continue;
            }

            statuses[normalized] = new("skipped", reason);
        }

        return statuses;
    }

    private static Dictionary<string, IncrementalProgressEntry> LoadIncrementalEntries(string incrementalJsonPath,
        string resolvedRoot)
    {
        if (!File.Exists(incrementalJsonPath))
            return new(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(incrementalJsonPath);
        var entries = IncrementalProgressStoreCodec.LoadEntries(json);
        if (entries is null)
            return new(StringComparer.OrdinalIgnoreCase);

        return entries
            .Select(entry =>
            {
                var normalizedPath = NormalizeExistingProgressPath(entry.Path, resolvedRoot);
                var relativePath = string.IsNullOrWhiteSpace(entry.RelativePath)
                    ? normalizedPath
                    : NormalizeExistingProgressPath(entry.RelativePath, resolvedRoot);
                return entry with { Path = normalizedPath, RelativePath = relativePath };
            })
            .ToDictionary(static x => x.Path, static x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static ProgressRow[] BuildProgressRows(
        IReadOnlyList<ProgressItem> items,
        Func<ProgressItem, string> keySelector,
        IReadOnlySet<string> passedSet,
        IReadOnlySet<string> failedSet,
        IReadOnlyDictionary<string, string> skipReasonsByPath,
        IReadOnlyDictionary<string, DateTimeOffset> updatedAtByPath)
    {
        return items.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var total = group.Count();
                var passed = group.Count(item => passedSet.Contains(item.Path));
                var failed = group.Count(item => failedSet.Contains(item.Path));
                var skipped = group.Count(item => skipReasonsByPath.ContainsKey(item.Path));
                var notYet = total - passed - failed - skipped;
                var lastUpdated = group
                    .Select(item => updatedAtByPath.GetValueOrDefault(item.Path, DateTimeOffset.MinValue))
                    .Max();
                return new ProgressRow(group.Key, total, passed, failed, skipped, notYet,
                    lastUpdated == DateTimeOffset.MinValue ? null : lastUpdated);
            })
            .OrderBy(static row => row.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProgressRow[] BuildFeatureProgressRows(
        IReadOnlyList<ProgressItem> items,
        Func<ProgressItem, IReadOnlyList<string>> keysSelector,
        IReadOnlySet<string> passedSet,
        IReadOnlySet<string> failedSet,
        IReadOnlyDictionary<string, string> skipReasonsByPath,
        IReadOnlyDictionary<string, DateTimeOffset> updatedAtByPath)
    {
        return items
            .SelectMany(item => keysSelector(item).Count == 0
                ? [new("(none)", item)]
                : keysSelector(item).Select(key => new KeyValuePair<string, ProgressItem>(key, item)))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var total = group.Count();
                var passed = group.Count(pair => passedSet.Contains(pair.Value.Path));
                var failed = group.Count(pair => failedSet.Contains(pair.Value.Path));
                var skipped = group.Count(pair => skipReasonsByPath.ContainsKey(pair.Value.Path));
                var notYet = total - passed - failed - skipped;
                var lastUpdated = group
                    .Select(pair => updatedAtByPath.GetValueOrDefault(pair.Value.Path, DateTimeOffset.MinValue))
                    .Max();
                return new ProgressRow(group.Key, total, passed, failed, skipped, notYet,
                    lastUpdated == DateTimeOffset.MinValue ? null : lastUpdated);
            })
            .OrderBy(static row => row.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void WriteProgressDoc(string outputPath, ProgressSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test262 Progress");
        builder.AppendLine();
        builder.AppendLine($"Test date: {snapshot.TestDate:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Root: `{snapshot.Root}`");
        if (!string.Equals(snapshot.ScopeKind, "incremental-full", StringComparison.Ordinal))
        {
            builder.AppendLine($"Scope kind: `{snapshot.ScopeKind}`");
            builder.AppendLine($"Scope label: `{snapshot.ScopeLabel}`");
            builder.AppendLine($"Selected files: `{snapshot.SelectedFiles}`");
            builder.AppendLine($"Filter: `{snapshot.Filter}`");
            builder.AppendLine($"Categories: `{string.Join(", ", snapshot.Categories)}`");
            builder.AppendLine($"Features: `{string.Join(", ", snapshot.Features)}`");
            builder.AppendLine($"Excluded features: `{string.Join(", ", snapshot.ExcludedFeatures)}`");
            if (snapshot.MaxTests.HasValue)
                builder.AppendLine($"Max tests: `{snapshot.MaxTests.Value}`");
            if (snapshot.SkipPassedCache)
                builder.AppendLine("Passed cache: `enabled`");
        }

        builder.AppendLine();

        AppendProgressTable(builder, "By Category", snapshot.ByCategory);
        AppendProgressTable(builder, "By Folder", snapshot.ByFolder);
        AppendProgressTable(builder, "By Feature", snapshot.ByFeature);

        builder.AppendLine("## Skip Reasons");
        builder.AppendLine();
        builder.AppendLine("| Reason | Count |");
        builder.AppendLine("| --- | ---: |");
        foreach (var row in snapshot.SkipReasons)
            builder.AppendLine($"| {EscapeMd(row.Reason)} | {row.Count} |");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteProgressJson(string outputPath, ProgressSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));
    }

    private static void WriteIncrementalProgressJson(string outputPath, IncrementalProgressSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = IncrementalProgressStoreCodec.Serialize(snapshot);
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));
    }

    private static void AppendProgressTable(StringBuilder builder, string title, IReadOnlyList<ProgressRow> rows)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine(
            "| Scope | Last Updated | Total | Passed | Failed | Skipped | Not Yet | Passed % | Failed % | Skipped % | Not Yet % |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var row in rows)
            builder.AppendLine(
                $"| {EscapeMd(row.Scope)} | {FormatTimestamp(row.LastUpdated)} | {row.Total} | {row.Passed} | {row.Failed} | {row.Skipped} | {row.NotYet} | {FormatPercent(row.Passed, row.Total)} | {FormatPercent(row.Failed, row.Total)} | {FormatPercent(row.Skipped, row.Total)} | {FormatPercent(row.NotYet, row.Total)} |");

        builder.AppendLine();
    }

    private static string GetCategoryKey(string normalizedPath)
    {
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "(root)" : parts[0];
    }

    private static string GetFolderKey(string normalizedPath)
    {
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "(root)";
        if (parts.Length == 1)
            return parts[0];
        return $"{parts[0]}/{parts[1]}";
    }

    private static string FormatPercent(int value, int total)
    {
        if (total == 0)
            return "0.0%";
        return $"{value * 100d / total:F1}%";
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "-";
    }

    private static string GetProgressRelativePath(string path, string resolvedRoot)
    {
        return Path.GetRelativePath(resolvedRoot, path).Replace('\\', '/');
    }

    private static string NormalizeExistingProgressPath(string path, string resolvedRoot)
    {
        if (Path.IsPathRooted(path))
            return GetProgressRelativePath(path, resolvedRoot);
        return path.Replace('\\', '/');
    }

    private static bool IsProgressTrackedTestPath(string path, string resolvedRoot)
    {
        var relative = Path.GetRelativePath(resolvedRoot, path).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal))
            return false;
        if (relative.StartsWith("harness/", StringComparison.OrdinalIgnoreCase))
            return false;
        return relative.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeMd(string text)
    {
        return text.Replace("|", "\\|");
    }


    private sealed class RunnerProgressState(int totalFiles, int selectedFiles, int skipped)
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> working = new(StringComparer.OrdinalIgnoreCase);
        private long compileTicks;
        private int completed;
        private int executed;
        private int failed;
        private long parseTicks;
        private int passed;
        private long runTicks;
        private int skipped = skipped;

        public int TotalFiles { get; } = totalFiles;
        public int SelectedFiles { get; } = selectedFiles;
        public int Skipped => Volatile.Read(ref skipped);
        public int Completed => Volatile.Read(ref completed);
        public int Executed => Volatile.Read(ref executed);
        public int Passed => Volatile.Read(ref passed);
        public int Failed => Volatile.Read(ref failed);
        public TimeSpan ParseDuration => TimestampDeltaToTimeSpan(Volatile.Read(ref parseTicks));
        public TimeSpan CompileDuration => TimestampDeltaToTimeSpan(Volatile.Read(ref compileTicks));
        public TimeSpan RunDuration => TimestampDeltaToTimeSpan(Volatile.Read(ref runTicks));
        public TimeSpan TotalDuration => ParseDuration + CompileDuration + RunDuration;

        public void MarkWorking(string path)
        {
            working[path] = DateTimeOffset.UtcNow;
        }

        public void MarkDone(string path)
        {
            working.TryRemove(path, out _);
        }

        public void IncrementCompleted()
        {
            Interlocked.Increment(ref completed);
        }

        public void IncrementExecuted()
        {
            Interlocked.Increment(ref executed);
        }

        public void IncrementPassed()
        {
            Interlocked.Increment(ref passed);
        }

        public void IncrementFailed()
        {
            Interlocked.Increment(ref failed);
        }

        public void IncrementSkipped()
        {
            Interlocked.Increment(ref skipped);
        }

        public void RecordTimings(in RunnerCaseTimings timings)
        {
            Interlocked.Add(ref parseTicks, timings.ParseTicks);
            Interlocked.Add(ref compileTicks, timings.CompileTicks);
            Interlocked.Add(ref runTicks, timings.RunTicks);
        }

        public IReadOnlyList<(string Path, TimeSpan Elapsed)> GetWorkingSnapshot()
        {
            var now = DateTimeOffset.UtcNow;
            return working
                .Select(pair => (pair.Key, now - pair.Value))
                .OrderByDescending(static x => x.Item2)
                .Select(static x => (Path: x.Key, Elapsed: x.Item2))
                .ToArray();
        }

        private static TimeSpan TimestampDeltaToTimeSpan(long timestampDelta)
        {
            return Stopwatch.GetElapsedTime(0, timestampDelta);
        }
    }

    private struct RunnerCaseTimings
    {
        public long ParseTicks;
        public long CompileTicks;
        public long RunTicks;

        public TimeSpan ParseDuration => Stopwatch.GetElapsedTime(0, ParseTicks);
        public TimeSpan CompileDuration => Stopwatch.GetElapsedTime(0, CompileTicks);
        public TimeSpan RunDuration => Stopwatch.GetElapsedTime(0, RunTicks);
        public TimeSpan TotalDuration => ParseDuration + CompileDuration + RunDuration;

        public void AddParse(long startTimestamp, long endTimestamp)
        {
            ParseTicks += endTimestamp - startTimestamp;
        }

        public void AddCompile(long startTimestamp, long endTimestamp)
        {
            CompileTicks += endTimestamp - startTimestamp;
        }

        public void AddRun(long startTimestamp, long endTimestamp)
        {
            RunTicks += endTimestamp - startTimestamp;
        }

        public void Add(RunnerCaseTimings other)
        {
            ParseTicks += other.ParseTicks;
            CompileTicks += other.CompileTicks;
            RunTicks += other.RunTicks;
        }
    }

    private sealed record ProgressItem(
        string Path,
        string RelativePath,
        IReadOnlyList<string> Features);

    private sealed record ProgressReasonRow(
        string Reason,
        int Count);

    private sealed record ProgressRow(
        string Scope,
        int Total,
        int Passed,
        int Failed,
        int Skipped,
        int NotYet,
        DateTimeOffset? LastUpdated);

    private sealed record ProgressSnapshot(
        DateTimeOffset TestDate,
        string ScopeKind,
        string ScopeLabel,
        string Root,
        int SelectedFiles,
        string Filter,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Features,
        IReadOnlyList<string> ExcludedFeatures,
        int? MaxTests,
        bool SkipPassedCache,
        IReadOnlyList<ProgressRow> ByCategory,
        IReadOnlyList<ProgressRow> ByFolder,
        IReadOnlyList<ProgressRow> ByFeature,
        IReadOnlyList<ProgressReasonRow> SkipReasons);

    private sealed record ProgressOutput(
        string? DocPath,
        string? JsonPath,
        bool IsFullScope,
        string ScopeLabel);

    private sealed record CurrentProgressStatus(
        string Status,
        string? SkipReason);

    private sealed record IncrementalBuildResult(
        ProgressSnapshot Snapshot,
        IncrementalProgressSnapshot Store);
}
