namespace Okojo.Text.Unicode.Tests;

[TestFixture]
public class Utf16Tests
{
    [TestCase(0x41, false)]
    [TestCase(0xD800, true)]
    [TestCase(0xDBFF, true)]
    [TestCase(0xDC00, false)]
    [TestCase(0xFFFF, false)]
    public void IsHighSurrogate_IdentifiesHighRange(int value, bool expected)
    {
        Assert.That(Utf16.IsHighSurrogate(value), Is.EqualTo(expected));
    }

    [TestCase(0xDC00, true)]
    [TestCase(0xDFFF, true)]
    [TestCase(0xD800, false)]
    [TestCase(0x41, false)]
    public void IsLowSurrogate_IdentifiesLowRange(int value, bool expected)
    {
        Assert.That(Utf16.IsLowSurrogate(value), Is.EqualTo(expected));
    }

    [Test]
    public void CombineSurrogates_ProducesAstralCodePoint()
    {
        Assert.That(Utf16.CombineSurrogates(0xD83D, 0xDE00), Is.EqualTo(0x1F600));
    }

    [TestCase(0x41, 1)]
    [TestCase(0x1F600, 2)]
    public void CodeUnitLength_ReflectsAstralWidth(int codePoint, int expected)
    {
        Assert.That(Utf16.CodeUnitLength(codePoint), Is.EqualTo(expected));
    }

    [Test]
    public void TryReadForward_HandlesSurrogatePair()
    {
        var input = "a😀b";
        Assert.That(
            Utf16.TryReadForward(input, 1, unicode: true, out var cp, out var width),
            Is.True
        );
        Assert.Multiple(() =>
        {
            Assert.That(cp, Is.EqualTo(0x1F600));
            Assert.That(width, Is.EqualTo(2));
        });
    }

    [Test]
    public void TryReadForward_NonUnicode_ReadsSingleUnit()
    {
        var input = "a😀b";
        Assert.That(
            Utf16.TryReadForward(input, 1, unicode: false, out var cp, out var width),
            Is.True
        );
        Assert.Multiple(() =>
        {
            Assert.That(cp, Is.EqualTo(0xD83D));
            Assert.That(width, Is.EqualTo(1));
        });
    }

    [Test]
    public void TryReadBackward_HandlesSurrogatePair()
    {
        var input = "a😀b";
        Assert.That(
            Utf16.TryReadBackward(input, 3, unicode: true, out var cp, out var width),
            Is.True
        );
        Assert.Multiple(() =>
        {
            Assert.That(cp, Is.EqualTo(0x1F600));
            Assert.That(width, Is.EqualTo(2));
        });
    }

    [Test]
    public void AdvanceStringIndex_SkipsPairInUnicodeMode()
    {
        var input = "a😀b";
        Assert.That(Utf16.AdvanceStringIndex(input, 1, unicode: true), Is.EqualTo(3));
        Assert.That(Utf16.AdvanceStringIndex(input, 1, unicode: false), Is.EqualTo(2));
    }

    [TestCase("", 0)]
    [TestCase("abc", 3)]
    [TestCase("a😀b", 3)]
    public void CountCodePoints_CountsCodePoints(string value, int expected)
    {
        Assert.That(Utf16.CountCodePoints(value), Is.EqualTo(expected));
    }

    [TestCase('\n', true)]
    [TestCase('\r', true)]
    [TestCase('\u2028', true)]
    [TestCase('\u2029', true)]
    [TestCase('a', false)]
    [TestCase(' ', false)]
    public void IsLineTerminator_MatchesEcmaLineTerminators(int codePoint, bool expected)
    {
        Assert.That(Utf16.IsLineTerminator(codePoint), Is.EqualTo(expected));
    }

    [TestCase('a', true)]
    [TestCase('Z', true)]
    [TestCase('0', true)]
    [TestCase('_', true)]
    [TestCase('-', false)]
    [TestCase(' ', false)]
    public void IsAsciiWord_MatchesAsciiWordCharacters(int codePoint, bool expected)
    {
        Assert.That(Utf16.IsAsciiWord(codePoint), Is.EqualTo(expected));
    }
}

[TestFixture]
public class UnicodeCaseFoldingTests
{
    [TestCase(0x41, 0x41)]
    [TestCase(0x61, 0x41)]
    [TestCase(0x1F600, 0x1F600)]
    [TestCase(0x212A, 0x4B)]
    [TestCase(0x17F, 0x53)]
    public void CanonicalizeUnicode_FoldsCase(int codePoint, int expected)
    {
        Assert.That(UnicodeCaseFolding.CanonicalizeUnicode(codePoint), Is.EqualTo(expected));
    }

    [TestCase(0x41, 0x61, true)]
    [TestCase(0x61, 0x62, false)]
    [TestCase(0x1F600, 0x1F601, false)]
    public void EqualsUnicode_ComparesByFold(int left, int right, bool expected)
    {
        Assert.That(UnicodeCaseFolding.EqualsUnicode(left, right), Is.EqualTo(expected));
    }

    [TestCase(0x41, 0x41)]
    [TestCase(0xDF, 0xDF)]
    [TestCase(0x61, 0x41)]
    public void CanonicalizeLegacy_KeepsNonAsciiMappings(int codePoint, int expected)
    {
        Assert.That(UnicodeCaseFolding.CanonicalizeLegacy(codePoint), Is.EqualTo(expected));
    }

    [TestCase(0x61, 0x41)]
    [TestCase(0x1F600, 0x1F600)]
    public void ToUpperInvariant_MapsCodePoint(int codePoint, int expected)
    {
        Assert.That(UnicodeCaseFolding.ToUpperInvariant(codePoint), Is.EqualTo(expected));
    }

    [TestCase(0x41, 0x61)]
    [TestCase(0x1F600, 0x1F600)]
    public void ToLowerInvariant_MapsCodePoint(int codePoint, int expected)
    {
        Assert.That(UnicodeCaseFolding.ToLowerInvariant(codePoint), Is.EqualTo(expected));
    }

    [Test]
    public void TryGetEquivalents_ReportsAsciiRange()
    {
        Assert.That(UnicodeCaseFolding.TryGetEquivalents(0x41, out _, out var count), Is.True);
        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }
}
