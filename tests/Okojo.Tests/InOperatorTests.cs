using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class InOperatorTests
{
    [Test]
    public void InOperator_NamedOwnProperty_IsTrue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   "x" in { x: 1 };
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InOperator_PrototypeProperty_IsTrue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   "toString" in {};
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InOperator_ArrayIndex_UsesElementSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (0 in [1]) ? (1 in [1]) : true;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsFalse, Is.True);
    }

    [Test]
    public void InOperator_SymbolKey_IsTrueWhenPresent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const s = Symbol();
                                                                   const o = {};
                                                                   o[s] = 1;
                                                                   s in o;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InOperator_NonString_Primitives_Are_Normalized_To_PropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var a = {};
                                a["true"] = 1;
                                a.Infinity = 1;
                                a.undefined = 1;
                                a["null"] = 1;
                                (true in a) &&
                                (Infinity in a) &&
                                (undefined in a) &&
                                (null in a);
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void InOperator_NonObjectRhs_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   "x" in 1;
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("IN_RHS_NOT_OBJECT"));
    }
}
