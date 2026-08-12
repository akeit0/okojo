namespace Okojo.Text.RegularExpressions;

public enum EcmaRegexError
{
    UnexpectedToken,
    UnexpectedEnd,
    UnterminatedGroup,
    UnterminatedCharacterClass,
    NothingToRepeat,
    InvalidQuantifier,
    QuantifierRangeOutOfOrder,
    InvalidEscape,
    InvalidCharacterRange,
    InvalidBackreference,
    UnknownGroupName,
    DuplicateGroupName,
    InvalidGroupName,
    InvalidUnicodeProperty,
    InvalidFlag,
    IncompatibleFlags,
    PatternTooLarge,
    UnsupportedUnicodeSetString,
}

/// <summary>Thrown when an ECMAScript pattern or flag sequence is invalid.</summary>
public sealed class EcmaRegexParseException : ArgumentException
{
    public EcmaRegexParseException(string message, int patternIndex, EcmaRegexError error)
        : base(patternIndex >= 0 ? $"{message} (at pattern index {patternIndex})." : message)
    {
        PatternIndex = patternIndex;
        Error = error;
    }

    public int PatternIndex { get; }
    public EcmaRegexError Error { get; }
}

public enum EcmaRegexLimitKind
{
    Steps,
    Backtracks,
    BacktrackDepth,
    AssertionDepth,
    Timeout,
}

/// <summary>Thrown when a configured matching resource limit is exceeded.</summary>
public sealed class EcmaRegexExecutionException : Exception
{
    public EcmaRegexExecutionException(EcmaRegexLimitKind limitKind)
        : base($"Regular-expression execution exceeded the configured {limitKind} limit.")
    {
        LimitKind = limitKind;
    }

    public EcmaRegexLimitKind LimitKind { get; }
}
