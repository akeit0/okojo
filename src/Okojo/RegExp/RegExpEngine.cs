using Okojo.Text.RegularExpressions;
using EcmaCapture = Okojo.Text.RegularExpressions.EcmaCapture;
using EcmaRegexFlagSet = Okojo.Text.RegularExpressions.EcmaRegexFlagSet;
using EcmaRegexOptions = Okojo.Text.RegularExpressions.EcmaRegexOptions;

namespace Okojo.RegExp;

/// <summary>
///     Engine-internal ECMAScript regex engine backed by <see cref="Okojo.Text.RegularExpressions.EcmaRegex"/>.
/// </summary>
internal sealed class RegExpEngine
{
    public static RegExpEngine Default { get; } = new();

    private readonly EcmaRegexOptions _options;

    private RegExpEngine(EcmaRegexOptions? options = null)
    {
        _options = options ?? EcmaRegexOptions.Default;
    }

    public RegExpCompiledPattern Compile(string pattern, string flags)
    {
        var ecma = EcmaRegex.Compile(pattern, flags, _options);
        return new(
            pattern,
            ecma.FlagsText,
            pattern,
            ToNamedGroupNames(ecma),
            ToRuntimeFlags(ecma.Flags)
        )
        {
            EngineState = ecma,
        };
    }

    public RegExpMatchResult? Exec(RegExpCompiledPattern compiled, string input, int startIndex)
    {
        if (compiled.EngineState is not EcmaRegex ecma)
            throw new ArgumentException(
                "Compiled pattern was not created by RegExpEngine.",
                nameof(compiled)
            );

        var captures = new EcmaCapture[ecma.RequiredCaptureCount];
        if (!ecma.TryMatch(input, startIndex, captures, out _))
            return null;

        return BuildMatchResult(compiled, ecma, input, captures);
    }

    private static string[] ToNamedGroupNames(EcmaRegex ecma)
    {
        var names = new string[ecma.GroupNames.Count];
        var i = 0;
        foreach (var name in ecma.GroupNames.Keys)
            names[i++] = name;
        return names;
    }

    private static RegExpRuntimeFlags ToRuntimeFlags(EcmaRegexFlagSet flags) =>
        new(
            (flags & EcmaRegexFlagSet.Global) != 0,
            (flags & EcmaRegexFlagSet.IgnoreCase) != 0,
            (flags & EcmaRegexFlagSet.Multiline) != 0,
            (flags & EcmaRegexFlagSet.HasIndices) != 0,
            (flags & EcmaRegexFlagSet.Sticky) != 0,
            (flags & (EcmaRegexFlagSet.Unicode | EcmaRegexFlagSet.UnicodeSets)) != 0,
            (flags & EcmaRegexFlagSet.UnicodeSets) != 0,
            (flags & EcmaRegexFlagSet.DotAll) != 0
        );

    private static RegExpMatchResult BuildMatchResult(
        RegExpCompiledPattern compiled,
        EcmaRegex ecma,
        string input,
        EcmaCapture[] captures
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
                if (ecma.NameGroups.TryGetValue(name, out var indexes))
                {
                    foreach (var index in indexes)
                    {
                        if (!captures[index].Success)
                            continue;

                        value = groups[index];
                        range = groupIndices?[index];
                        break;
                    }
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
