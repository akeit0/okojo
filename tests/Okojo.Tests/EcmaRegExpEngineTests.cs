using Okojo.RegExp;
using Okojo.Runtime;

namespace Okojo.Tests;

public class EcmaRegExpEngineTests
{
    private static JsRuntime CreateRuntime() => JsRuntime.Create();

    [Test]
    public void CompilesAndExecutesBasicPattern()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile(@"(a)(b)?", "g");

        var match = engine.Exec(compiled, "zabz", 1);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Length, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("ab"));
        Assert.That(match.Groups[1], Is.EqualTo("a"));
        Assert.That(match.Groups[2], Is.EqualTo("b"));

        var named = engine.Compile(@"(?<name>a)", "");
        var namedMatch = engine.Exec(named, "za", 0);
        Assert.That(namedMatch, Is.Not.Null);
        Assert.That(namedMatch!.NamedGroups, Is.Not.Null);
        Assert.That(namedMatch.NamedGroups!["name"], Is.EqualTo("a"));
    }

    [Test]
    public void CanonicalizesFlags()
    {
        var engine = RegExpEngine.Default;
        var compiled = engine.Compile("a", "gimsyud");
        Assert.That(compiled.Flags, Is.EqualTo("dgimsuy"));

        var unicodeSets = engine.Compile("a", "v");
        Assert.That(unicodeSets.Flags, Is.EqualTo("v"));
        Assert.That(unicodeSets.ParsedFlags.UnicodeSets, Is.True);
        Assert.That(unicodeSets.ParsedFlags.Unicode, Is.True);
    }

    [Test]
    public void RejectsInvalidPatternsAsArgumentException()
    {
        var engine = RegExpEngine.Default;

        Assert.That(() => engine.Compile(@"(?I:a)", ""), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => engine.Compile("\\", ""), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => engine.Compile("[d-G\\a]", ""), Throws.InstanceOf<ArgumentException>());
        Assert.That(() => engine.Compile("a", "uuv"), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void HonorsUnicodeCaseFoldEdges()
    {
        var engine = RegExpEngine.Default;

        var compiled = engine.Compile(@"[\u0390]", "ui");
        var match = engine.Exec(compiled, "\u1fd3", 0);
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Groups[0], Is.EqualTo("\u1fd3"));

        Assert.That(engine.Exec(engine.Compile(@"[\ufb05]", "ui"), "\ufb06", 0), Is.Not.Null);
    }

    [Test]
    public void CombinesEscapedSurrogatePairsInUnicodeMode()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"\ud834\udf06", "u"), "𝌆", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"[\ud834\udf06]", "u"), "𝌆", 0), Is.Not.Null);
    }

    [Test]
    public void MatchesLookaheadBackreferenceRegression()
    {
        var engine = RegExpEngine.Default;
        var match = engine.Exec(engine.Compile(@"(.*?)a(?!(a+)b\2c)\2(.*)", ""), "baaabaac", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(0));
        Assert.That(match.Groups[0], Is.EqualTo("baaabaac"));
        Assert.That(match.Groups[1], Is.EqualTo("ba"));
        Assert.That(match.Groups[2], Is.Null);
        Assert.That(match.Groups[3], Is.EqualTo("abaac"));
    }

    [Test]
    public void SupportsDuplicateNamedGroupsAcrossAlternatives()
    {
        var engine = RegExpEngine.Default;
        var match = engine.Exec(engine.Compile(@"(?<x>a)|(?<x>b)", ""), "bab", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Groups[1], Is.Null);
        Assert.That(match.Groups[2], Is.EqualTo("b"));
        Assert.That(match.NamedGroups!["x"], Is.EqualTo("b"));
    }

    [Test]
    public void SupportsLookbehindAndIndices()
    {
        var realm = CreateRuntime().DefaultRealm;

        Assert.That(
            realm
                .Eval(
                    """
                    const m = /(?<x>a)(b)?/d.exec("za");
                    Array.isArray(m.indices)
                      && m.indices[0][0] === 1
                      && m.indices[0][1] === 2
                      && m.indices.groups.x[0] === 1
                      && m.indices.groups.x[1] === 2
                      && m.indices[2] === undefined;
                    """
                )
                .IsTrue,
            Is.True
        );

        Assert.That(
            realm
                .Eval(
                    """
                    const m = "abcdef".match(/(?<=(\w{2}))def/);
                    m && m[0] === "def" && m[1] === "bc";
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void HonorsGlobalLastIndexStateMachine()
    {
        var realm = CreateRuntime().DefaultRealm;

        Assert.That(
            realm
                .Eval(
                    """
                    const re = new RegExp("a+", "g");
                    const m1 = re.exec("baaa");
                    const li1 = re.lastIndex;
                    const m2 = re.exec("baaa");
                    const li2 = re.lastIndex;
                    m1[0] === "aaa" && li1 === 4 && m2 === null && li2 === 0;
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void SupportsUnicodeSetClassOperationsAndStringProperties()
    {
        var realm = CreateRuntime().DefaultRealm;

        Assert.That(
            realm
                .Eval(
                    """
                    /^[[0-9]&&\d]+$/v.test("4") &&
                    /^[_--\q{0|2|4|9\uFE0F\u20E3}]+$/v.test("_") &&
                    /^\p{Emoji_Keycap_Sequence}+$/v.test("0\uFE0F\u20E3");
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void SupportsUnicodePropertyFrontier()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"\p{AHex}+", "u"), "A9f", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"\p{Assigned}+", "u"), "\uDFFF", 0), Is.Not.Null);
        Assert.That(
            engine.Exec(engine.Compile(@"\p{Script=Adlm}+", "u"), "\ud83a\udd00", 0),
            Is.Not.Null
        );
        Assert.That(engine.Exec(engine.Compile(@"\P{Bidi_M}+", "u"), "ABC", 0), Is.Not.Null);
    }

    [Test]
    public void SupportsScopedModifiers()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"(?i:\x61)b", ""), "Ab", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"(?i:\P{Lu})", "u"), "A", 0), Is.Not.Null);
    }

    [Test]
    public void SupportsControlLetterEscapesAndEmptyMatches()
    {
        var engine = RegExpEngine.Default;

        Assert.That(engine.Exec(engine.Compile(@"\cA", ""), "\u0001", 0), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"a*", "g"), "", 0)!.Length, Is.EqualTo(0));
    }

    [Test]
    public void SupportsStickyAndZeroWidthBoundary()
    {
        var engine = RegExpEngine.Default;

        var compiled = engine.Compile(@"\bfoo", "");
        var match = engine.Exec(compiled, " foo", 0);
        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));

        Assert.That(engine.Exec(engine.Compile(@"c", "y"), "abc", 2), Is.Not.Null);
        Assert.That(engine.Exec(engine.Compile(@"c", "y"), "abc", 1), Is.Null);
    }
}
