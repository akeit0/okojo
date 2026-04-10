internal sealed record IncrementalProgressEntry(
    string Path,
    string RelativePath,
    IReadOnlyList<string> Features,
    string Status,
    string? SkipReason,
    string? SkipSpecStatus,
    DateTimeOffset LastUpdated);

internal sealed record IncrementalProgressSnapshot(
    IReadOnlyList<IncrementalProgressEntry> Entries);
