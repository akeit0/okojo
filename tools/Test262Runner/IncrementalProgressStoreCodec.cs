using System.Text.Json;
using System.Text.Json.Serialization;

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
        [property: JsonPropertyName("u")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        long U,
        [property: JsonPropertyName("f")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int[]? F);
}
