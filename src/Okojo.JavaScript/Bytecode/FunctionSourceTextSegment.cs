namespace Okojo.Bytecode;

public struct FunctionSourceTextSegment
{
    private string? sourceText;
    private int start;
    private int length;

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

    public override string ToString()
    {
        if (sourceText is null || length == 0)
            return string.Empty;

        if (length == sourceText.Length)
            return sourceText;

        sourceText = sourceText.Substring(start, length);
        start = 0;
        length = sourceText.Length;
        return sourceText;
    }
}
