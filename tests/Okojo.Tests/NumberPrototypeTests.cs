using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

[TestFixture]
public sealed class NumberPrototypeTests
{
    [Test]
    public void NumberPrototype_Has_Formatting_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Number.prototype.hasOwnProperty("toFixed") &&
            Number.prototype.hasOwnProperty("toExponential") &&
            Number.prototype.hasOwnProperty("toPrecision");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Are_Not_Constructors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ok = 0;
            try { new Number.prototype.toFixed(); } catch (e) { if (e && e.name === "TypeError") ok++; }
            try { new Number.prototype.toExponential(); } catch (e) { if (e && e.name === "TypeError") ok++; }
            try { new Number.prototype.toPrecision(); } catch (e) { if (e && e.name === "TypeError") ok++; }
            ok === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Produce_Basic_Results()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [
              (10).toString(16),
              (123.456).toExponential(2),
              (123.456).toFixed(2),
              (123.456).toPrecision(4),
              (1).toExponential(),
              (1).toFixed(),
              (1).toPrecision()
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("a|1.23e+2|123.46|123.5|1e+0|1|1"));
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Handle_NumberPrototype_Test262_Repros()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [
              (25).toExponential(0),
              (0.0001).toExponential(16),
              (123.456).toExponential(20),
              (new Number(1e21)).toFixed(),
              (10).toPrecision(1),
              (3).toPrecision(100)
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo(
            "3e+1|1.0000000000000000e-4|1.23456000000000003070e+2|1e+21|1e+1|3.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"));
    }

    [Test]
    public void NumberPrototype_ToPrecision_Coerces_Precision_Before_NaN_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let calls = 0;
            const p = { valueOf() { calls++; return Infinity; } };
            [NaN.toPrecision(p), calls].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("NaN|1"));
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Handle_NaN_And_Infinity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Number.NaN.toExponential() === "NaN" &&
            Number.POSITIVE_INFINITY.toExponential() === "Infinity";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Validate_Ranges()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ok = 0;
            try { (1).toExponential(-1); } catch (e) { if (e && e.name === "RangeError") ok++; }
            try { (1).toExponential(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
            try { (1).toFixed(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
            try { (1).toPrecision(0); } catch (e) { if (e && e.name === "RangeError") ok++; }
            try { (1).toPrecision(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
            ok === 5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
