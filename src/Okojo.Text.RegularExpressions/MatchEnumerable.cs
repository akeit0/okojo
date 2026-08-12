using System.Buffers;

namespace Okojo.Text.RegularExpressions;

/// <summary>Allocation-free input view with one pooled capture buffer per enumeration.</summary>
public readonly ref struct MatchEnumerable
{
    private readonly EcmaRegex _regex;
    private readonly ReadOnlySpan<char> _input;
    private readonly int _startIndex;

    internal MatchEnumerable(EcmaRegex regex, ReadOnlySpan<char> input, int startIndex)
    {
        _regex = regex;
        _input = input;
        _startIndex = startIndex;
    }

    /// <summary>Returns an enumerator over successive non-overlapping matches.</summary>
    public MatchEnumerator GetEnumerator() => new(_regex, _input, _startIndex);
}

/// <summary>Enumeration over successive non-overlapping matches, renting one capture buffer.</summary>
public ref struct MatchEnumerator
{
    private readonly EcmaRegex _regex;
    private readonly ReadOnlySpan<char> _input;
    private EcmaCapture[]? _captures;
    private int _nextIndex;
    private bool _finished;
    private MatchView _current;

    internal MatchEnumerator(EcmaRegex regex, ReadOnlySpan<char> input, int startIndex)
    {
        _regex = regex;
        _input = input;
        _captures = ArrayPool<EcmaCapture>.Shared.Rent(regex.RequiredCaptureCount);
        _nextIndex = startIndex;
        _finished = false;
        _current = default;
    }

    /// <summary>The current match view.</summary>
    public readonly MatchView Current => _current;

    /// <summary>Advances to the next match, returning false when no more matches exist.</summary>
    public bool MoveNext()
    {
        EcmaCapture[]? array = _captures;
        if (_finished || array is null || _nextIndex > _input.Length)
            return false;
        Span<EcmaCapture> captures = array.AsSpan(0, _regex.RequiredCaptureCount);
        if (!_regex.TryMatch(_input, _nextIndex, captures, out EcmaMatch match))
        {
            _finished = true;
            return false;
        }

        _current = new MatchView(_input, match, captures);
        _nextIndex = match.Length == 0 ? _regex.AdvanceStringIndex(_input, match.End) : match.End;
        return true;
    }

    /// <summary>Returns the rented capture buffer to the pool.</summary>
    public void Dispose()
    {
        EcmaCapture[]? array = _captures;
        _captures = null;
        _finished = true;
        _current = default;
        if (array is not null)
            ArrayPool<EcmaCapture>.Shared.Return(array, clearArray: false);
    }
}

/// <summary>Allocation-free view of a single match and its captures over the input span.</summary>
public readonly ref struct MatchView
{
    private readonly ReadOnlySpan<char> _input;
    private readonly ReadOnlySpan<EcmaCapture> _captures;

    internal MatchView(
        ReadOnlySpan<char> input,
        EcmaMatch match,
        ReadOnlySpan<EcmaCapture> captures
    )
    {
        _input = input;
        _captures = captures;
        Match = match;
    }

    /// <summary>Metadata for the whole match.</summary>
    public EcmaMatch Match { get; }

    /// <summary>Start offset of the whole match.</summary>
    public int Index => Match.Index;

    /// <summary>Length of the whole match.</summary>
    public int Length => Match.Length;

    /// <summary>Matched text of the whole match.</summary>
    public ReadOnlySpan<char> Value => Match.Value(_input);

    /// <summary>All capture ranges, index zero being the whole match.</summary>
    public ReadOnlySpan<EcmaCapture> Captures => _captures;

    /// <summary>Returns the captured text of a group, or empty if unmatched.</summary>
    public ReadOnlySpan<char> GroupValue(int group)
    {
        if ((uint)group >= (uint)_captures.Length)
            throw new ArgumentOutOfRangeException(nameof(group));
        return _captures[group].Value(_input);
    }
}
