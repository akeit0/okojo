namespace Okojo.JavaScript.RegExp;

internal readonly record struct RegExpMatchRange(int Start, int End);

internal sealed record RegExpMatchResult(
    int Index,
    int Length,
    string?[] Groups,
    IReadOnlyDictionary<string, string?>? NamedGroups,
    RegExpMatchRange?[]? GroupIndices = null,
    IReadOnlyDictionary<string, RegExpMatchRange?>? NamedGroupIndices = null
);
