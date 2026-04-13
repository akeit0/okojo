namespace Okojo.RegExp.Experimental;

internal readonly record struct ExperimentalRegExpAsciiBitmap(ulong Low, ulong High)
{
    public bool HasValue => Low != 0 || High != 0;
}

internal sealed class ExperimentalRegExpSimpleClass
{
    public required ScratchRegExpProgram.ClassItem[] Items { get; init; }
    public required bool Negated { get; init; }
}

internal sealed class ExperimentalRegExpCharacterSet
{
    public ExperimentalRegExpSimpleClass? SimpleClass { get; init; }
    public ScratchRegExpProgram.ClassNode? ComplexClass { get; init; }
    public int[] LiteralCodePoints { get; init; } = [];
    public ExperimentalRegExpAsciiBitmap AsciiBitmap { get; init; }
}
