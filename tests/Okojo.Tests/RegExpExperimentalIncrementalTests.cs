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

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesExactLiteralFastPath()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile("literal", "");

        var match = engine.Exec(compiled, "xxliteralyy", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("literal"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesExactLiteralFastPathForFixedRepeat()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"(?:ab){2}", "");

        var match = engine.Exec(compiled, "zzabab", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("abab"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesExactLiteralFastPathForUnicodeLiteral()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"\ud834\udf06", "u");

        var match = engine.Exec(compiled, "z𝌆z", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Groups[0], Is.EqualTo("𝌆"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesBytecodeLiteralPathForIgnoreCase()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile("foo", "i");

        var match = engine.Exec(compiled, "xxFOOyy", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("FOO"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_BoundsExactLiteralExpansion()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile("b{1000000}", "");

        var match = engine.Exec(compiled, "bbb", 0);

        Assert.That(match, Is.Null);
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesLinearBytecodeForAnchorsClassAndDot()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"^[ab].$", "");

        var match = engine.Exec(compiled, "a!", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(0));
        Assert.That(match.Groups[0], Is.EqualTo("a!"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesLinearBytecodeForWordBoundary()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"\bfoo", "");

        var match = engine.Exec(compiled, " foo", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Groups[0], Is.EqualTo("foo"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesLinearBytecodeForPropertyEscape()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"^\p{ASCII}$", "u");

        var match = engine.Exec(compiled, "A", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(0));
        Assert.That(match.Groups[0], Is.EqualTo("A"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesIrVmAlternationBacktracking()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"a(?:bc|b)d", "");

        var match = engine.Exec(compiled, "zzabd", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("abd"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesIrVmGreedyQuantifier()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"ab*c", "");

        var match = engine.Exec(compiled, "zzabbbc", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("abbbc"));
    }

    [Test]
    public void ExperimentalRegExpEngine_Incremental_UsesIrVmLazyQuantifier()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"a.+?c", "");

        var match = engine.Exec(compiled, "zzaXcYc", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("aXc"));
    }
}
