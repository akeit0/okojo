using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Test262Runner;
using JsValue = Okojo.JsValue;

internal static partial class Program
{
    private static string FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Okojo.slnx"))) return dir.FullName.Replace("\\", "/");

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory().Replace("\\", "/");
        ;
    }

    private static string FormatAssertValue(JsValue value)
    {
        if (value.IsUndefined)
            return "undefined";
        if (value.IsNull)
            return "null";
        if (value.IsBool)
            return value.IsTrue ? "true" : "false";
        if (value.IsString)
            return value.AsString();
        return value.ToString();
    }

    private static string ResolveRootPath(string rootArg)
    {
        if (Path.IsPathRooted(rootArg)) return rootArg;

        var fromCwd = Path.GetFullPath(rootArg, Directory.GetCurrentDirectory());
        if (Directory.Exists(fromCwd)) return fromCwd;

        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        return Path.GetFullPath(rootArg, repoRoot);
    }

    private static string ResolveOutputPath(string? outputPathArg, string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(outputPathArg))
            return Path.Combine(repoRoot, "artifacts", "test262", "test262-last.txt");

        if (Path.IsPathRooted(outputPathArg)) return outputPathArg;

        return Path.GetFullPath(outputPathArg, repoRoot);
    }

    private static ProgressOutput ResolveProgressOutput(Test262Options options, string repoRoot, string resolvedRoot)
    {
        if (options.ProgressDocPath is null && options.ProgressJsonPath is null)
            return new(null, null, false, "(disabled)");

        var isFullScope = IsFullProgressScope(options, repoRoot, resolvedRoot);
        var scopeLabel = BuildProgressScopeLabel(options, repoRoot, resolvedRoot);

        return new(
            ResolveProgressPath(options.ProgressDocPath, repoRoot, resolvedRoot, isFullScope, scopeLabel, ".md"),
            ResolveProgressPath(options.ProgressJsonPath, repoRoot, resolvedRoot, isFullScope, scopeLabel, ".json"),
            isFullScope,
            scopeLabel);
    }

    private static ProgressOutput ResolveIncrementalProgressOutput(string repoRoot)
    {
        return new(
            Path.Combine(repoRoot, "TEST262_PROGRESS_INCREMENTAL.md"),
            Path.Combine(repoRoot, "TEST262_PROGRESS_INCREMENTAL.json"),
            false,
            "incremental-full");
    }

    private static string? ResolveProgressPath(
        string? pathArg,
        string repoRoot,
        string resolvedRoot,
        bool isFullScope,
        string scopeLabel,
        string extension)
    {
        if (pathArg is null)
            return null;

        if (!string.IsNullOrWhiteSpace(pathArg))
            return Path.IsPathRooted(pathArg) ? pathArg : Path.GetFullPath(pathArg, repoRoot);

        if (isFullScope)
            return Path.Combine(repoRoot, $"TEST262_PROGRESS{extension}");

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var historyDir = Path.Combine(repoRoot, "TEST262_PROGRESS_HISTORY");
        var rootLabel = Path.GetRelativePath(repoRoot, resolvedRoot).Replace('\\', '/');
        var safeRootLabel = SlugifyProgressPart(rootLabel);
        var safeScopeLabel = SlugifyProgressPart(scopeLabel);
        return Path.Combine(historyDir, $"{timestamp}--{safeRootLabel}--{safeScopeLabel}{extension}");
    }

    private static bool IsFullProgressScope(Test262Options options, string repoRoot, string resolvedRoot)
    {
        if (!string.IsNullOrWhiteSpace(options.Filter))
            return false;
        if (options.Categories.Count != 0)
            return false;
        if (options.Features.Count != 0)
            return false;
        if (options.MaxTests.HasValue)
            return false;

        var defaultRoot = Path.GetFullPath(Path.Combine(repoRoot, "test262", "test"));
        return string.Equals(resolvedRoot, defaultRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProgressScopeLabel(Test262Options options, string repoRoot, string resolvedRoot)
    {
        if (IsFullProgressScope(options, repoRoot, resolvedRoot))
            return "full";

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(options.Filter))
            parts.Add($"filter-{options.Filter}");
        if (options.Categories.Count != 0)
            parts.Add(
                $"category-{string.Join("-", options.Categories.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))}");
        if (options.Features.Count != 0)
            parts.Add(
                $"feature-{string.Join("-", options.Features.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))}");
        if (options.MaxTests.HasValue)
            parts.Add($"max-{options.MaxTests.Value}");
        if (parts.Count == 0)
            parts.Add(Path.GetRelativePath(repoRoot, resolvedRoot).Replace('\\', '/'));
        return string.Join("__", parts);
    }

    private static string SlugifyProgressPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "scope";

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "scope" : slug;
    }

    private static string MakeDisplayPath(string repoRoot, string absoluteOrRelativePath, bool fullPath)
    {
        var full = Path.GetFullPath(absoluteOrRelativePath);
        if (fullPath)
            return full.Replace("\\", "/");
        var rel = Path.GetRelativePath(repoRoot, full);
        return (rel.StartsWith("..", StringComparison.Ordinal) ? full : rel).Replace("\\", "/");
    }

    private static bool MatchesFilter(string path, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        static string NormalizePathLike(string text)
        {
            return text.Replace('\\', '/');
        }

        var normalizedPath = NormalizePathLike(path);
        var normalizedFilter = NormalizePathLike(filter);
        return normalizedPath.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCategory(string path, string root, IReadOnlyCollection<string> categories)
    {
        if (categories.Count == 0) return true;

        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        foreach (var category in categories)
        {
            if (string.IsNullOrWhiteSpace(category)) continue;

            var token = category.Replace('\\', '/').Trim().Trim('/');
            if (token.Length == 0) continue;

            // Top-level category shorthand: "language" => only "language/**"
            if (!token.Contains('/'))
            {
                var prefix = token + "/";
                if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

                continue;
            }

            // Nested category path: allow exact subtree match.
            if (relative.StartsWith(token + "/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relative, token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetMetadataCachePath(string repoRoot, string resolvedRoot)
    {
        var rel = Path.GetRelativePath(repoRoot, resolvedRoot)
            .Replace('\\', '/')
            .Trim('/');
        if (string.IsNullOrEmpty(rel)) rel = "root";

        var safe = rel.Replace("/", "__").Replace(":", "_");
        return Path.Combine(repoRoot, "artifacts", "test262", "cache", safe + ".metadata.v1.json");
    }

    private static string GetPassedCachePath(string repoRoot, string resolvedRoot)
    {
        var rel = Path.GetRelativePath(repoRoot, resolvedRoot)
            .Replace('\\', '/')
            .Trim('/');
        if (string.IsNullOrEmpty(rel))
            rel = "root";

        var safe = rel.Replace("/", "__").Replace(":", "_");
        return Path.Combine(repoRoot, "artifacts", "test262", "cache", safe + ".passed.v1.json");
    }

    private static HashSet<string> LoadPassedCache(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return new(items, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SavePassedCache(string path, IEnumerable<string> passedPaths)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = passedPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var json = JsonSerializer.Serialize(payload);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static string NormalizeCachePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string GetFeatureMetadataCachePath(string repoRoot, string resolvedRoot, string feature)
    {
        var rel = Path.GetRelativePath(repoRoot, resolvedRoot)
            .Replace('\\', '/')
            .Trim('/');
        if (string.IsNullOrEmpty(rel)) rel = "root";

        var safeRoot = rel.Replace("/", "__").Replace(":", "_");
        var safeFeature = feature.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
        return Path.Combine(repoRoot, "artifacts", "test262", "cache", "features", safeRoot,
            safeFeature + ".metadata.v1.json");
    }

    private static TestFileCandidate[] LoadOrBuildMetadataCandidates(
        string resolvedRoot,
        string repoRoot,
        Test262Options options,
        string cachePath,
        Action<string> log)
    {
        if (options.UseMetadataCache && !options.RebuildMetadataCache && options.Features.Count > 0)
        {
            var featureCandidates = new Dictionary<string, TestFileCandidate>(StringComparer.OrdinalIgnoreCase);
            var loadedAny = false;
            var loadedAll = true;
            foreach (var feature in options.Features)
            {
                var featureCachePath = GetFeatureMetadataCachePath(repoRoot, resolvedRoot, feature);
                if (!File.Exists(featureCachePath))
                {
                    loadedAll = false;
                    break;
                }

                try
                {
                    var json = File.ReadAllText(featureCachePath);
                    var cache = JsonSerializer.Deserialize<Test262MetadataCache>(json);
                    if (cache is null)
                    {
                        loadedAll = false;
                        break;
                    }

                    foreach (var f in cache.Files)
                    {
                        var candidate = f.ToCandidate(resolvedRoot);
                        if (candidate is null) continue;

                        var key = candidate.Path.Replace('\\', '/');
                        featureCandidates[key] = candidate;
                    }

                    loadedAny = true;
                }
                catch
                {
                    loadedAll = false;
                    break;
                }
            }

            if (loadedAny && loadedAll) return featureCandidates.Values.ToArray();
        }

        if (options.UseMetadataCache && !options.RebuildMetadataCache && File.Exists(cachePath))
            try
            {
                var json = File.ReadAllText(cachePath);
                var cache = JsonSerializer.Deserialize<Test262MetadataCache>(json);
                if (cache is not null)
                    return cache.Files
                        .Select(f => f.ToCandidate(resolvedRoot))
                        .Where(c => c is not null)
                        .Select(c => c!)
                        .ToArray();
            }
            catch (Exception ex)
            {
                _ = ex;
            }

        var filePaths = Directory.EnumerateFiles(resolvedRoot, "*.js", SearchOption.AllDirectories).ToArray();
        var sw = Stopwatch.StartNew();
        var files = new List<Test262CachedFile>(filePaths.Length);
        var filesByFeature = new Dictionary<string, List<Test262CachedFile>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < filePaths.Length; i++)
        {
            var path = filePaths[i];
            var source = File.ReadAllText(path);
            var meta = Test262Metadata.Parse(source);
            var rel = Path.GetRelativePath(resolvedRoot, path).Replace('\\', '/');
            files.Add(new()
            {
                RelativePath = rel,
                IsNegative = meta.IsNegative,
                Features = meta.Features.ToArray(),
                Flags = meta.Flags.ToArray(),
                Includes = meta.Includes.ToArray()
            });

            if (meta.Features.Count > 0)
            {
                var cloned = new Test262CachedFile
                {
                    RelativePath = rel,
                    IsNegative = meta.IsNegative,
                    Features = meta.Features.ToArray(),
                    Flags = meta.Flags.ToArray(),
                    Includes = meta.Includes.ToArray()
                };

                foreach (var feature in meta.Features)
                {
                    if (!filesByFeature.TryGetValue(feature, out var list))
                    {
                        list = [];
                        filesByFeature[feature] = list;
                    }

                    list.Add(cloned);
                }
            }
        }

        var cacheObj = new Test262MetadataCache
        {
            RootRelative = Path.GetRelativePath(repoRoot, resolvedRoot).Replace('\\', '/'),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Files = files
        };

        if (options.UseMetadataCache)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var json = JsonSerializer.Serialize(cacheObj, new JsonSerializerOptions { WriteIndented = false });
                var tempPath = cachePath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                File.Move(tempPath, cachePath, true);

                if (filesByFeature.Count > 0)
                {
                    var featureRoot = Path.GetDirectoryName(GetFeatureMetadataCachePath(repoRoot, resolvedRoot, "_"))!;
                    Directory.CreateDirectory(featureRoot);
                    foreach (var pair in filesByFeature)
                    {
                        var featurePath = GetFeatureMetadataCachePath(repoRoot, resolvedRoot, pair.Key);
                        var featureCache = new Test262MetadataCache
                        {
                            RootRelative = cacheObj.RootRelative,
                            GeneratedAtUtc = cacheObj.GeneratedAtUtc,
                            Files = pair.Value
                        };

                        var featureJson = JsonSerializer.Serialize(featureCache,
                            new JsonSerializerOptions { WriteIndented = false });
                        var featureTempPath = featurePath + ".tmp";
                        File.WriteAllText(featureTempPath, featureJson,
                            new UTF8Encoding(false));
                        File.Move(featureTempPath, featurePath, true);
                    }
                }
            }
            catch (IOException)
            {
            }

        return files
            .Select(f => f.ToCandidate(resolvedRoot))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToArray();
    }

    private static bool ShouldSkip(Test262Options options, Test262Metadata metadata, string path, out string reason)
    {
        path = path.Replace('\\', '/');
        if (path.EndsWith("_FIXTURE.js", StringComparison.OrdinalIgnoreCase))
        {
            reason = "fixture";
            return true;
        }

        foreach (var entry in SkipList.Entries)
            if (path.Contains(entry.Pattern))
            {
                reason = entry.FormattedReason;
                return true;
            }

        if (options.Features.Count > 0 && metadata.Features.Count == 0)
        {
            reason = "feature filter requires metadata";
            return true;
        }

        if (metadata.Features.Count > 0)
        {
            var excludedFeatureHit = metadata.Features
                .FirstOrDefault(f => options.ExcludedFeatures.Contains(f) && !options.Features.Contains(f));
            if (excludedFeatureHit is not null)
            {
                reason = SkipList.TryGetExcludedFeature(excludedFeatureHit, out var featureEntry)
                    ? featureEntry.FormattedReason
                    : $"excluded feature '{excludedFeatureHit}'";
                return true;
            }

            if (options.Features.Count > 0 && !metadata.Features.Overlaps(options.Features))
            {
                reason = "feature filter mismatch";
                return true;
            }

            if (!options.AllowFeatureTests && options.Features.Count == 0)
            {
                reason = "features metadata present";
                return true;
            }
        }

        var unsupportedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "raw",
            "CanBlockIsFalse"
        };
        // if (options.Features.Count == 0)
        // {
        //     unsupportedFlags.Add("generated");
        // }

        if (metadata.Flags.Overlaps(unsupportedFlags))
        {
            reason = "unsupported flags";
            return true;
        }

        if (path.Contains("annexB", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Annex B excluded";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsModuleCase(Test262Metadata metadata, string path)
    {
        if (metadata.Flags.Contains("module"))
            return true;

        path = path.Replace('\\', '/');
        if (path.EndsWith("-script-code.js", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Contains("/module-code/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private enum Test262RegExpEngineMode
    {
        Current,
        Experimental,
        BuiltIn
    }

    private sealed class Test262Options
    {
        public required string Root { get; init; }
        public string? OutputPath { get; init; }
        public string? ProgressDocPath { get; init; }
        public string? ProgressJsonPath { get; init; }
        public string? QueryIncrementalPath { get; init; }
        public string? SingleTestPath { get; init; }
        public bool WorkerMode { get; init; }
        public string? Filter { get; init; }
        public HashSet<string> Categories { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Features { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AllowFeatureTests { get; init; }
        public int? MaxTests { get; init; }
        public int TimeoutMs { get; init; }
        public int TimeoutTotalMs { get; init; }
        public int StopOnLongTestSeconds { get; init; } = 5;
        public int ProgressSeconds { get; init; } = 2;
        public bool VerboseProgress { get; init; }
        public int Parallelism { get; init; } = 8;
        public bool UseMetadataCache { get; init; } = true;
        public bool RebuildMetadataCache { get; init; }
        public int MaxListed { get; init; } = 50;
        public bool ShowNotFailedTests { get; init; }
        public bool ShowSkippedInQuery { get; init; }
        public HashSet<string> QueryStatuses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? QueryReasonFilter { get; init; }
        public DateTimeOffset? QueryUpdatedSince { get; init; }
        public string QueryGroupBy { get; init; } = "all";
        public string QueryList { get; init; } = "";
        public int? QueryTop { get; init; }
        public bool FullPath { get; init; }
        public bool SkipPassed { get; init; }
        public bool QueryIncremental { get; init; }
        public Test262RegExpEngineMode RegExpEngineMode { get; init; } = Test262RegExpEngineMode.Current;
        public bool UseRealTimers { get; init; }

        public static Test262Options Parse(string[] args)
        {
            if (args.Any(static x => string.Equals(x, "--help", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(x, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                PrintHelp();
                Environment.Exit(0);
            }

            var root = Path.Combine("test262", "test");
            string? outputPath = null;
            string? progressDocPath = null;
            string? progressJsonPath = null;
            string? queryIncrementalPath = null;
            string? singleTestPath = null;
            var workerMode = false;
            string? filter = null;
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludedFeatures = new HashSet<string>(
                SkipList.DefaultExcludedFeatures,
                StringComparer.OrdinalIgnoreCase);
            var allowFeatureTests = true;
            int? maxTests = null;
            var timeoutMs = 0;
            var timeoutTotalMs = 0;
            var stopOnLongTestSeconds = 8;
            var progressSeconds = 2;
            var verboseProgress = false;
            var parallelism = 8;
            var useMetadataCache = true;
            var rebuildMetadataCache = false;
            var maxListed = 50;
            var showNotFailed = false;
            var showSkippedInQuery = false;
            var queryStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? queryReasonFilter = null;
            DateTimeOffset? queryUpdatedSince = null;
            var queryGroupBy = "all";
            var queryList = "";
            int? queryTop = null;
            var fullPath = false;
            var skipPassed = false;
            var regExpEngineMode = Test262RegExpEngineMode.Current;
            var useRealTimers = false;
            for (var i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--root" when i + 1 < args.Length:
                        root = args[++i];
                        break;
                    case "--progress-doc":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            progressDocPath = args[++i];
                        else
                            progressDocPath = string.Empty;
                        break;
                    case "--progress-json":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            progressJsonPath = args[++i];
                        else
                            progressJsonPath = string.Empty;
                        break;
                    case "--query-incremental":
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            queryIncrementalPath = args[++i];
                        else
                            queryIncrementalPath = string.Empty;
                        break;
                    case "--single-test" when i + 1 < args.Length:
                        singleTestPath = args[++i];
                        break;
                    case "--worker-mode":
                        workerMode = true;
                        break;
                    case "--filter" when i + 1 < args.Length:
                        filter = args[++i];
                        break;
                    case "--out" when i + 1 < args.Length:
                        outputPath = args[++i];
                        break;
                    case "--category" when i + 1 < args.Length:
                        AddCsvValues(categories, args[++i]);
                        break;
                    case "--feature" when i + 1 < args.Length:
                        AddCsvValues(features, args[++i]);
                        break;
                    case "--exclude-feature" when i + 1 < args.Length:
                        AddCsvValues(excludedFeatures, args[++i]);
                        break;
                    case "--allow-features":
                        allowFeatureTests = true;
                        break;
                    case "--max-tests" when i + 1 < args.Length && int.TryParse(args[++i], out var maxTestsValue):
                        maxTests = Math.Max(1, maxTestsValue);
                        break;
                    case "--timeout-ms" when i + 1 < args.Length && int.TryParse(args[++i], out var timeoutValue):
                        timeoutMs = Math.Max(0, timeoutValue);
                        break;
                    case "--timeout-total-ms"
                        when i + 1 < args.Length && int.TryParse(args[++i], out var timeoutTotalValue):
                        timeoutTotalMs = Math.Max(0, timeoutTotalValue);
                        break;
                    case "--stop-on-long-test-seconds"
                        when i + 1 < args.Length && int.TryParse(args[++i], out var stopOnLongTestSecondsValue):
                        stopOnLongTestSeconds = Math.Max(0, stopOnLongTestSecondsValue);
                        break;
                    case "--progress-seconds"
                        when i + 1 < args.Length && int.TryParse(args[++i], out var progressSecondsValue):
                        progressSeconds = Math.Max(0, progressSecondsValue);
                        break;
                    case "--verbose-progress":
                        verboseProgress = true;
                        break;
                    case "--parallel" when i + 1 < args.Length && int.TryParse(args[++i], out var parallelValue):
                        parallelism = Math.Max(1, parallelValue);
                        break;
                    case "--no-metadata-cache":
                        useMetadataCache = false;
                        break;
                    case "--rebuild-cache":
                        rebuildMetadataCache = true;
                        break;
                    case "--max-listed" when i + 1 < args.Length && int.TryParse(args[++i], out var value):
                        maxListed = Math.Max(1, value);
                        break;
                    case "--show-passed":
                        showNotFailed = true;
                        break;
                    case "--show-skipped":
                        showSkippedInQuery = true;
                        break;
                    case "--status" when i + 1 < args.Length:
                        AddCsvValues(queryStatuses, args[++i]);
                        break;
                    case "--reason" when i + 1 < args.Length:
                        queryReasonFilter = args[++i];
                        break;
                    case "--updated-since" when i + 1 < args.Length &&
                                                DateTimeOffset.TryParse(args[++i], out var updatedSinceValue):
                        queryUpdatedSince = updatedSinceValue;
                        break;
                    case "--group-by" when i + 1 < args.Length:
                        queryGroupBy = args[++i].Trim();
                        break;
                    case "--list" when i + 1 < args.Length:
                        queryList = args[++i].Trim();
                        break;
                    case "--top" when i + 1 < args.Length && int.TryParse(args[++i], out var topValue):
                        queryTop = Math.Max(1, topValue);
                        break;
                    case "--full-path":
                        fullPath = true;
                        break;
                    case "--skip-passed":
                        skipPassed = true;
                        break;
                    case "--regexp-engine" when i + 1 < args.Length:
                        regExpEngineMode = ParseRegExpEngineMode(args[++i]);
                        break;
                    case "--no-external-regexp":
                        regExpEngineMode = Test262RegExpEngineMode.BuiltIn;
                        break;
                    case "--real-timers":
                        useRealTimers = true;
                        break;
                }

            // Explicitly included features should override the default excluded-feature baseline.
            foreach (var includedFeature in features) excludedFeatures.Remove(includedFeature);

            if (showSkippedInQuery && string.IsNullOrWhiteSpace(queryList))
                queryList = "failed-skipped";

            return new()
            {
                Root = root,
                OutputPath = outputPath,
                ProgressDocPath = progressDocPath,
                ProgressJsonPath = progressJsonPath,
                QueryIncrementalPath = queryIncrementalPath,
                SingleTestPath = singleTestPath,
                WorkerMode = workerMode,
                Filter = filter,
                Categories = categories,
                Features = features,
                ExcludedFeatures = excludedFeatures,
                AllowFeatureTests = allowFeatureTests,
                MaxTests = maxTests,
                TimeoutMs = timeoutMs,
                TimeoutTotalMs = timeoutTotalMs,
                StopOnLongTestSeconds = stopOnLongTestSeconds,
                ProgressSeconds = progressSeconds,
                VerboseProgress = verboseProgress,
                Parallelism = parallelism,
                UseMetadataCache = useMetadataCache,
                RebuildMetadataCache = rebuildMetadataCache,
                MaxListed = maxListed,
                ShowNotFailedTests = showNotFailed,
                ShowSkippedInQuery = showSkippedInQuery,
                QueryStatuses = queryStatuses,
                QueryReasonFilter = queryReasonFilter,
                QueryUpdatedSince = queryUpdatedSince,
                QueryGroupBy = queryGroupBy,
                QueryList = queryList,
                QueryTop = queryTop,
                FullPath = fullPath,
                SkipPassed = skipPassed,
                QueryIncremental = queryIncrementalPath is not null,
                RegExpEngineMode = regExpEngineMode,
                UseRealTimers = useRealTimers
            };
        }

        private static void AddCsvValues(HashSet<string> output, string raw)
        {
            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                if (trimmed.Length > 0) output.Add(trimmed);
            }
        }

        private static Test262RegExpEngineMode ParseRegExpEngineMode(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "current" => Test262RegExpEngineMode.Current,
                "experimental" => Test262RegExpEngineMode.Experimental,
                "built-in" => Test262RegExpEngineMode.BuiltIn,
                "builtin" => Test262RegExpEngineMode.BuiltIn,
                _ => throw new ArgumentException(
                    $"Unknown RegExp engine '{value}'. Expected built-in, current, or experimental.")
            };
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Test262Runner options:");
            Console.WriteLine("  --root <path>               Root directory (default: test262/test)");
            Console.WriteLine("  --out <path>                Report output path");
            Console.WriteLine(
                "  --progress-doc [path]       Write dated markdown progress report (default: TEST262_PROGRESS.md)");
            Console.WriteLine(
                "  --progress-json [path]      Write machine-readable progress snapshot (default: TEST262_PROGRESS.json)");
            Console.WriteLine(
                "  --query-incremental [path]  Print failed/skipped/progress from TEST262_PROGRESS_INCREMENTAL.json");
            Console.WriteLine(
                "  --single-test <path>        Internal manager mode: run one test and emit a JSON result");
            Console.WriteLine(
                "  --worker-mode               Internal manager mode: serve multiple test requests over stdin/stdout");
            Console.WriteLine("  --filter <text>             Path substring filter");
            Console.WriteLine("  --category <name[,name]>    Category/path filter (repeatable)");
            Console.WriteLine("  --feature <name[,name]>     Include tests requiring these features (repeatable)");
            Console.WriteLine("  --status <name[,name]>      Query status filter: failed, skipped, passed, not-yet");
            Console.WriteLine("  --reason <text>             Query failed/skip reason substring filter");
            Console.WriteLine("  --updated-since <date>      Query only tests updated on/after the given date");
            Console.WriteLine("  --group-by <name>           Query grouping: all, category, folder, feature, none");
            Console.WriteLine(
                "  --list <name>               Query list output: failed, skipped, passed, not-yet, all, none");
            Console.WriteLine("  --top <n>                   Limit grouped rows in --query-incremental mode");
            Console.WriteLine("  --exclude-feature <name[,name]>  Exclude tests requiring these features (repeatable)");
            Console.WriteLine(
                "  --allow-features            No-op for compatibility; feature-tagged tests run by default unless excluded");
            Console.WriteLine("  --max-tests <n>             Stop after selecting first n test files");
            Console.WriteLine("  --timeout-ms <ms>           Per-test timeout in milliseconds (0 = no timeout)");
            Console.WriteLine("  --timeout-total-ms <ms>     Stop whole run after elapsed milliseconds");
            Console.WriteLine(
                "  --stop-on-long-test-seconds <n>  Timeout a test after n seconds and skip it (default: 8)");
            Console.WriteLine("  --show-skipped              Print skipped tests in --query-incremental mode");
            Console.WriteLine("  --progress-every <n>        Print progress every n files (default: 25)");
            Console.WriteLine("  --progress-seconds <n>      Print progress every n seconds with working tests");
            Console.WriteLine("  --verbose-progress          Print every file progress line");
            Console.WriteLine("  --parallel <n>              Run up to n test files in parallel (default: 8)");
            Console.WriteLine("  --no-metadata-cache         Disable metadata cache");
            Console.WriteLine("  --rebuild-cache             Rebuild metadata cache before run");
            Console.WriteLine("  --skip-passed               Skip tests recorded as passed in the local pass cache");
            Console.WriteLine(
                "  --regexp-engine <mode>      Select RegExp path: built-in, current, or experimental");
            Console.WriteLine(
                "  --no-external-regexp        Use the built-in RegExp path instead of the external engine");
            Console.WriteLine(
                "                             Alias for --regexp-engine built-in");
            Console.WriteLine(
                "  --real-timers               Use wall-clock timer waits instead of FakeTimeProvider-driven time");
            Console.WriteLine("  --max-listed <n>            Max listed failed/passed/skipped tests");
            Console.WriteLine("  --show-passed               Include passed/skipped list in report");
            Console.WriteLine(
                "  --full-path                 Display absolute paths instead of solution-relative paths");
            Console.WriteLine("  --help, -h                  Show this help");
        }
    }


    private sealed class Test262Metadata
    {
        public bool IsNegative { get; set; }
        public List<string> Includes { get; } = [];
        public HashSet<string> Features { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

        public static Test262Metadata Parse(string source)
        {
            var legacy = ParseLegacyMetadata(source);
            var startMarker = source.IndexOf("/*---", StringComparison.Ordinal);
            if (startMarker < 0) return legacy;

            var endMarker = source.IndexOf("---*/", startMarker + 5, StringComparison.Ordinal);
            if (endMarker < 0) return legacy;

            var metaText = source.Substring(startMarker + 5, endMarker - (startMarker + 5));
            var lines = metaText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var metadata = legacy;
            string? section = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("negative:", StringComparison.Ordinal))
                {
                    metadata.IsNegative = true;
                    section = "negative";
                    continue;
                }

                if (line.StartsWith("features:", StringComparison.Ordinal))
                {
                    section = "features";
                    ParseInlineList(line, metadata.Features);
                    continue;
                }

                if (line.StartsWith("flags:", StringComparison.Ordinal))
                {
                    section = "flags";
                    ParseInlineList(line, metadata.Flags);
                    continue;
                }

                if (line.StartsWith("includes:", StringComparison.Ordinal))
                {
                    section = "includes";
                    ParseInlineList(line, metadata.Includes);
                    continue;
                }

                if (section == "flags")
                {
                    if (line.StartsWith("-", StringComparison.Ordinal))
                        metadata.Flags.Add(CleanMetadataToken(line.TrimStart('-').Trim()));

                    continue;
                }

                if (section == "features")
                {
                    if (line.StartsWith("-", StringComparison.Ordinal))
                        metadata.Features.Add(CleanMetadataToken(line.TrimStart('-').Trim()));

                    continue;
                }

                if (section == "includes")
                    if (line.StartsWith("-", StringComparison.Ordinal))
                        metadata.Includes.Add(CleanMetadataToken(line.TrimStart('-').Trim()));
            }

            return metadata;
        }

        private static Test262Metadata ParseLegacyMetadata(string source)
        {
            var metadata = new Test262Metadata();
            if (source.IndexOf("@negative", StringComparison.OrdinalIgnoreCase) >= 0) metadata.IsNegative = true;

            if (source.IndexOf("@noStrict", StringComparison.OrdinalIgnoreCase) >= 0) metadata.Flags.Add("noStrict");

            if (source.IndexOf("@onlyStrict", StringComparison.OrdinalIgnoreCase) >= 0)
                metadata.Flags.Add("onlyStrict");

            if (source.IndexOf("@module", StringComparison.OrdinalIgnoreCase) >= 0) metadata.Flags.Add("module");

            return metadata;
        }

        private static void ParseInlineList(string line, HashSet<string> output)
        {
            var start = line.IndexOf('[');
            var end = line.IndexOf(']');
            if (start < 0 || end <= start) return;

            var inner = line.Substring(start + 1, end - start - 1);
            foreach (var token in inner.Split(','))
            {
                var item = CleanMetadataToken(token.Trim());
                if (item.Length > 0) output.Add(item);
            }
        }

        private static void ParseInlineList(string line, List<string> output)
        {
            var start = line.IndexOf('[');
            var end = line.IndexOf(']');
            if (start < 0 || end <= start) return;

            var inner = line.Substring(start + 1, end - start - 1);
            foreach (var token in inner.Split(','))
            {
                var item = CleanMetadataToken(token.Trim());
                if (item.Length > 0) output.Add(item);
            }
        }

        private static string CleanMetadataToken(string item)
        {
            if (item.Length >= 2)
                if ((item[0] == '\'' && item[^1] == '\'') || (item[0] == '"' && item[^1] == '"'))
                    return item.Substring(1, item.Length - 2);

            return item;
        }
    }

    private sealed class Test262MetadataCache
    {
        public string RootRelative { get; set; } = "";
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public List<Test262CachedFile> Files { get; set; } = [];
    }

    private sealed class Test262CachedFile
    {
        public string RelativePath { get; set; } = "";
        public bool IsNegative { get; set; }
        public string[] Features { get; set; } = [];
        public string[] Flags { get; set; } = [];
        public string[] Includes { get; set; } = [];

        public TestFileCandidate? ToCandidate(string resolvedRoot)
        {
            if (string.IsNullOrWhiteSpace(RelativePath)) return null;

            var path = Path.GetFullPath(Path.Combine(resolvedRoot,
                RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var metadata = new Test262Metadata
            {
                IsNegative = IsNegative
            };
            foreach (var f in Features) metadata.Features.Add(f);

            foreach (var f in Flags) metadata.Flags.Add(f);

            foreach (var include in Includes) metadata.Includes.Add(include);

            return new(path, metadata);
        }
    }

    private sealed record TestFileCandidate(
        string Path,
        Test262Metadata Metadata);
}
