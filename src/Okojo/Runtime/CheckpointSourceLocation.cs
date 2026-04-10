namespace Okojo.Runtime;

public readonly record struct CheckpointSourceLocation(
    string? SourcePath,
    int Line,
    int Column);
