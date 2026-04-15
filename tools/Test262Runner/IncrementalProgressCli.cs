using Test262Runner;

internal static class IncrementalProgressCli
{
    public static void Run(
        string repoRoot,
        string resolvedRoot,
        string? queryPathArg,
        string? filter,
        IReadOnlyCollection<string> categories,
        IReadOnlyCollection<string> features,
        IReadOnlyCollection<string> statuses,
        string? reasonFilter,
        DateTimeOffset? updatedSince,
        string groupBy,
        string listMode,
        int? top,
        int maxListed,
        bool showSkipped,
        bool fullPath)
    {
        var jsonPath = ResolveIncrementalJsonPath(repoRoot, queryPathArg);
        if (!File.Exists(jsonPath))
        {
            Console.Error.WriteLine(
                $"Incremental progress json not found: {DisplayPath(repoRoot, jsonPath, fullPath)}");
            Environment.Exit(2);
            return;
        }

        var json = File.ReadAllText(jsonPath);
        var entriesSource = IncrementalProgressStoreCodec.LoadEntries(json);
        if (entriesSource is null)
        {
            Console.Error.WriteLine(
                $"Failed to parse incremental progress json: {DisplayPath(repoRoot, jsonPath, fullPath)}");
            Environment.Exit(2);
            return;
        }

        var entries = entriesSource
            .Select(entry => NormalizeEntry(entry, resolvedRoot))
            .Where(static entry => IsTrackedTestEntry(entry.RelativePath))
            .Where(entry => MatchesPathFilter(entry.RelativePath, filter))
            .Where(entry => MatchesCategoryFilter(entry.RelativePath, categories))
            .Where(entry => MatchesFeatureFilter(entry.Features, features))
            .Where(entry => MatchesStatusFilter(entry.Status, statuses))
            .Where(entry => MatchesReasonFilter(entry, reasonFilter))
            .Where(entry => MatchesUpdatedSince(entry.LastUpdated, updatedSince))
            .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine("# Test262 Incremental Query");
        Console.WriteLine();
        Console.WriteLine($"Source: {DisplayPath(repoRoot, jsonPath, fullPath)}");
        Console.WriteLine($"Selected tests: {entries.Length}");
        Console.WriteLine();

        switch (NormalizeGroupBy(groupBy))
        {
            case "all":
                AppendTable(Console.Out, "By Category",
                    ApplyTop(BuildRows(entries, static entry => GetCategoryKey(entry.RelativePath)), top));
                AppendTable(Console.Out, "By Folder",
                    ApplyTop(BuildRows(entries, static entry => GetFolderKey(entry.RelativePath)), top));
                AppendTable(Console.Out, "By Feature", ApplyTop(BuildFeatureRows(entries), top));
                break;
            case "category":
                AppendTable(Console.Out, "By Category",
                    ApplyTop(BuildRows(entries, static entry => GetCategoryKey(entry.RelativePath)), top));
                break;
            case "folder":
                AppendTable(Console.Out, "By Folder",
                    ApplyTop(BuildRows(entries, static entry => GetFolderKey(entry.RelativePath)), top));
                break;
            case "feature":
                AppendTable(Console.Out, "By Feature", ApplyTop(BuildFeatureRows(entries), top));
                break;
            case "none":
                break;
            default:
                Console.Error.WriteLine($"Invalid --group-by value: {groupBy}");
                Environment.Exit(2);
                return;
        }

        var normalizedListMode = NormalizeListMode(listMode, showSkipped);
        if (normalizedListMode is null)
        {
            Console.Error.WriteLine($"Invalid --list value: {listMode}");
            Environment.Exit(2);
            return;
        }

        if (!string.Equals(normalizedListMode, "none", StringComparison.Ordinal))
        {
            if (string.Equals(normalizedListMode, "all", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "failed-skipped", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "failed", StringComparison.Ordinal))
                AppendStatusList(Console.Out, "Failed Tests", entries, "failed", maxListed, resolvedRoot, fullPath);

            if (string.Equals(normalizedListMode, "all", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "failed-skipped", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "skipped", StringComparison.Ordinal))
                AppendStatusList(Console.Out, "Skipped Tests", entries, "skipped", maxListed, resolvedRoot, fullPath);

            if (string.Equals(normalizedListMode, "all", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "passed", StringComparison.Ordinal))
                AppendStatusList(Console.Out, "Passed Tests", entries, "passed", maxListed, resolvedRoot, fullPath);

            if (string.Equals(normalizedListMode, "all", StringComparison.Ordinal) ||
                string.Equals(normalizedListMode, "not-yet", StringComparison.Ordinal))
                AppendStatusList(Console.Out, "Not Yet Tests", entries, "not-yet", maxListed, resolvedRoot, fullPath);
        }

        var skipReasons = entries
            .Where(static entry => string.Equals(entry.Status, "skipped", StringComparison.Ordinal) &&
                                   !string.IsNullOrWhiteSpace(entry.SkipReason))
            .GroupBy(static entry => entry.SkipReason!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new { Reason = group.Key, Count = group.Count() })
            .ToArray();
        Console.WriteLine("## Skip Reasons");
        Console.WriteLine();
        if (skipReasons.Length == 0)
            Console.WriteLine("(none)");
        else
            foreach (var item in skipReasons)
                Console.WriteLine($"- {item.Reason}: {item.Count}");

        Console.WriteLine();
        var failureReasons = entries
            .Where(static entry => string.Equals(entry.Status, "failed", StringComparison.Ordinal) &&
                                   !string.IsNullOrWhiteSpace(entry.FailureReason))
            .GroupBy(static entry => entry.FailureReason!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new { Reason = group.Key, Count = group.Count() })
            .ToArray();
        Console.WriteLine("## Failure Reasons");
        Console.WriteLine();
        if (failureReasons.Length == 0)
            Console.WriteLine("(none)");
        else
            foreach (var item in failureReasons)
                Console.WriteLine($"- {item.Reason}: {item.Count}");
    }

    private static string ResolveIncrementalJsonPath(string repoRoot, string? queryPathArg)
    {
        if (queryPathArg is null || string.IsNullOrWhiteSpace(queryPathArg))
            return Path.Combine(repoRoot, "TEST262_PROGRESS_INCREMENTAL.json");
        return Path.IsPathRooted(queryPathArg) ? queryPathArg : Path.GetFullPath(queryPathArg, repoRoot);
    }

    private static IncrementalProgressEntry NormalizeEntry(IncrementalProgressEntry entry, string resolvedRoot)
    {
        var relativePath = NormalizeRelativePath(entry.RelativePath, resolvedRoot);
        var storePath = NormalizeRelativePath(entry.Path, resolvedRoot);
        return entry with
        {
            Path = storePath,
            RelativePath = string.IsNullOrWhiteSpace(relativePath) ? storePath : relativePath
        };
    }

    private static string NormalizeRelativePath(string path, string resolvedRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        if (Path.IsPathRooted(path))
            return Path.GetRelativePath(resolvedRoot, path).Replace('\\', '/');
        return path.Replace('\\', '/');
    }

    private static bool IsTrackedTestEntry(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        if (relativePath.StartsWith("harness/", StringComparison.OrdinalIgnoreCase))
            return false;
        return relativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPathFilter(string relativePath, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return relativePath.Contains(filter.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCategoryFilter(string relativePath, IReadOnlyCollection<string> categories)
    {
        if (categories.Count == 0)
            return true;
        var key = GetCategoryKey(relativePath);
        return categories.Contains(key);
    }

    private static bool MatchesFeatureFilter(IReadOnlyList<string> entryFeatures, IReadOnlyCollection<string> features)
    {
        if (features.Count == 0)
            return true;
        foreach (var feature in entryFeatures)
            if (features.Contains(feature))
                return true;

        return false;
    }

    private static bool MatchesStatusFilter(string status, IReadOnlyCollection<string> statuses)
    {
        if (statuses.Count == 0)
            return true;
        return statuses.Contains(status);
    }

    private static bool MatchesReasonFilter(IncrementalProgressEntry entry, string? reasonFilter)
    {
        if (string.IsNullOrWhiteSpace(reasonFilter))
            return true;
        return (!string.IsNullOrWhiteSpace(entry.SkipReason) &&
                entry.SkipReason.Contains(reasonFilter, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(entry.FailureReason) &&
                entry.FailureReason.Contains(reasonFilter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesUpdatedSince(DateTimeOffset lastUpdated, DateTimeOffset? updatedSince)
    {
        if (!updatedSince.HasValue)
            return true;
        if (lastUpdated == DateTimeOffset.MinValue)
            return false;
        return lastUpdated >= updatedSince.Value;
    }

    private static string NormalizeGroupBy(string groupBy)
    {
        return string.IsNullOrWhiteSpace(groupBy) ? "all" : groupBy.Trim().ToLowerInvariant();
    }

    private static string? NormalizeListMode(string listMode, bool showSkipped)
    {
        var normalized = string.IsNullOrWhiteSpace(listMode)
            ? showSkipped ? "failed-skipped" : "failed"
            : listMode.Trim().ToLowerInvariant();
        return normalized is "failed" or "skipped" or "passed" or "not-yet" or "all" or "none" or "failed-skipped"
            ? normalized
            : null;
    }

    private static IReadOnlyList<IncrementalRow> BuildRows(
        IReadOnlyList<IncrementalProgressEntry> entries,
        Func<IncrementalProgressEntry, string> keySelector)
    {
        return entries
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var total = group.Count();
                var passed = group.Count(static x => string.Equals(x.Status, "passed", StringComparison.Ordinal));
                var failed = group.Count(static x => string.Equals(x.Status, "failed", StringComparison.Ordinal));
                var skippedStandard = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.Standard));
                var skippedLegacy = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.Legacy));
                var skippedAnnexB = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.AnnexB));
                var skippedProposal = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.Proposal));
                var skippedFinishedProposalNotInBaseline = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.FinishedProposalNotInBaseline));
                var skippedOther = group.Count(static x => IsSkippedWithSpecStatus(x, SkipList.SkipSpecStatus.Other));
                var skipped = skippedStandard + skippedLegacy + skippedAnnexB + skippedProposal + skippedFinishedProposalNotInBaseline + skippedOther;
                var baselineTotal = total - skippedLegacy - skippedAnnexB - skippedProposal - skippedFinishedProposalNotInBaseline - skippedOther;
                var notYet = total - passed - failed - skipped;
                var lastUpdated = group.Select(static x => x.LastUpdated).Max();
                return new IncrementalRow(group.Key, total, passed, failed, skippedStandard, skippedLegacy, skippedAnnexB,
                    skippedProposal, skippedFinishedProposalNotInBaseline, skippedOther, skipped, notYet,
                    baselineTotal,
                    lastUpdated == DateTimeOffset.MinValue ? null : lastUpdated);
            })
            .OrderBy(static x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<IncrementalRow> BuildFeatureRows(IReadOnlyList<IncrementalProgressEntry> entries)
    {
        return entries
            .SelectMany(entry => entry.Features.Count == 0
                ? [new("(none)", entry)]
                : entry.Features.Select(feature => new KeyValuePair<string, IncrementalProgressEntry>(feature, entry)))
            .GroupBy(static x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var total = group.Count();
                var passed = group.Count(static x => string.Equals(x.Value.Status, "passed", StringComparison.Ordinal));
                var failed = group.Count(static x => string.Equals(x.Value.Status, "failed", StringComparison.Ordinal));
                var skippedStandard = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.Standard));
                var skippedLegacy = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.Legacy));
                var skippedAnnexB = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.AnnexB));
                var skippedProposal = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.Proposal));
                var skippedFinishedProposalNotInBaseline = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.FinishedProposalNotInBaseline));
                var skippedOther = group.Count(static x => IsSkippedWithSpecStatus(x.Value, SkipList.SkipSpecStatus.Other));
                var skipped = skippedStandard + skippedLegacy + skippedAnnexB + skippedProposal + skippedFinishedProposalNotInBaseline + skippedOther;
                var baselineTotal = total - skippedLegacy - skippedAnnexB - skippedProposal - skippedFinishedProposalNotInBaseline - skippedOther;
                var notYet = total - passed - failed - skipped;
                var lastUpdated = group.Select(static x => x.Value.LastUpdated).Max();
                return new IncrementalRow(group.Key, total, passed, failed, skippedStandard, skippedLegacy, skippedAnnexB,
                    skippedProposal, skippedFinishedProposalNotInBaseline, skippedOther, skipped, notYet,
                    baselineTotal,
                    lastUpdated == DateTimeOffset.MinValue ? null : lastUpdated);
            })
            .OrderBy(static x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendTable(TextWriter writer, string title, IReadOnlyList<IncrementalRow> rows)
    {
        writer.WriteLine($"## {title}");
        writer.WriteLine();
        writer.WriteLine(
            "| Scope | Last Updated | Total | Passed | Failed | Skip Std | Skip Legacy | Skip Annex B | Skip Proposal | Skip Finished | Skip Other | Skipped | Not Yet | Passed % | Failed % | Skipped % | Not Yet % | Baseline Passed % |");
        writer.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var row in rows)
            writer.WriteLine(
                $"| {EscapeMd(row.Scope)} | {FormatTimestamp(row.LastUpdated)} | {row.Total} | {row.Passed} | {row.Failed} | {row.SkippedStandard} | {row.SkippedLegacy} | {row.SkippedAnnexB} | {row.SkippedProposal} | {row.SkippedFinishedProposalNotInBaseline} | {row.SkippedOther} | {row.Skipped} | {row.NotYet} | {FormatPercent(row.Passed, row.Total)} | {FormatPercent(row.Failed, row.Total)} | {FormatPercent(row.Skipped, row.Total)} | {FormatPercent(row.NotYet, row.Total)} | {FormatPercent(row.Passed, row.BaselineTotal)} |");

        if (rows.Count == 0)
            writer.WriteLine("| (none) | - | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0.0% | 0.0% | 0.0% | 0.0% | 0.0% |");
        writer.WriteLine();
    }

    private static IReadOnlyList<IncrementalRow> ApplyTop(IReadOnlyList<IncrementalRow> rows, int? top)
    {
        if (!top.HasValue || rows.Count <= top.Value)
            return rows;
        return rows.Take(top.Value).ToArray();
    }

    private static void AppendStatusList(
        TextWriter writer,
        string title,
        IReadOnlyList<IncrementalProgressEntry> entries,
        string status,
        int maxListed,
        string resolvedRoot,
        bool fullPath)
    {
        var items = entries
            .Where(entry => string.Equals(entry.Status, status, StringComparison.Ordinal))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        writer.WriteLine($"## {title}");
        writer.WriteLine();
        foreach (var entry in items.Take(maxListed))
            writer.WriteLine(
                $"- {DisplayTestPath(resolvedRoot, entry.RelativePath, fullPath)}{FormatListSuffix(entry, status)}");
        if (items.Length == 0)
            writer.WriteLine("(none)");
        writer.WriteLine();
    }

    private static string FormatListSuffix(IncrementalProgressEntry entry, string status)
    {
        return string.Equals(status, "skipped", StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(entry.SkipReason)
            ? $" | {entry.SkipReason}"
            : string.Equals(status, "failed", StringComparison.Ordinal) &&
              !string.IsNullOrWhiteSpace(entry.FailureReason)
                ? $" | {entry.FailureReason}"
            : string.Empty;
    }

    private static bool IsSkippedWithSpecStatus(IncrementalProgressEntry entry, SkipList.SkipSpecStatus status)
    {
        return string.Equals(entry.Status, "skipped", StringComparison.Ordinal) &&
               string.Equals(entry.SkipSpecStatus, status.ToString(), StringComparison.Ordinal);
    }

    private static string GetCategoryKey(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "(root)" : parts[0];
    }

    private static string GetFolderKey(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "(root)";
        if (parts.Length == 1)
            return parts[0];
        return $"{parts[0]}/{parts[1]}";
    }

    private static string DisplayPath(string repoRoot, string path, bool fullPath)
    {
        var full = Path.GetFullPath(path);
        if (fullPath)
            return full.Replace("\\", "/");
        var rel = Path.GetRelativePath(repoRoot, full);
        return (rel.StartsWith("..", StringComparison.Ordinal) ? full : rel).Replace("\\", "/");
    }

    private static string DisplayTestPath(string resolvedRoot, string relativePath, bool fullPath)
    {
        if (!fullPath)
            return relativePath;
        return Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), resolvedRoot)
            .Replace("\\", "/");
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

    private static string EscapeMd(string text)
    {
        return text.Replace("|", "\\|");
    }

    private sealed record IncrementalRow(
        string Scope,
        int Total,
        int Passed,
        int Failed,
        int SkippedStandard,
        int SkippedLegacy,
        int SkippedAnnexB,
        int SkippedProposal,
        int SkippedFinishedProposalNotInBaseline,
        int SkippedOther,
        int Skipped,
        int NotYet,
        int BaselineTotal,
        DateTimeOffset? LastUpdated);
}
