namespace Okojo.Text.RegularExpressions;

/// <summary>
/// Compatibility facade for callers that prefer an explicit “compiled pattern” type name.
/// All execution is delegated to the immutable <see cref="EcmaRegex"/> instance.
/// </summary>
public sealed class EcmaRegexPattern
{
    private readonly EcmaRegex _regex;

    private EcmaRegexPattern(EcmaRegex regex) => _regex = regex;

    public string Pattern => _regex.Pattern;
    public EcmaRegexFlagSet Flags => _regex.Flags;
    public string FlagsText => _regex.FlagsText;
    public int ExplicitCaptureCount => _regex.CaptureCount;
    public int RequiredCaptureCount => _regex.RequiredCaptureCount;
    public bool UsesLinearVmForIsMatch => _regex.UsesLinearEngineForIsMatch;
    public IReadOnlyDictionary<string, int> GroupNames => _regex.GroupNames;

    public static string UnicodeDataSource => EcmaRegex.UnicodeDataSource;

    public static EcmaRegexPattern Compile(
        string pattern,
        string? flags = null,
        EcmaRegexOptions? options = null
    ) => new(EcmaRegex.Compile(pattern, flags, options));

    public static EcmaRegexPattern Compile(
        string pattern,
        EcmaRegexFlagSet flags,
        EcmaRegexOptions? options = null
    ) => new(EcmaRegex.Compile(pattern, flags, options));

    public bool IsMatch(ReadOnlySpan<char> input, int startIndex = 0) =>
        _regex.IsMatch(input, startIndex);

    public bool TryMatch(
        ReadOnlySpan<char> input,
        int startIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => _regex.TryMatch(input, startIndex, captures, out match);

    public bool TryMatch(
        ReadOnlySpan<char> input,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => _regex.TryMatch(input, captures, out match);

    public bool TryMatchAt(
        ReadOnlySpan<char> input,
        int startIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => _regex.TryMatchAt(input, startIndex, captures, out match);

    public bool TryExec(
        ReadOnlySpan<char> input,
        ref int lastIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => _regex.TryExec(input, ref lastIndex, captures, out match);

    public EcmaMatchResult? Match(string input, int startIndex = 0) =>
        _regex.Match(input, startIndex);

    public int GetCaptureIndex(string name) => _regex.GetCaptureIndex(name);

    public MatchEnumerable EnumerateMatches(ReadOnlySpan<char> input, int startIndex = 0) =>
        _regex.EnumerateMatches(input, startIndex);

    public string GetDebugView() => _regex.GetDebugView();
}
