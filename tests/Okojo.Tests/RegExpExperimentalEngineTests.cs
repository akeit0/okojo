using Okojo.RegExp.Experimental;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpExperimentalEngineTests
{
    [Test]
    public void ExperimentalRegExpEngine_CompilesAndExecutes()
    {
        var engine = ExperimentalRegExpEngine.Default;
        var compiled = engine.Compile(@"(a)(b)?", "g");

        var match = engine.Exec(compiled, "zabz", 1);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Index, Is.EqualTo(1));
        Assert.That(match.Length, Is.EqualTo(2));
        Assert.That(match.Groups[0], Is.EqualTo("ab"));
        Assert.That(match.Groups[1], Is.EqualTo("a"));
        Assert.That(match.Groups[2], Is.EqualTo("b"));
    }

    [Test]
    public void ExperimentalRegExpEngine_CompilesUnicodeCaseFoldSample()
    {
        var engine = ExperimentalRegExpEngine.Default;

        var compiled = engine.Compile(@"[\u0390]", "ui");
        var match = engine.Exec(compiled, "\u1fd3", 0);

        Assert.That(match, Is.Not.Null);
        Assert.That(match!.Groups[0], Is.EqualTo("\u1fd3"));
    }

    [Test]
    public void OkojoCanUseExperimentalRegExpEngineForJsSemantics()
    {
        using var runtime = JsRuntime.CreateBuilder()
            .UseRegExpEngine(ExperimentalRegExpEngine.Default)
            .Build();
        var realm = runtime.DefaultRealm;

        Assert.That(realm.Eval("""
                               const re = new RegExp("a+", "g");
                               const m1 = re.exec("baaa");
                               const li1 = re.lastIndex;
                               const m2 = re.exec("baaa");
                               const li2 = re.lastIndex;
                               m1[0] === "aaa" && li1 === 4 && m2 === null && li2 === 0;
                               """).IsTrue, Is.True);
    }
}
