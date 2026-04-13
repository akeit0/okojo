namespace Okojo.RegExp.Experimental;

internal readonly record struct ExperimentalRegExpAsciiBitmap(ulong Low, ulong High)
{
    public bool HasValue => Low != 0 || High != 0;
}

internal sealed class ExperimentalRegExpCharacterSet
{
    public ScratchRegExpProgram.ClassNode? Class { get; init; }
    public int[] LiteralCodePoints { get; init; } = [];
    public ExperimentalRegExpAsciiBitmap AsciiBitmap { get; init; }
}
