namespace Okojo.Text.RegularExpressions;

/// <summary>Allocation-free metadata for the whole match.</summary>
public readonly record struct MatchRange(int Index, int Length)
{
    /// <summary>A sentinel describing a failed match.</summary>
    public static MatchRange Failure => new(-1, 0);

    /// <summary>True if this describes a successful match.</summary>
    public bool Success => Index >= 0;

    /// <summary>Exclusive end offset of the match, or -1 when failed.</summary>
    public int End => Success ? checked(Index + Length) : -1;

    /// <summary>Returns the matched text slice of the input.</summary>
    public ReadOnlySpan<char> Value(ReadOnlySpan<char> input) =>
        Success ? input.Slice(Index, Length) : ReadOnlySpan<char>.Empty;
}

/// <summary>
/// Allocating convenience result. Use <see cref="RegExp.TryMatch(ReadOnlySpan{char},Span{CaptureRange},out MatchRange)"/>
/// on hot paths.
/// </summary>
public sealed class MatchResult
{
    private readonly string _input;
    private readonly CaptureRange[] _captures;

    /// <summary>Capture indices (ascending) sharing each group name.</summary>
    private readonly IReadOnlyDictionary<string, int[]> _nameGroups;

    internal MatchResult(
        string input,
        CaptureRange[] captures,
        IReadOnlyDictionary<string, int[]> nameGroups
    )
    {
        _input = input;
        _captures = captures;
        _nameGroups = nameGroups;
    }

    /// <summary>Start offset of the whole match.</summary>
    public int Index => _captures[0].Index;

    /// <summary>Length of the whole match.</summary>
    public int Length => _captures[0].Length;

    /// <summary>Exclusive end offset of the whole match.</summary>
    public int End => _captures[0].End;

    /// <summary>Matched text of the whole match.</summary>
    public string Value => _input.Substring(Index, Length);

    /// <summary>Number of explicit capturing groups.</summary>
    public int CaptureCount => Math.Max(0, _captures.Length - 1);

    /// <summary>All capture ranges, index zero being the whole match.</summary>
    public ReadOnlyMemory<CaptureRange> Captures => _captures;

    private IReadOnlyDictionary<string, int>? _groupNames;

    /// <summary>Maps each group name to one representative capture index.</summary>
    public IReadOnlyDictionary<string, int> GroupNames =>
        _groupNames ??= _nameGroups.ToDictionary(
            pair => pair.Key,
            pair => pair.Value[0],
            StringComparer.Ordinal
        );

    /// <summary>Returns the capture for a numeric group.</summary>
    public CaptureRange GetCapture(int groupNumber)
    {
        if ((uint)groupNumber >= (uint)_captures.Length)
            throw new ArgumentOutOfRangeException(nameof(groupNumber));
        return _captures[groupNumber];
    }

    /// <summary>Returns the most recently matched capture for a named group.</summary>
    public CaptureRange GetCapture(string groupName)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        if (!_nameGroups.TryGetValue(groupName, out int[]? groups))
            throw new KeyNotFoundException($"No capture group named '{groupName}' exists.");
        foreach (int group in groups)
        {
            if (_captures[group].Success)
                return _captures[group];
        }
        return CaptureRange.Unmatched;
    }

    /// <summary>Returns the matched text of a numeric group, or null if unmatched.</summary>
    public string? GetGroupValue(int groupNumber)
    {
        CaptureRange capture = GetCapture(groupNumber);
        return capture.Success ? _input.Substring(capture.Index, capture.Length) : null;
    }

    /// <summary>Returns the matched text of a named group, or null if unmatched.</summary>
    public string? GetGroupValue(string groupName)
    {
        CaptureRange capture = GetCapture(groupName);
        return capture.Success ? _input.Substring(capture.Index, capture.Length) : null;
    }
}
