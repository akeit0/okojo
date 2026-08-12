namespace Okojo.Text.RegularExpressions;

/// <summary>A UTF-16 range for a successful capture, or <see cref="Unmatched"/>.</summary>
public readonly record struct EcmaCapture(int Index, int Length)
{
    /// <summary>A sentinel describing an unmatched group.</summary>
    public static EcmaCapture Unmatched => new(-1, 0);

    /// <summary>True if the group participated in the match.</summary>
    public bool Success => Index >= 0;

    /// <summary>Exclusive end offset of the capture, or -1 when unmatched.</summary>
    public int End => Success ? checked(Index + Length) : -1;

    /// <summary>Returns the captured text slice of the input.</summary>
    public ReadOnlySpan<char> Value(ReadOnlySpan<char> input) =>
        Success ? input.Slice(Index, Length) : ReadOnlySpan<char>.Empty;

    /// <inheritdoc />
    public override string ToString() => Success ? $"[{Index}..{End})" : "<unmatched>";
}
