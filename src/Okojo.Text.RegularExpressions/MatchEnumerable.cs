using System.Buffers;

namespace Okojo.Text.RegularExpressions;

/// <summary>Allocation-free input view with one pooled capture buffer per enumeration.</summary>
public readonly ref struct MatchEnumerable
{
    private readonly RegExp _regex;
    private readonly ReadOnlySpan<char> _input;
    private readonly int _startIndex;

    internal MatchEnumerable(RegExp regex, ReadOnlySpan<char> input, int startIndex)
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
    private readonly RegExp _regex;
    private readonly ReadOnlySpan<char> _input;
    private CaptureRange[]? _captures;
    private int _nextIndex;
    private bool _finished;
    private MatchView _current;

    internal MatchEnumerator(RegExp regex, ReadOnlySpan<char> input, int startIndex)
    {
        _regex = regex;
        _input = input;
        _captures = ArrayPool<CaptureRange>.Shared.Rent(regex.RequiredCaptureCount);
        _nextIndex = startIndex;
        _finished = false;
        _current = default;
    }

    /// <summary>The current match view.</summary>
    public readonly MatchView Current => _current;

    /// <summary>Advances to the next match, returning false when no more matches exist.</summary>
    public bool MoveNext()
    {
        CaptureRange[]? array = _captures;
        if (_finished || array is null || _nextIndex > _input.Length)
            return false;
        Span<CaptureRange> captures = array.AsSpan(0, _regex.RequiredCaptureCount);
        if (!_regex.TryMatch(_input, _nextIndex, captures, out MatchRange match))
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
        CaptureRange[]? array = _captures;
        _captures = null;
        _finished = true;
        _current = default;
        if (array is not null)
            ArrayPool<CaptureRange>.Shared.Return(array, clearArray: false);
    }
}

/// <summary>Allocation-free view of a single match and its captures over the input span.</summary>
public readonly ref struct MatchView
{
    private readonly ReadOnlySpan<char> _input;
    private readonly ReadOnlySpan<CaptureRange> _captures;

    internal MatchView(
        ReadOnlySpan<char> input,
        MatchRange match,
        ReadOnlySpan<CaptureRange> captures
    )
    {
        _input = input;
        _captures = captures;
        Range = match;
    }

    /// <summary>Metadata for the whole match.</summary>
    public MatchRange Range { get; }

    /// <summary>Start offset of the whole match.</summary>
    public int Index => Range.Index;

    /// <summary>Length of the whole match.</summary>
    public int Length => Range.Length;

    /// <summary>Matched text of the whole match.</summary>
    public ReadOnlySpan<char> Value => Range.Value(_input);

    /// <summary>All capture ranges, index zero being the whole match.</summary>
    public ReadOnlySpan<CaptureRange> Captures => _captures;

    /// <summary>Returns the captured text of a group, or empty if unmatched.</summary>
    public ReadOnlySpan<char> GetGroupValue(int group)
    {
        if ((uint)group >= (uint)_captures.Length)
            throw new ArgumentOutOfRangeException(nameof(group));
        return _captures[group].Value(_input);
    }
}
