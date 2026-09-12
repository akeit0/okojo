using Okojo.JavaScript.SourceMaps;

namespace Okojo.Tests;

public class SourceMapDocumentTests
{
    private static SourceMapDocument BuildDocument(List<SourceMapEntry> entries)
    {
        return new SourceMapDocument("gen.js", entries, null);
    }

    private static SourceMapEntry Entry(
        int generatedColumn,
        int originalLine,
        int originalColumn = 0
    )
    {
        return new SourceMapEntry(1, generatedColumn, "orig.ts", originalLine, originalColumn);
    }

    [Test]
    public void MapToOriginal_Selects_Last_Entry_At_Or_Before_Column()
    {
        var document = BuildDocument([Entry(0, 1), Entry(10, 2), Entry(20, 3)]);

        Assert.That(document.TryMapToOriginal(1, 0, out var atZero), Is.True);
        Assert.That(atZero.Line, Is.EqualTo(1));

        Assert.That(document.TryMapToOriginal(1, 15, out var between), Is.True);
        Assert.That(between.Line, Is.EqualTo(2));

        Assert.That(document.TryMapToOriginal(1, 20, out var exact), Is.True);
        Assert.That(exact.Line, Is.EqualTo(3));

        Assert.That(document.TryMapToOriginal(1, 10_000, out var pastEnd), Is.True);
        Assert.That(pastEnd.Line, Is.EqualTo(3));
    }

    [Test]
    public void MapToOriginal_Fails_Before_First_Entry_And_On_Missing_Line()
    {
        var document = BuildDocument([Entry(10, 2)]);

        Assert.That(document.TryMapToOriginal(1, 9, out _), Is.False);
        Assert.That(document.TryMapToOriginal(2, 0, out _), Is.False);
    }

    [Test]
    public void MapToOriginal_Last_Duplicate_Column_Wins()
    {
        var document = BuildDocument([Entry(10, 2), Entry(10, 3)]);

        Assert.That(document.TryMapToOriginal(1, 10, out var location), Is.True);
        Assert.That(location.Line, Is.EqualTo(3));
    }

    [Test]
    public void MapToOriginal_Normalizes_Unsorted_Input_Order()
    {
        var document = BuildDocument([Entry(20, 3), Entry(0, 1), Entry(10, 2)]);

        Assert.That(document.TryMapToOriginal(1, 15, out var location), Is.True);
        Assert.That(location.Line, Is.EqualTo(2));
    }
}
