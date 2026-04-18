using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AsyncFunctionExpressionTests
{
    [Test]
    public void AsyncFunctionExpression_Call_ReturnsPromiseInstance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var p = async function() { }();
                                                                   p instanceof Promise;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunctionExpression_WithStaCompatAssert_Passes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function Test262Error(message) { this.message = message || ""; }
                                                                   function assert(mustBeTrue, message) {
                                                                       if (!mustBeTrue) throw new Test262Error(message || "Expected true");
                                                                   }
                                                                   var p = async function() { }();
                                                                   assert(p instanceof Promise, "async functions return promise instances");
                                                                   1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncArrowExpression_ChainedAwaitExpressionBody_Resolves_Final_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   globalThis.done = false;
                                                                   var p = (async () => await 1 + await 2)();
                                                                   p.then(function (v) {
                                                                     globalThis.out = v;
                                                                     globalThis.done = true;
                                                                   }, function (e) {
                                                                     globalThis.out = -1;
                                                                     globalThis.done = true;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["done"].IsTrue, Is.True);
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(3));
    }
}
