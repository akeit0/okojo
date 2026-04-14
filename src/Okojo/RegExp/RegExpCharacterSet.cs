namespace Okojo.RegExp;

internal readonly record struct RegExpAsciiBitmap(ulong Low, ulong High)
{
    public bool HasValue => Low != 0 || High != 0;
}

internal readonly record struct RegExpPropertyEscape(
    ScratchRegExpProgram.PropertyEscapeKind Kind,
    bool Negated = false,
    ScratchRegExpProgram.GeneralCategoryMask Categories = ScratchRegExpProgram.GeneralCategoryMask.None,
    string? PropertyValue = null);

internal enum RegExpSimpleClassItemKind : byte
{
    Literal,
    Range,
    Digit,
    NotDigit,
    Space,
    NotSpace,
    Word,
    NotWord,
    PropertyEscape
}

internal readonly record struct RegExpSimpleClassItem(
    RegExpSimpleClassItemKind Kind,
    int CodePoint = 0,
    int RangeStart = 0,
    int RangeEnd = 0,
    RegExpPropertyEscape PropertyEscape = default);

internal sealed class RegExpSimpleClass
{
    public required RegExpSimpleClassItem[] Items { get; init; }
    public required bool Negated { get; init; }
}

internal sealed class RegExpCharacterSet
{
    public RegExpSimpleClass? SimpleClass { get; init; }
    public ScratchRegExpProgram.ClassNode? ComplexClass { get; init; }
    public int[] LiteralCodePoints { get; init; } = [];
    public RegExpAsciiBitmap AsciiBitmap { get; init; }
}
