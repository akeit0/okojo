namespace Okojo.Compiler;

internal readonly struct CompilerSymbolId(int value)
{
    public int Value { get; } = value;

    private const int TagShift = 24;
    private const int PayloadMask = 0x00FF_FFFF;
    private const byte SourceIdentifierTag = 0x00;
    private const byte SyntheticArgumentsTag = 0x80;
    private const byte DerivedThisTag = 0x81;
    private const byte SuperBaseTag = 0x82;
    private const byte CompilerSyntheticTag = 0xFF;

    public static CompilerSymbolId SyntheticArguments { get; } = new(unchecked((int)0x8000_0000));
    public static CompilerSymbolId DerivedThis { get; } = new(unchecked((int)0x8100_0000));
    public static CompilerSymbolId SuperBase { get; } = new(unchecked((int)0x8200_0000));

    public static CompilerSymbolId FromSourceIdentifier(int identifierId)
    {
        if ((uint)identifierId > PayloadMask)
            throw new InvalidOperationException("Identifier id exceeds packed compiler symbol id payload range.");

        return new(Pack(SourceIdentifierTag, identifierId));
    }

    public static CompilerSymbolId CreateCompilerSynthetic(int ordinal)
    {
        if ((uint)ordinal > PayloadMask)
            throw new InvalidOperationException("Synthetic compiler symbol id exceeds packed payload range.");

        return new(Pack(CompilerSyntheticTag, ordinal));
    }

    public static bool IsSourceIdentifier(int symbolId)
    {
        return GetTag(symbolId) == SourceIdentifierTag;
    }

    public static int GetSourceIdentifierId(int symbolId)
    {
        if (!IsSourceIdentifier(symbolId))
            throw new InvalidOperationException("Compiler symbol id does not reference a source identifier.");

        return symbolId & PayloadMask;
    }

    public static bool IsCompilerSynthetic(int symbolId)
    {
        return GetTag(symbolId) == CompilerSyntheticTag;
    }

    private static int Pack(byte tag, int payload)
    {
        return (tag << TagShift) | (payload & PayloadMask);
    }

    private static byte GetTag(int symbolId)
    {
        return unchecked((byte)((uint)symbolId >> TagShift));
    }
}
