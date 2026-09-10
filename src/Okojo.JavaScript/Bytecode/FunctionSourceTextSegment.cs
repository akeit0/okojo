namespace Okojo.JavaScript.Bytecode;

public readonly struct FunctionSourceTextSegment
{
    private readonly string? sourceText;
    private readonly int start;
    private readonly int length;

    public FunctionSourceTextSegment(string sourceText, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if ((uint)start > (uint)sourceText.Length)
            throw new ArgumentOutOfRangeException(nameof(start));
        if ((uint)length > (uint)(sourceText.Length - start))
            throw new ArgumentOutOfRangeException(nameof(length));
        this.sourceText = sourceText;
        this.start = start;
        this.length = length;
    }

    public static FunctionSourceTextSegment FromWholeString(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new(sourceText, 0, sourceText.Length);
    }

    public bool IsEmpty => sourceText is null || length == 0;

    public ReadOnlySpan<char> AsSpan() => sourceText.AsSpan(start, length);

    public override string ToString() =>
        IsEmpty ? string.Empty
        : start == 0 && length == sourceText!.Length ? sourceText!
        : sourceText!.Substring(start, length);
}
