using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class SourceCodeLineStartsTests
{
    private const string MixedSource = "a = 1;\r\nb = 2;\u2028c = 3;\rd = 4;";

    [Test]
    public void LineIndex_Recognizes_Crlf_As_Single_Terminator()
    {
        var code = new SourceCode(MixedSource, null);

        // `b` follows CRLF: line 2, not line 3.
        Assert.That(SourceLocation.GetLineColumn(code, 8), Is.EqualTo((2, 1)));
    }

    [Test]
    public void LineIndex_Recognizes_Line_And_Paragraph_Separators()
    {
        var code = new SourceCode(MixedSource, null);

        // `c` follows U+2028: line 3.
        Assert.That(SourceLocation.GetLineColumn(code, 15), Is.EqualTo((3, 1)));
    }

    [Test]
    public void LineIndex_Recognizes_Lone_Carriage_Return()
    {
        var code = new SourceCode(MixedSource, null);

        // `d` follows a lone CR: line 4.
        Assert.That(SourceLocation.GetLineColumn(code, 22), Is.EqualTo((4, 1)));
        Assert.That(
            SourceLocation.GetLineColumn(new SourceCode("a\rb", null), 2),
            Is.EqualTo((2, 1))
        );
    }

    [Test]
    public void LineIndex_Keeps_Lf_Behavior_Unchanged()
    {
        var code = new SourceCode("a\nb", null);

        Assert.That(SourceLocation.GetLineColumn(code, 0), Is.EqualTo((1, 1)));
        Assert.That(SourceLocation.GetLineColumn(code, 2), Is.EqualTo((2, 1)));
    }

    [Test]
    public void String_Overload_Agrees_With_Line_Index()
    {
        Assert.That(SourceLocation.GetLineColumn(MixedSource, 8), Is.EqualTo((2, 1)));
        Assert.That(SourceLocation.GetLineColumn(MixedSource, 15), Is.EqualTo((3, 1)));
        Assert.That(SourceLocation.GetLineColumn(MixedSource, 22), Is.EqualTo((4, 1)));
        Assert.That(SourceLocation.GetLineColumn("a\r\nb", 3), Is.EqualTo((2, 1)));
    }
}
