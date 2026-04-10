namespace Okojo.Parsing;

public sealed class JsIdentifierTable
{
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    private readonly List<string> literals = new();
    private Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>>? alternateLookup;

    public string GetIdentifierLiteral(int identifierId)
    {
        if ((uint)identifierId >= (uint)literals.Count)
            throw new InvalidOperationException("Token does not reference an identifier literal.");

        return literals[identifierId];
    }

    public bool TryGetIdentifierId(string value, out int identifierId)
    {
        return indices.TryGetValue(value, out identifierId);
    }

    public bool TryGetIdentifierId(ReadOnlySpan<char> value, out int identifierId)
    {
        var lookup = alternateLookup ??= indices.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.TryGetValue(value, out identifierId);
    }

    public int AddIdentifierLiteral(string value)
    {
        if (indices.TryGetValue(value, out var existingIndex))
            return existingIndex;

        literals.Add(value);
        var index = literals.Count - 1;
        indices[value] = index;
        return index;
    }

    public int AddIdentifierLiteral(ReadOnlySpan<char> value)
    {
        var lookup = alternateLookup ??= indices.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(value, out var existingIndex))
            return existingIndex;

        var text = value.ToString();
        literals.Add(text);
        var index = literals.Count - 1;
        indices[text] = index;
        return index;
    }
}
