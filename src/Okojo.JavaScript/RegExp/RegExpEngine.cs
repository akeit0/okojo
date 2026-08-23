using Okojo.Text.RegularExpressions;
using CompiledRegExp = Okojo.Text.RegularExpressions.RegExp;

namespace Okojo.JavaScript.RegExp;

/// <summary>
///     Engine-internal ECMAScript regex engine backed by <see cref="Okojo.Text.RegularExpressions.RegExp"/>.
/// </summary>
internal sealed class RegExpEngine
{
    public static RegExpEngine Default { get; } = new();

    private readonly RegExpOptions _options;

    // R8-regexp: per-thread reusable capture buffer for the fast stepping
    // helpers below. Valid until the next TryMatchRanges call on the thread.
    [ThreadStatic]
    private static CaptureRange[]? t_reuseCaptures;

    private RegExpEngine(RegExpOptions? options = null)
    {
        _options = options ?? RegExpOptions.Default;
    }

    /// <summary>
    ///     Executes <paramref name="compiled"/> against
    ///     <paramref name="input"/> from <paramref name="startIndex"/>,
    ///     reporting the full capture-range set without constructing any
    ///     match-result objects. The returned ranges reference a per-thread
    ///     scratch buffer valid only until the next call on that thread.
    /// </summary>
    public bool TryMatchRanges(
        RegExpCompiledPattern compiled,
        string input,
        int startIndex,
        out int index,
        out int length,
        out CaptureRange[] ranges,
        out int rangeCount
    )
    {
        if (
            compiled.EngineState is not CompiledRegExp regexp
            || startIndex > input.Length
            || startIndex < 0
        )
        {
            index = 0;
            length = 0;
            ranges = [];
            rangeCount = 0;
            return false;
        }

        var required = regexp.RequiredCaptureCount;
        var captures = t_reuseCaptures;
        if (captures is null || captures.Length < required)
            captures = new CaptureRange[Math.Max(required, 4)];
        t_reuseCaptures = captures;

        if (!regexp.TryMatch(input, startIndex, captures.AsSpan(0, required), out _))
        {
            index = 0;
            length = 0;
            ranges = [];
            rangeCount = 0;
            return false;
        }

        index = captures[0].Index;
        length = captures[0].Length;
        ranges = captures;
        rangeCount = required;
        return true;
    }

    public RegExpCompiledPattern Compile(string pattern, string flags)
    {
        var regexp = CompiledRegExp.Compile(pattern, flags, _options);
        return new(
            pattern,
            regexp.FlagsText,
            pattern,
            ToNamedGroupNames(regexp),
            ToRuntimeFlags(regexp.Flags)
        )
        {
            EngineState = regexp,
        };
    }

    public RegExpMatchResult? Exec(RegExpCompiledPattern compiled, string input, int startIndex)
    {
        if (compiled.EngineState is not CompiledRegExp regexp)
            throw new ArgumentException(
                "Compiled pattern was not created by RegExpEngine.",
                nameof(compiled)
            );

        var captures = new CaptureRange[regexp.RequiredCaptureCount];
        if (!regexp.TryMatch(input, startIndex, captures, out _))
            return null;

        return BuildMatchResult(compiled, regexp, input, captures);
    }

    private static string[] ToNamedGroupNames(CompiledRegExp regexp)
    {
        var names = new string[regexp.GroupNames.Count];
        var i = 0;
        foreach (var name in regexp.GroupNames.Keys)
            names[i++] = name;
        return names;
    }

    private static RegExpRuntimeFlags ToRuntimeFlags(RegExpFlags flags) =>
        new(
            (flags & RegExpFlags.Global) != 0,
            (flags & RegExpFlags.IgnoreCase) != 0,
            (flags & RegExpFlags.Multiline) != 0,
            (flags & RegExpFlags.HasIndices) != 0,
            (flags & RegExpFlags.Sticky) != 0,
            (flags & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)) != 0,
            (flags & RegExpFlags.UnicodeSets) != 0,
            (flags & RegExpFlags.DotAll) != 0
        );

    private static RegExpMatchResult BuildMatchResult(
        RegExpCompiledPattern compiled,
        CompiledRegExp regexp,
        string input,
        CaptureRange[] captures
    )
    {
        var hasIndices = compiled.ParsedFlags.HasIndices;
        var groupCount = captures.Length;
        var groups = new string?[groupCount];
        var groupIndices = hasIndices ? new RegExpMatchRange?[groupCount] : null;

        for (var i = 0; i < groupCount; i++)
        {
            var capture = captures[i];
            if (!capture.Success)
                continue;

            groups[i] = input.Substring(capture.Index, capture.Length);
            if (groupIndices is not null)
                groupIndices[i] = new RegExpMatchRange(capture.Index, capture.End);
        }

        IReadOnlyDictionary<string, string?>? namedGroups = null;
        IReadOnlyDictionary<string, RegExpMatchRange?>? namedGroupIndices = null;
        var names = compiled.NamedGroupNames;
        if (names.Length != 0)
        {
            var valueDict = new Dictionary<string, string?>(names.Length, StringComparer.Ordinal);
            var indexDict = hasIndices
                ? new Dictionary<string, RegExpMatchRange?>(names.Length, StringComparer.Ordinal)
                : null;
            foreach (var name in names)
            {
                string? value = null;
                RegExpMatchRange? range = null;
                foreach (var index in regexp.GetCaptureIndices(name))
                {
                    if (!captures[index].Success)
                        continue;

                    value = groups[index];
                    range = groupIndices?[index];
                    break;
                }

                valueDict[name] = value;
                if (indexDict is not null)
                    indexDict[name] = range;
            }

            namedGroups = valueDict;
            namedGroupIndices = indexDict;
        }

        return new(
            captures[0].Index,
            captures[0].Length,
            groups,
            namedGroups,
            groupIndices,
            namedGroupIndices
        );
    }
}
