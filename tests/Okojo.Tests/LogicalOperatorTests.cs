using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class LogicalOperatorTests
{
    [Test]
    public void LogicalAnd_ReturnsLeftWhenFalsy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                0 && 5;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void LogicalAnd_ReturnsRightWhenLeftTruthy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                1 && 5;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void LogicalOr_ReturnsLeftWhenTruthy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                7 || 9;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void LogicalOr_ReturnsRightWhenLeftFalsy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                0 || 9;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void LogicalAnd_ShortCircuitsRightSide()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let c = 0;
                function bump() { c = c + 1; return 1; }
                0 && bump();
                c;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void LogicalAnd_PreservesNegativeZero_WhenLeftIsMinusZero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                (1 / (-0 && -1)) === Number.NEGATIVE_INFINITY;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LogicalOr_ShortCircuitsRightSide()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let c = 0;
                function bump() { c = c + 1; return 1; }
                1 || bump();
                c;
                """
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }
}
