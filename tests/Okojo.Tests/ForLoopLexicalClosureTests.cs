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

    [Test]
    public void NestedForLet_Can_Read_Outer_Loop_Head_When_Body_Uses_PerIteration_Closures()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let seen = [];
                                                                   for (let i = 0; i < 2; ++i) {
                                                                     for (let j = i + 1; j < 4; ++j) {
                                                                       let captured = j * 10;
                                                                       const readCaptured = () => captured;
                                                                       for (let k = j; k < 4; ++k) {
                                                                         seen.push(`${j}:${k}:${readCaptured()}`);
                                                                       }
                                                                     }
                                                                   }
                                                                   seen.join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("1:1:10|1:2:10|1:3:10|2:2:20|2:3:20|3:3:30|2:2:20|2:3:20|3:3:30"));
    }

    [Test]
    public void NestedForLet_InnerLoopHead_Initializes_Current_Context_Before_PerIteration_Clone()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let seen = [];
                                                                   for (let i = 0; i < 2; ++i) {
                                                                     for (let j = i + 1; j < 4; ++j) {
                                                                       let captured = j * 10;
                                                                       const readCaptured = () => captured;
                                                                       seen.push(`${j}:${readCaptured()}`);
                                                                     }
                                                                   }
                                                                   seen.join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("1:10|2:20|3:30|2:20|3:30"));
    }
}
