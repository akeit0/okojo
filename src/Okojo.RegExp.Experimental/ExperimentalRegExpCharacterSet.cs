namespace Okojo.RegExp.Experimental;

internal readonly record struct ExperimentalRegExpAsciiBitmap(ulong Low, ulong High)
{
    public bool HasValue => Low != 0 || High != 0;
}

internal enum ExperimentalRegExpSimpleClassItemKind : byte
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

internal readonly record struct ExperimentalRegExpSimpleClassItem(
    ExperimentalRegExpSimpleClassItemKind Kind,
    int CodePoint = 0,
    int RangeStart = 0,
    int RangeEnd = 0,
    ScratchRegExpProgram.PropertyEscapeKind PropertyKind = default,
    bool PropertyNegated = false,
    ScratchRegExpProgram.GeneralCategoryMask PropertyCategories = ScratchRegExpProgram.GeneralCategoryMask.None,
    string? PropertyValue = null);

internal sealed class ExperimentalRegExpSimpleClass
{
    public required ExperimentalRegExpSimpleClassItem[] Items { get; init; }
    public required bool Negated { get; init; }
}

internal sealed class ExperimentalRegExpCharacterSet
{
    public ExperimentalRegExpSimpleClass? SimpleClass { get; init; }
    public ScratchRegExpProgram.ClassNode? ComplexClass { get; init; }
    public int[] LiteralCodePoints { get; init; } = [];
    public ExperimentalRegExpAsciiBitmap AsciiBitmap { get; init; }
}
