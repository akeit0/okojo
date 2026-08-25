using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ArrowParameterCoverTests
{
    [Test]
    public void ArrowParameters_ObjectBindingWithDefault_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                var af = ({x = 1}) => x;
                (typeof af === "function") && af({}) === 1 && af({x: 2}) === 2;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowParameters_WithRest_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                var af = (x, ...rest) => x + ":" + rest.length + ":" + rest[0];
                af("a", "b", "c") === "a:2:b";
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowParameters_MixedWithObjectBinding_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                var af = (key, val, { source }) => key + ":" + val + ":" + source;
                af("k", 3, { source: 44 }) === "k:3:44";
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowParameters_ObjectBindingAlias_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                var af = ({ value: imported }) => imported.x;
                af({ value: { x: 7 } }) === 7;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Function_WithRest_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                function f(a, ...rest) { return rest.length === 2 && rest[1] === 3; }
                f(1, 2, 3);
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
