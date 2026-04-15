using System.Text.Json;
using System.Text.Json.Serialization;
using Test262Runner;

internal static class IncrementalProgressStoreCodec
{
    public static IReadOnlyList<IncrementalProgressEntry>? LoadEntries(string json)
    {
        var snapshot = JsonSerializer.Deserialize<CompactIncrementalProgressSnapshot>(json);
        if (snapshot?.E is null)
            return null;

        var featurePool = snapshot.F ?? [];
        return snapshot.E.Select(entry =>
        {
            var features = entry.F is null || entry.F.Length == 0
                ? Array.Empty<string>()
                : entry.F.Select(index => index >= 0 && index < featurePool.Length ? featurePool[index] : string.Empty)
                    .Where(static x => !string.IsNullOrEmpty(x))
                    .ToArray();
            return new IncrementalProgressEntry(
                entry.P,
                entry.P,
                features,
                DecodeStatus(entry.S),
                entry.K,
                entry.M,
                DecodeSkipSpecStatus(entry.T),
                entry.U == 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(entry.U));
        }).ToArray();
    }

    public static string Serialize(IncrementalProgressSnapshot snapshot)
    {
        var featurePool = snapshot.Entries
            .SelectMany(static x => x.Features)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var featureIndex = featurePool
            .Select((value, index) => new KeyValuePair<string, int>(value, index))
            .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.Ordinal);

        var entries = snapshot.Entries
            .Select(entry => new CompactIncrementalEntry(
                entry.Path,
                EncodeStatus(entry.Status),
                string.IsNullOrWhiteSpace(entry.SkipReason) ? null : entry.SkipReason,
                string.IsNullOrWhiteSpace(entry.FailureReason) ? null : entry.FailureReason,
                EncodeSkipSpecStatus(entry.SkipSpecStatus),
                entry.LastUpdated == DateTimeOffset.MinValue ? 0 : entry.LastUpdated.ToUnixTimeSeconds(),
                entry.Features.Count == 0 ? null : entry.Features.Select(feature => featureIndex[feature]).ToArray()))
            .ToArray();

        return JsonSerializer.Serialize(new CompactIncrementalProgressSnapshot(1, featurePool, entries));
    }

    public static string DecodeStatus(byte status)
    {
        return status switch
        {
            1 => "passed",
            2 => "failed",
            3 => "skipped",
            _ => "not-yet"
        };
    }

    private static byte EncodeStatus(string status)
    {
        return status switch
        {
            "passed" => 1,
            "failed" => 2,
            "skipped" => 3,
            _ => 0
        };
    }

    private static string? DecodeSkipSpecStatus(byte status)
    {
        return status switch
        {
            1 => nameof(SkipList.SkipSpecStatus.Legacy),
            2 => nameof(SkipList.SkipSpecStatus.AnnexB),
            3 => nameof(SkipList.SkipSpecStatus.Proposal),
            4 => nameof(SkipList.SkipSpecStatus.FinishedProposalNotInBaseline),
            5 => nameof(SkipList.SkipSpecStatus.Standard),
            6 => nameof(SkipList.SkipSpecStatus.Other),
            _ => null
        };
    }

    private static byte EncodeSkipSpecStatus(string? status)
    {
        return status switch
        {
            nameof(SkipList.SkipSpecStatus.Legacy) => 1,
            nameof(SkipList.SkipSpecStatus.AnnexB) => 2,
            nameof(SkipList.SkipSpecStatus.Proposal) => 3,
            nameof(SkipList.SkipSpecStatus.FinishedProposalNotInBaseline) => 4,
            nameof(SkipList.SkipSpecStatus.Standard) => 5,
            nameof(SkipList.SkipSpecStatus.Other) => 6,
            _ => 0
        };
    }

    private sealed record CompactIncrementalProgressSnapshot(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("f")] string[] F,
        [property: JsonPropertyName("e")] CompactIncrementalEntry[] E);

    private sealed record CompactIncrementalEntry(
        [property: JsonPropertyName("p")] string P,
        [property: JsonPropertyName("s")] byte S,
        [property: JsonPropertyName("k")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? K,
        [property: JsonPropertyName("m")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? M,
        [property: JsonPropertyName("t")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        byte T,
        [property: JsonPropertyName("u")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        long U,
        [property: JsonPropertyName("f")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int[]? F);
}
