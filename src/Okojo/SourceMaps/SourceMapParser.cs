using System.Text.Json;

namespace Okojo.SourceMaps;

public static class SourceMapParser
{
    public static SourceMapDocument Parse(string sourceMapJson, string generatedSourcePath,
        string? sourceMapPath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceMapJson);
        ArgumentNullException.ThrowIfNull(generatedSourcePath);

        using var document = JsonDocument.Parse(sourceMapJson);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetInt32();
        if (version != 3)
            throw new FormatException($"Unsupported source map version '{version}'.");

        var generatedPath = Path.GetFullPath(generatedSourcePath);
        var sourceRoot = root.TryGetProperty("sourceRoot", out var sourceRootProperty) &&
                         sourceRootProperty.ValueKind == JsonValueKind.String
            ? sourceRootProperty.GetString()
            : null;

        var sourcesElement = root.GetProperty("sources");
        var resolvedSources = new string[sourcesElement.GetArrayLength()];
        for (var i = 0; i < resolvedSources.Length; i++)
        {
            var rawSource = sourcesElement[i].GetString()
                            ?? throw new FormatException("Source map source entry cannot be null.");
            resolvedSources[i] = ResolveSourcePath(rawSource, sourceRoot, generatedPath, sourceMapPath);
        }

        IReadOnlyDictionary<string, string?>? sourceContents = null;
        if (root.TryGetProperty("sourcesContent", out var sourcesContentElement) &&
            sourcesContentElement.ValueKind == JsonValueKind.Array)
        {
            var contents = new Dictionary<string, string?>(SourcePathComparer.Instance);
            var count = Math.Min(resolvedSources.Length, sourcesContentElement.GetArrayLength());
            for (var i = 0; i < count; i++)
                contents[resolvedSources[i]] = sourcesContentElement[i].ValueKind == JsonValueKind.Null
                    ? null
                    : sourcesContentElement[i].GetString();

            sourceContents = contents;
        }

        var mappings = root.TryGetProperty("mappings", out var mappingsProperty)
            ? mappingsProperty.GetString() ?? string.Empty
            : string.Empty;

        var entries = ParseMappings(mappings, resolvedSources);
        return new(generatedPath, entries, sourceContents);
    }

    private static IReadOnlyList<SourceMapEntry> ParseMappings(string mappings, IReadOnlyList<string> resolvedSources)
    {
        var entries = new List<SourceMapEntry>();
        if (string.IsNullOrEmpty(mappings))
            return entries;

        var generatedLine = 1;
        var previousGeneratedColumn = 0;
        var previousSourceIndex = 0;
        var previousOriginalLine = 0;
        var previousOriginalColumn = 0;

        var lines = mappings.Split(';');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++, generatedLine++)
        {
            previousGeneratedColumn = 0;
            if (lines[lineIndex].Length == 0)
                continue;

            var segments = lines[lineIndex].Split(',');
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                if (segment.Length == 0)
                    continue;

                var values = Base64Vlq.Decode(segment);
                if (values.Length == 1)
                {
                    previousGeneratedColumn += values[0];
                    continue;
                }

                if (values.Length < 4)
                    continue;

                previousGeneratedColumn += values[0];
                previousSourceIndex += values[1];
                previousOriginalLine += values[2];
                previousOriginalColumn += values[3];

                if ((uint)previousSourceIndex >= (uint)resolvedSources.Count)
                    continue;

                entries.Add(new(
                    generatedLine,
                    previousGeneratedColumn + 1,
                    resolvedSources[previousSourceIndex],
                    previousOriginalLine + 1,
                    previousOriginalColumn + 1));
            }
        }

        return entries;
    }

    private static string ResolveSourcePath(
        string rawSourcePath,
        string? sourceRoot,
        string generatedSourcePath,
        string? sourceMapPath)
    {
        if (TryResolveFileUri(rawSourcePath, out var fileUriPath))
            return fileUriPath;

        var combined = rawSourcePath;
        if (!string.IsNullOrEmpty(sourceRoot))
        {
            if (TryResolveFileUri(sourceRoot, out var sourceRootUriPath))
                return Path.GetFullPath(rawSourcePath, sourceRootUriPath);

            combined = Path.Combine(sourceRoot, rawSourcePath);
        }

        if (Path.IsPathRooted(combined))
            return Path.GetFullPath(combined);

        var basePath = sourceMapPath is { Length: > 0 }
            ? Path.GetDirectoryName(Path.GetFullPath(sourceMapPath))!
            : Path.GetDirectoryName(generatedSourcePath)!;
        return Path.GetFullPath(combined, basePath);
    }

    private static bool TryResolveFileUri(string candidate, out string path)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = Path.GetFullPath(uri.LocalPath);
            return true;
        }

        path = string.Empty;
        return false;
    }
}
