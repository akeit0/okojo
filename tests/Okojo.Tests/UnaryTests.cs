using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class UnaryTests
{
    [Test]
    public void UnaryVoid_Zero_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   void 0 === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryVoid_EvaluatesOperandSideEffects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x = 0;
                                                                   var y = void (x = 3);
                                                                   x === 3 && y === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryMinus_Zero_PreservesNegativeZero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (1 / (-0)) === Number.NEGATIVE_INFINITY;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryMinus_Applies_ToNumeric_To_Primitives()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   -false === 0 && -"1" === -1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryMinus_Applies_ToNumeric_To_BigInt_Wrappers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   -Object(1n) === -1n &&
                                                                   -({
                                                                     [Symbol.toPrimitive]: function() { return 1n; },
                                                                     valueOf: function() { throw new Error("valueOf should not run"); },
                                                                     toString: function() { throw new Error("toString should not run"); }
                                                                   }) === -1n;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryBitwiseNot_Applies_ToNumeric_To_BigInt_Wrappers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ~Object(1n) === -2n &&
                                                                   ~({
                                                                     [Symbol.toPrimitive]: function() { return 1n; },
                                                                     valueOf: function() { throw new Error("valueOf should not run"); },
                                                                     toString: function() { throw new Error("toString should not run"); }
                                                                   }) === -2n;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
