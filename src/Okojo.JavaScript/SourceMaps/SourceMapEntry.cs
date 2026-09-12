namespace Okojo.JavaScript.SourceMaps;

internal readonly record struct SourceMapEntry(
    int GeneratedLine,
    int GeneratedColumn,
    string OriginalSourcePath,
    int OriginalLine,
    int OriginalColumn
);
