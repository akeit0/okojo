namespace Okojo.Parsing;

public static class SourceLocation
{
    public static bool TryGetLineOffsetRange(SourceCode sourceCode, int line, out int startOffset,
        out int endOffsetExclusive)
    {
        startOffset = 0;
        endOffsetExclusive = 0;

        if (line <= 0 || sourceCode.Source is null)
            return false;

        var lineStarts = sourceCode.GetOrCreateLineStarts();
        if ((uint)(line - 1) >= (uint)lineStarts.Length)
            return false;

        startOffset = lineStarts[line - 1];
        endOffsetExclusive = line >= lineStarts.Length ? sourceCode.Source.Length : lineStarts[line] - 1;
        return true;
    }

    public static bool TryGetLineOffsetRange(string source, int line, out int startOffset, out int endOffsetExclusive)
    {
        startOffset = 0;
        endOffsetExclusive = 0;

        if (line <= 0)
            return false;

        if (line == 1)
        {
            startOffset = 0;
            endOffsetExclusive = FindLineEnd(source, 0);
            return true;
        }

        var currentLine = 1;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n')
                continue;

            currentLine++;
            if (currentLine != line)
                continue;

            startOffset = i + 1;
            endOffsetExclusive = FindLineEnd(source, startOffset);
            return true;
        }

        return false;
    }

    public static (int Line, int Column) GetLineColumn(SourceCode sourceCode, int position)
    {
        if (sourceCode.Source is null)
            return (0, 0);

        var clamped = Math.Clamp(position, 0, sourceCode.Source.Length);
        var lineStarts = sourceCode.GetOrCreateLineStarts();
        var index = Array.BinarySearch(lineStarts, clamped);
        if (index < 0)
            index = ~index - 1;
        if (index < 0)
            index = 0;

        var lineStart = lineStarts[index];
        return (index + 1, clamped - lineStart + 1);
    }

    public static (int Line, int Column) GetLineColumn(string source, int position)
    {
        var clamped = Math.Clamp(position, 0, source.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < clamped; i++)
            if (source[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        return (line, clamped - lineStart + 1);
    }

    private static int FindLineEnd(string source, int startOffset)
    {
        for (var i = startOffset; i < source.Length; i++)
            if (source[i] == '\n')
                return i;

        return source.Length;
    }
}
