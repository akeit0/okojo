namespace Okojo.Text.RegularExpressions;

/// <summary>Allocation-free metadata for the whole match.</summary>
public readonly record struct EcmaMatch(int Index, int Length)
{
    /// <summary>A sentinel describing a failed match.</summary>
    public static EcmaMatch Failure => new(-1, 0);

    /// <summary>True if this describes a successful match.</summary>
    public bool Success => Index >= 0;

    /// <summary>Exclusive end offset of the match, or -1 when failed.</summary>
    public int End => Success ? checked(Index + Length) : -1;

    /// <summary>Returns the matched text slice of the input.</summary>
    public ReadOnlySpan<char> Value(ReadOnlySpan<char> input) =>
        Success ? input.Slice(Index, Length) : ReadOnlySpan<char>.Empty;
}

/// <summary>
/// Allocating convenience result. Use <see cref="EcmaRegex.TryMatch(ReadOnlySpan{char},Span{EcmaCapture},out EcmaMatch)"/>
/// on hot paths.
/// </summary>
public sealed class EcmaMatchResult
{
    private readonly string _input;
    private readonly EcmaCapture[] _captures;

    /// <summary>Capture indices (ascending) sharing each group name.</summary>
    private readonly IReadOnlyDictionary<string, int[]> _nameGroups;

    internal EcmaMatchResult(
        string input,
        EcmaCapture[] captures,
        IReadOnlyDictionary<string, int[]> nameGroups
    )
    {
        _input = input;
        _captures = captures;
        _nameGroups = nameGroups;
    }

    /// <summary>True if a match was found.</summary>
    public bool Success => _captures.Length != 0 && _captures[0].Success;

    /// <summary>Start offset of the whole match, or -1 when failed.</summary>
    public int Index => Success ? _captures[0].Index : -1;

    /// <summary>Length of the whole match.</summary>
    public int Length => Success ? _captures[0].Length : 0;

    /// <summary>Exclusive end offset of the whole match.</summary>
    public int End => Success ? _captures[0].End : -1;

    /// <summary>Matched text of the whole match.</summary>
    public string Value => Success ? _input.Substring(Index, Length) : string.Empty;

    /// <summary>Number of explicit capturing groups.</summary>
    public int CaptureCount => Math.Max(0, _captures.Length - 1);

    /// <summary>All capture ranges, index zero being the whole match.</summary>
    public ReadOnlyMemory<EcmaCapture> Captures => _captures;

    private IReadOnlyDictionary<string, int>? _groupNames;

    /// <summary>Maps each group name to one representative capture index.</summary>
    public IReadOnlyDictionary<string, int> GroupNames =>
        _groupNames ??= _nameGroups.ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);

    /// <summary>Returns the capture for a numeric group.</summary>
    public EcmaCapture GetCapture(int groupNumber)
    {
        if ((uint)groupNumber >= (uint)_captures.Length)
            throw new ArgumentOutOfRangeException(nameof(groupNumber));
        return _captures[groupNumber];
    }

    /// <summary>Returns the most recently matched capture for a named group.</summary>
    public EcmaCapture GetCapture(string groupName)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        if (!_nameGroups.TryGetValue(groupName, out int[]? groups))
            throw new KeyNotFoundException($"No capture group named '{groupName}' exists.");
        foreach (int group in groups)
        {
            if (_captures[group].Success)
                return _captures[group];
        }
        return EcmaCapture.Unmatched;
    }

    /// <summary>Returns the matched text of a numeric group, or null if unmatched.</summary>
    public string? GetGroupValue(int groupNumber)
    {
        EcmaCapture capture = GetCapture(groupNumber);
        return capture.Success ? _input.Substring(capture.Index, capture.Length) : null;
    }

    /// <summary>Returns the matched text of a named group, or null if unmatched.</summary>
    public string? GetGroupValue(string groupName)
    {
        EcmaCapture capture = GetCapture(groupName);
        return capture.Success ? _input.Substring(capture.Index, capture.Length) : null;
    }
}
