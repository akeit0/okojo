namespace Okojo.Parsing;

internal sealed partial class JsLexer
{
    private const int NumericSeparatorStackallocThreshold = 64;
    private readonly List<JsBigInt> bigIntLiterals = new();
    private readonly List<string> stringLiterals = new();
    public JsIdentifierTable IdentifierTable { get; } = new();

    public string GetStringLiteral(in JsToken token)
    {
        if (token.DataIndex < 0 || token.DataIndex >= stringLiterals.Count)
            throw new InvalidOperationException("Token does not reference a string literal.");

        return stringLiterals[token.DataIndex];
    }

    public string GetIdentifierLiteral(in JsToken token)
    {
        return GetIdentifierLiteral(token.DataIndex);
    }

    public string GetIdentifierLiteral(int identifierId)
    {
        return IdentifierTable.GetIdentifierLiteral(identifierId);
    }

    public JsBigInt GetBigIntLiteral(in JsToken token)
    {
        if (token.DataIndex < 0 || token.DataIndex >= bigIntLiterals.Count)
            throw new InvalidOperationException("Token does not reference a BigInt literal.");

        return bigIntLiterals[token.DataIndex];
    }

    private int AddStringLiteral(string value)
    {
        stringLiterals.Add(value);
        return stringLiterals.Count - 1;
    }

    public int AddIdentifierLiteral(string value)
    {
        return IdentifierTable.AddIdentifierLiteral(value);
    }

    public int AddIdentifierLiteral(ReadOnlySpan<char> value)
    {
        return IdentifierTable.AddIdentifierLiteral(value);
    }

    private int AddBigIntLiteral(JsBigInt value)
    {
        bigIntLiterals.Add(value);
        return bigIntLiterals.Count - 1;
    }

    private static void CopyWithoutNumericSeparators(ReadOnlySpan<char> text, Span<char> destination, out int written)
    {
        written = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
                continue;

            destination[written++] = text[i];
        }
    }
}
