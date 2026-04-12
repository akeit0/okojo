using Okojo.RegExp.Experimental;

namespace Okojo.Tests;

public class RegExpExperimentalIncrementalTests
{
    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesRequiredLiteralPrefixAfterLookahead()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"(?=foo)foo\d+", "");

        var match = engine.Exec(compiled, "xxfoo42", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("foo42"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_KeepsVariableQuantifierPrefixesCorrect()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"a+b", "");

        var match = engine.Exec(compiled, "caaab", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Groups[0], Is.EqualTo("aaab"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_DoesNotInventOptionalPrefix()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"a?b", "");

        var match = engine.Exec(compiled, "cb", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Groups[0], Is.EqualTo("b"));
    }
}
