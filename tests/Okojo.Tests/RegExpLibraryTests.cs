using Okojo.Text.RegularExpressions;

namespace Okojo.Tests;

public class RegExpLibraryTests
{
    [Test]
    public void PublicApiCompilesAndMatches()
    {
        var regexp = RegExp.Compile("(?<value>a)", RegExpFlags.Global);
        Span<CaptureRange> captures = stackalloc CaptureRange[regexp.RequiredCaptureCount];

        Assert.That(regexp.TryMatch("ba", captures, out var range), Is.True);
        Assert.That(range, Is.EqualTo(new MatchRange(1, 1)));
        Assert.That(captures[1], Is.EqualTo(new CaptureRange(1, 1)));
        Assert.That(regexp.GetCaptureIndices("value").ToArray(), Is.EqualTo(new[] { 1 }));

        var result = regexp.Match("ba");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GetGroupValue("value"), Is.EqualTo("a"));

        foreach (var match in regexp.EnumerateMatches("ba"))
        {
            Assert.That(match.Range, Is.EqualTo(range));
            Assert.That(match.GetGroupValue(1).ToString(), Is.EqualTo("a"));
        }
    }
}
