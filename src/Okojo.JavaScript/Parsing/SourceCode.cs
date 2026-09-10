namespace Okojo.JavaScript.Parsing;

public sealed class SourceCode(string? source, string? path)
{
    private int[]? lineStarts;

    public string? Source { get; } = source;
    public string? Path { get; } = path;

    internal int[] GetOrCreateLineStarts()
    {
        if (lineStarts is not null)
            return lineStarts;

        if (string.IsNullOrEmpty(Source))
            return lineStarts = [0];

        // ECMA-262 line terminators: LF, CR, CRLF (single terminator), LS, PS.
        // Keep in sync with JsLexer.IsLineTerminator; the lexer is the
        // authority on what ends a line, this index only records where.
        var source = Source;
        var starts = new List<int>(source.Length / 32 + 2) { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                starts.Add(i + 1);
            }
            else if (c is '\n' or '\u2028' or '\u2029')
            {
                starts.Add(i + 1);
            }
        }

        lineStarts = starts.ToArray();
        return lineStarts;
    }
}
