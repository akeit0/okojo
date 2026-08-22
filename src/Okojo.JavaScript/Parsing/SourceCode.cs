namespace Okojo.Parsing;

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

        var source = Source;
        var lineCount = 1;
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n')
                lineCount++;

        var starts = new int[lineCount];
        starts[0] = 0;
        var nextLine = 1;
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n')
                starts[nextLine++] = i + 1;

        lineStarts = starts;
        return starts;
    }
}
