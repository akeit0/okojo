using System.Buffers;
using System.Collections.ObjectModel;
using Okojo.Text.RegularExpressions.Internal;
using Okojo.Text.Unicode;

namespace Okojo.Text.RegularExpressions;

/// <summary>
/// Immutable compiled ECMAScript regular expression. Matching is allocation-free on the common
/// stack-sized path; larger workspaces spill to <see cref="ArrayPool{T}"/>.
/// </summary>
public sealed class EcmaRegex
{
    private readonly RegexProgram _program;
    private readonly LinearProgram? _linearProgram;
    private readonly EcmaRegexOptions _options;
    private readonly ReadOnlyDictionary<string, int> _groupNames;
    private readonly ReadOnlyDictionary<string, int[]> _nameGroups;
    private readonly bool _unicode;

    private EcmaRegex(
        string pattern,
        EcmaRegexFlagSet flags,
        EcmaRegexOptions options,
        ParseResult parseResult,
        RegexProgram program,
        LinearProgram? linearProgram
    )
    {
        Pattern = pattern;
        Flags = flags;
        FlagsText = EcmaRegexFlagParser.Format(flags);
        CaptureCount = parseResult.CaptureCount;
        RequiredCaptureCount = CaptureCount + 1;
        _options = options;
        _program = program;
        _linearProgram = linearProgram;
        _nameGroups = new ReadOnlyDictionary<string, int[]>(
            new Dictionary<string, int[]>(parseResult.GroupNames, StringComparer.Ordinal)
        );
        Dictionary<string, int> representatives = new(StringComparer.Ordinal);
        foreach ((string name, int[] groups) in parseResult.GroupNames)
            representatives.Add(name, groups[0]);
        _groupNames = new ReadOnlyDictionary<string, int>(representatives);
        _unicode = (flags & (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets)) != 0;
    }

    /// <summary>The original source pattern text.</summary>
    public string Pattern { get; }

    /// <summary>The parsed ECMAScript flags.</summary>
    public EcmaRegexFlagSet Flags { get; }

    /// <summary>The canonical flag string (e.g. <c>"dgimsuvy"</c>).</summary>
    public string FlagsText { get; }

    /// <summary>Number of explicit capturing groups, excluding group zero.</summary>
    public int CaptureCount { get; }

    /// <summary>Required caller buffer length: group zero plus all explicit groups.</summary>
    public int RequiredCaptureCount { get; }

    /// <summary>Maps each group name to its representative capture index.</summary>
    public IReadOnlyDictionary<string, int> GroupNames => _groupNames;

    /// <summary>
    /// Capture indices (ascending) sharing each group name. Names with multiple
    /// indices are duplicate named capturing groups; a named-group value is the
    /// most recently matched index among the set.
    /// </summary>
    public IReadOnlyDictionary<string, int[]> NameGroups => _nameGroups;

    /// <summary>True if <see cref="IsMatch(ReadOnlySpan{char},int)"/> uses the linear Boolean engine.</summary>
    public bool UsesLinearEngineForIsMatch => _linearProgram is not null;

    /// <summary>Description of the Unicode data source used for property matching.</summary>
    public static string UnicodeDataSource => UnicodePropertyDatabase.DataSource;

    /// <summary>Compiles a pattern with flags given as a string (e.g. <c>"gi"</c>).</summary>
    public static EcmaRegex Compile(
        string pattern,
        string? flags = null,
        EcmaRegexOptions? options = null
    ) => Compile(pattern, EcmaRegexFlagParser.Parse(flags), options);

    /// <summary>Compiles a pattern with an explicit flag set.</summary>
    public static EcmaRegex Compile(
        string pattern,
        EcmaRegexFlagSet flags,
        EcmaRegexOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ValidateFlags(flags);
        options ??= EcmaRegexOptions.Default;
        options.Validate();

        if (pattern.Length > options.MaxPatternLength)
        {
            throw new EcmaRegexParseException(
                "Pattern exceeds MaxPatternLength.",
                options.MaxPatternLength,
                EcmaRegexError.PatternTooLarge
            );
        }

        ParseResult parsed = RegexParser.Parse(pattern, flags, options);
        RegexProgram program = RegexCompiler.Compile(
            parsed.Root,
            parsed.CaptureCount,
            flags,
            options
        );
        LinearProgram? linear = LinearNfaCompiler.TryCompile(parsed.Root, options);
        return new EcmaRegex(pattern, flags, options, parsed, program, linear);
    }

    /// <summary>Tests for a match without materializing capture ranges.</summary>
    public bool IsMatch(ReadOnlySpan<char> input, int startIndex = 0)
    {
        ValidateStart(input, startIndex);
        bool sticky = (Flags & EcmaRegexFlagSet.Sticky) != 0;
        if (_linearProgram is not null)
        {
            return LinearNfaRunner.IsMatch(
                _linearProgram,
                input,
                startIndex,
                sticky,
                _unicode,
                _options,
                _program.SearchPlan
            );
        }

        int slotCount = checked(RequiredCaptureCount * 2);
        int[]? rented = null;
        Span<int> registers =
            slotCount <= 128
                ? stackalloc int[slotCount]
                : (rented = ArrayPool<int>.Shared.Rent(slotCount)).AsSpan(0, slotCount);
        try
        {
            return BacktrackingVm.TrySearch(
                _program,
                input,
                startIndex,
                sticky,
                registers,
                _options,
                out _
            );
        }
        finally
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented, clearArray: false);
        }
    }

    /// <summary>Tests for a match over a span, starting at <paramref name="startIndex"/>.</summary>
    public bool IsMatch(string input, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        return IsMatch(input.AsSpan(), startIndex);
    }

    /// <summary>Searches at or after <paramref name="startIndex"/>, unless the <c>y</c> flag is set.</summary>
    public bool TryMatch(
        ReadOnlySpan<char> input,
        int startIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    )
    {
        bool sticky = (Flags & EcmaRegexFlagSet.Sticky) != 0;
        return TryMatchCore(input, startIndex, sticky, captures, out match);
    }

    /// <summary>Searches at or after index zero, unless the <c>y</c> flag is set.</summary>
    public bool TryMatch(
        ReadOnlySpan<char> input,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => TryMatch(input, 0, captures, out match);

    /// <summary>Attempts a match exactly at <paramref name="startIndex"/>, independent of flags.</summary>
    public bool TryMatchAt(
        ReadOnlySpan<char> input,
        int startIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    ) => TryMatchCore(input, startIndex, sticky: true, captures, out match);

    /// <summary>
    /// Implements ECMAScript RegExpBuiltinExec-style <c>lastIndex</c> transitions for <c>g</c>/<c>y</c>.
    /// A failed stateful execution resets <paramref name="lastIndex"/> to zero.
    /// </summary>
    public bool TryExec(
        ReadOnlySpan<char> input,
        ref int lastIndex,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    )
    {
        bool stateful = (Flags & (EcmaRegexFlagSet.Global | EcmaRegexFlagSet.Sticky)) != 0;
        int start = stateful ? Math.Max(0, lastIndex) : 0;
        if ((uint)start > (uint)input.Length)
        {
            if (stateful)
                lastIndex = 0;
            ClearCaptureBuffer(captures);
            match = EcmaMatch.Failure;
            return false;
        }

        bool sticky = (Flags & EcmaRegexFlagSet.Sticky) != 0;
        if (TryMatchCore(input, start, sticky, captures, out match))
        {
            if (stateful)
                lastIndex = match.End;
            return true;
        }

        if (stateful)
            lastIndex = 0;
        return false;
    }

    /// <summary>Allocating convenience execution API.</summary>
    public EcmaMatchResult? Exec(string input, ref int lastIndex)
    {
        ArgumentNullException.ThrowIfNull(input);
        EcmaCapture[] captures = new EcmaCapture[RequiredCaptureCount];
        return TryExec(input.AsSpan(), ref lastIndex, captures, out _)
            ? new EcmaMatchResult(input, captures, _nameGroups)
            : null;
    }

    /// <summary>Allocating convenience search API that does not use JavaScript state.</summary>
    public EcmaMatchResult? Match(string input, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        EcmaCapture[] captures = new EcmaCapture[RequiredCaptureCount];
        return TryMatch(input.AsSpan(), startIndex, captures, out _)
            ? new EcmaMatchResult(input, captures, _nameGroups)
            : null;
    }

    /// <summary>Enumerates all non-overlapping matches over the input span.</summary>
    public MatchEnumerable EnumerateMatches(ReadOnlySpan<char> input, int startIndex = 0)
    {
        ValidateStart(input, startIndex);
        return new MatchEnumerable(this, input, startIndex);
    }

    /// <summary>Returns the capture index for a named group, or throws if the name is unknown.</summary>
    public int GetCaptureIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _groupNames.TryGetValue(name, out int index)
            ? index
            : throw new KeyNotFoundException($"No capture group named '{name}' exists.");
    }

    /// <summary>Returns a human-readable disassembly of the compiled program.</summary>
    public string GetDebugView()
    {
        string linear = _linearProgram is null
            ? "not eligible"
            : $"eligible ({_linearProgram.Code.Length} instructions)";
        return "=== prioritized bytecode ===\n"
            + _program.GetDebugView()
            + "=== linear Boolean engine ===\n"
            + linear
            + "\n";
    }

    internal int AdvanceStringIndex(ReadOnlySpan<char> input, int index) =>
        Utf16.AdvanceStringIndex(input, index, _unicode);

    private bool TryMatchCore(
        ReadOnlySpan<char> input,
        int startIndex,
        bool sticky,
        Span<EcmaCapture> captures,
        out EcmaMatch match
    )
    {
        ValidateStart(input, startIndex);
        if (captures.Length < RequiredCaptureCount)
        {
            throw new ArgumentException(
                $"Capture buffer must contain at least {RequiredCaptureCount} elements.",
                nameof(captures)
            );
        }

        int slotCount = checked(RequiredCaptureCount * 2);
        int[]? rented = null;
        Span<int> registers =
            slotCount <= 128
                ? stackalloc int[slotCount]
                : (rented = ArrayPool<int>.Shared.Rent(slotCount)).AsSpan(0, slotCount);
        try
        {
            if (
                !BacktrackingVm.TrySearch(
                    _program,
                    input,
                    startIndex,
                    sticky,
                    registers,
                    _options,
                    out int end
                )
            )
            {
                captures[..RequiredCaptureCount].Fill(EcmaCapture.Unmatched);
                match = EcmaMatch.Failure;
                return false;
            }

            for (int group = 0; group < RequiredCaptureCount; group++)
            {
                int start = registers[group * 2];
                int finish = registers[group * 2 + 1];
                if (start < 0 || finish < 0)
                {
                    captures[group] = EcmaCapture.Unmatched;
                }
                else
                {
                    if (start > finish)
                        (start, finish) = (finish, start);
                    captures[group] = new EcmaCapture(start, finish - start);
                }
            }

            EcmaCapture whole = captures[0];
            match = new EcmaMatch(whole.Index, whole.Length);
            System.Diagnostics.Debug.Assert(match.End == end);
            return true;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented, clearArray: false);
        }
    }

    private void ClearCaptureBuffer(Span<EcmaCapture> captures)
    {
        if (captures.Length < RequiredCaptureCount)
        {
            throw new ArgumentException(
                $"Capture buffer must contain at least {RequiredCaptureCount} elements.",
                nameof(captures)
            );
        }
        captures[..RequiredCaptureCount].Fill(EcmaCapture.Unmatched);
    }

    private static void ValidateStart(ReadOnlySpan<char> input, int startIndex)
    {
        if ((uint)startIndex > (uint)input.Length)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
    }

    private static void ValidateFlags(EcmaRegexFlagSet flags)
    {
        const EcmaRegexFlagSet all =
            EcmaRegexFlagSet.HasIndices
            | EcmaRegexFlagSet.Global
            | EcmaRegexFlagSet.IgnoreCase
            | EcmaRegexFlagSet.Multiline
            | EcmaRegexFlagSet.DotAll
            | EcmaRegexFlagSet.Unicode
            | EcmaRegexFlagSet.UnicodeSets
            | EcmaRegexFlagSet.Sticky;
        if ((flags & ~all) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags));
        if (
            (flags & (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets))
            == (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets)
        )
        {
            throw new EcmaRegexParseException(
                "The ECMAScript 'u' and 'v' flags are mutually exclusive.",
                -1,
                EcmaRegexError.IncompatibleFlags
            );
        }
    }
}
