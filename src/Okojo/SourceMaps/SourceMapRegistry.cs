using System.Collections.Concurrent;

namespace Okojo.SourceMaps;

public sealed class SourceMapRegistry
{
    private readonly ConcurrentDictionary<string, SourceMapDocument> mapsByGeneratedSourcePath =
        new(SourcePathComparer.Instance);

    public void Register(SourceMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        mapsByGeneratedSourcePath[Path.GetFullPath(document.GeneratedSourcePath)] = document;
    }

    public bool TryGetDocument(string generatedSourcePath, out SourceMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(generatedSourcePath);
        return mapsByGeneratedSourcePath.TryGetValue(Path.GetFullPath(generatedSourcePath), out document!);
    }

    public bool TryMapToOriginal(string generatedSourcePath, int generatedLine, int generatedColumn,
        out SourceMapLocation location)
    {
        if (TryGetDocument(generatedSourcePath, out var document))
            return document.TryMapToOriginal(generatedLine, generatedColumn, out location);

        location = default;
        return false;
    }

    public bool TryMapToGenerated(string originalSourcePath, int originalLine, int originalColumn,
        out SourceMapLocation location)
    {
        ArgumentNullException.ThrowIfNull(originalSourcePath);
        var normalizedPath = Path.GetFullPath(originalSourcePath);

        foreach (var pair in mapsByGeneratedSourcePath)
            if (pair.Value.TryMapToGenerated(normalizedPath, originalLine, originalColumn, out location))
                return true;

        location = default;
        return false;
    }

    public bool TryGetSourceContent(string generatedSourcePath, string originalSourcePath, out string? sourceContent)
    {
        sourceContent = null;
        return TryGetDocument(generatedSourcePath, out var document) &&
               document.TryGetSourceContent(originalSourcePath, out sourceContent);
    }
}
