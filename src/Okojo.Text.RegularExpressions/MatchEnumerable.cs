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

    public MatchEnumerator GetEnumerator() => new(_regex, _input, _startIndex);
}

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

    public readonly MatchView Current => _current;

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

    public EcmaMatch Match { get; }
    public int Index => Match.Index;
    public int Length => Match.Length;
    public ReadOnlySpan<char> Value => Match.Value(_input);
    public ReadOnlySpan<EcmaCapture> Captures => _captures;

    public ReadOnlySpan<char> GroupValue(int group)
    {
        if ((uint)group >= (uint)_captures.Length)
            throw new ArgumentOutOfRangeException(nameof(group));
        return _captures[group].Value(_input);
    }
}
