namespace Okojo.Text.RegularExpressions;

/// <summary>Compilation and execution limits for an <see cref="EcmaRegex"/>.</summary>
public sealed record EcmaRegexOptions
{
    public static EcmaRegexOptions Default { get; } = new();

    /// <summary>Maximum UTF-16 pattern length accepted by the compiler.</summary>
    public int MaxPatternLength { get; init; } = 1_000_000;

    /// <summary>Maximum explicit capturing groups.</summary>
    public int MaxCaptureCount { get; init; } = 65_535;

    /// <summary>Maximum syntactic group nesting depth.</summary>
    public int MaxParseDepth { get; init; } = 256;

    /// <summary>Maximum instruction count across the prioritized program and assertion subprograms.</summary>
    public int MaxProgramSize { get; init; } = 1_000_000;

    /// <summary>Maximum numeric quantifier bound accepted by the parser.</summary>
    public int MaxRepeatCount { get; init; } = 1_000_000_000;

    /// <summary>Maximum VM instruction/closure steps per public operation.</summary>
    public long MaxSteps { get; init; } = 20_000_000;

    /// <summary>Maximum number of alternatives restored by the prioritized VM.</summary>
    public long MaxBacktracks { get; init; } = 2_000_000;

    /// <summary>Maximum explicit backtracking frames in one VM invocation.</summary>
    public int MaxBacktrackDepth { get; init; } = 262_144;

    /// <summary>Maximum recursively nested lookaround assertions.</summary>
    public int MaxAssertionDepth { get; init; } = 64;

    /// <summary>Wall-clock limit. The infinite value disables time checks.</summary>
    public TimeSpan MatchTimeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>Enables a Thompson/Pike linear-time Boolean engine for eligible patterns.</summary>
    public bool EnableLinearEngine { get; init; } = true;

    /// <summary>Maximum bounded-repeat expansion used only by the linear Boolean program.</summary>
    public int MaxLinearRepeatExpansion { get; init; } = 256;

    internal void Validate()
    {
        if (MaxPatternLength < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPatternLength));
        if (MaxCaptureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxCaptureCount));
        if (MaxParseDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxParseDepth));
        if (MaxProgramSize < 16)
            throw new ArgumentOutOfRangeException(nameof(MaxProgramSize));
        if (MaxRepeatCount < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxRepeatCount));
        if (MaxSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxSteps));
        if (MaxBacktracks < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxBacktracks));
        if (MaxBacktrackDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxBacktrackDepth));
        if (MaxAssertionDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxAssertionDepth));
        if (MaxLinearRepeatExpansion < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxLinearRepeatExpansion));
        if (
            MatchTimeout != System.Threading.Timeout.InfiniteTimeSpan
            && MatchTimeout <= TimeSpan.Zero
        )
            throw new ArgumentOutOfRangeException(nameof(MatchTimeout));
    }
}
