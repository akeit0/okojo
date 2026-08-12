namespace Okojo.Text.RegularExpressions;

/// <summary>A UTF-16 range for a successful capture, or <see cref="Unmatched"/>.</summary>
public readonly record struct EcmaCapture(int Index, int Length)
{
    public static EcmaCapture Unmatched => new(-1, 0);
    public bool Success => Index >= 0;
    public int End => Success ? checked(Index + Length) : -1;

    public ReadOnlySpan<char> Value(ReadOnlySpan<char> input) =>
        Success ? input.Slice(Index, Length) : ReadOnlySpan<char>.Empty;

    public override string ToString() => Success ? $"[{Index}..{End})" : "<unmatched>";
}
