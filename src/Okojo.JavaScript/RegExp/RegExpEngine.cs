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

    private RegExpEngine(RegExpOptions? options = null)
    {
        _options = options ?? RegExpOptions.Default;
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
