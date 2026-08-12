namespace Okojo.Text.RegularExpressions;

/// <summary>Categories of ECMAScript pattern or flag syntax errors.</summary>
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

    /// <summary>Zero-based index of the offending pattern position, or -1 when not position-specific.</summary>
    public int PatternIndex { get; }

    /// <summary>The category of the parse error.</summary>
    public EcmaRegexError Error { get; }
}

/// <summary>Categories of matching resource limits.</summary>
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

    /// <summary>The limit that was exceeded.</summary>
    public EcmaRegexLimitKind LimitKind { get; }
}
