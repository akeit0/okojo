namespace Okojo.Values;

public sealed class Symbol(int atom, string? description, bool isWellKnown = false, bool isRegistered = false)
{
    public int Atom { get; } = atom;
    public string? Description { get; } = description;
    public bool IsWellKnown { get; } = isWellKnown;
    public bool IsRegistered { get; } = isRegistered;

    public override string ToString()
    {
        return Description is null ? "Symbol()" : $"Symbol({Description})";
    }
}
