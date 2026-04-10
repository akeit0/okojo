using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ForLoopLexicalClosureTests
{
    [Test]
    public void ForLet_ClosureInsideCondition_CapturesPerIterationValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let a = [];
                                                                   for (let i = 0; a.push(function () { return i; }), i < 5; ++i) { }
                                                                   let ok = true;
                                                                   for (let k = 0; k < 5; ++k) {
                                                                     if (k !== a[k]()) ok = false;
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_PerIterationContext_Does_Not_Shadow_Outer_Captured_Lexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let values;
                                                                   function readValues() { return values.length; }
                                                                   let ok = true;
                                                                   for (let ctor of [1, 2]) {
                                                                     values = [];
                                                                     if (readValues() !== 0) ok = false;
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
