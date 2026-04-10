namespace Okojo.RegExp;

public readonly record struct RegExpRuntimeFlags(
    bool Global,
    bool IgnoreCase,
    bool Multiline,
    bool HasIndices,
    bool Sticky,
    bool Unicode,
    bool UnicodeSets,
    bool DotAll);
