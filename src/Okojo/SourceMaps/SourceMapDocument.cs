namespace Okojo.SourceMaps;

public sealed class SourceMapDocument
{
    private readonly Dictionary<int, List<SourceMapEntry>> entriesByGeneratedLine;
    private readonly Dictionary<string, Dictionary<int, List<SourceMapEntry>>> entriesByOriginalSource;

    internal SourceMapDocument(
        string generatedSourcePath,
        IReadOnlyList<SourceMapEntry> entries,
        IReadOnlyDictionary<string, string?>? sourceContents)
    {
        GeneratedSourcePath = Path.GetFullPath(generatedSourcePath);
        Entries = entries;
        SourceContents = sourceContents;

        entriesByGeneratedLine = new();
        entriesByOriginalSource = new(SourcePathComparer.Instance);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!entriesByGeneratedLine.TryGetValue(entry.GeneratedLine, out var generatedEntries))
            {
                generatedEntries = [];
                entriesByGeneratedLine[entry.GeneratedLine] = generatedEntries;
            }

            generatedEntries.Add(entry);

            if (!entriesByOriginalSource.TryGetValue(entry.OriginalSourcePath, out var originalLines))
            {
                originalLines = new();
                entriesByOriginalSource[entry.OriginalSourcePath] = originalLines;
            }

            if (!originalLines.TryGetValue(entry.OriginalLine, out var originalEntries))
            {
                originalEntries = [];
                originalLines[entry.OriginalLine] = originalEntries;
            }

            originalEntries.Add(entry);
        }
    }

    public string GeneratedSourcePath { get; }

    internal IReadOnlyList<SourceMapEntry> Entries { get; }

    public IReadOnlyDictionary<string, string?>? SourceContents { get; }

    public bool TryMapToOriginal(int generatedLine, int generatedColumn, out SourceMapLocation location)
    {
        if (!entriesByGeneratedLine.TryGetValue(generatedLine, out var entries))
        {
            location = default;
            return false;
        }

        SourceMapEntry? best = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.GeneratedColumn > generatedColumn)
                break;

            best = entry;
        }

        if (best is null)
        {
            location = default;
            return false;
        }

        var resolved = best.Value;
        location = new(resolved.OriginalSourcePath, resolved.OriginalLine, resolved.OriginalColumn);
        return true;
    }

    public bool TryMapToGenerated(string originalSourcePath, int originalLine, int originalColumn,
        out SourceMapLocation location)
    {
        ArgumentNullException.ThrowIfNull(originalSourcePath);
        originalSourcePath = Path.GetFullPath(originalSourcePath);

        if (!entriesByOriginalSource.TryGetValue(originalSourcePath, out var lines))
        {
            location = default;
            return false;
        }

        if (TryMapLine(lines, originalLine, originalColumn, out location))
            return true;

        int? nextLine = null;
        foreach (var candidate in lines.Keys)
        {
            if (candidate <= originalLine)
                continue;
            if (nextLine is null || candidate < nextLine.Value)
                nextLine = candidate;
        }

        if (nextLine is not null && TryMapLine(lines, nextLine.Value, 1, out location))
            return true;

        location = default;
        return false;
    }

    public bool TryGetSourceContent(string sourcePath, out string? sourceContent)
    {
        sourceContent = null;
        if (SourceContents is null)
            return false;

        return SourceContents.TryGetValue(Path.GetFullPath(sourcePath), out sourceContent);
    }

    private bool TryMapLine(
        Dictionary<int, List<SourceMapEntry>> lines,
        int originalLine,
        int originalColumn,
        out SourceMapLocation location)
    {
        if (!lines.TryGetValue(originalLine, out var entries) || entries.Count == 0)
        {
            location = default;
            return false;
        }

        var selected = entries[0];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.OriginalColumn >= originalColumn)
            {
                selected = entry;
                location = new(GeneratedSourcePath, selected.GeneratedLine, selected.GeneratedColumn);
                return true;
            }

            selected = entry;
        }

        location = new(GeneratedSourcePath, selected.GeneratedLine, selected.GeneratedColumn);
        return true;
    }
}
