using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

[TestFixture]
public sealed class NumberPrototypeTests
{
    [TestCase("(2.5).toFixed(0)", "3")]
    [TestCase("(-2.5).toFixed(0)", "-3")]
    [TestCase("(1.25).toFixed(1)", "1.3")]
    [TestCase("(1.35).toFixed(1)", "1.4")]
    [TestCase("(2.55).toFixed(1)", "2.5")]
    [TestCase("(-0).toFixed(3)", "0.000")]
    [TestCase("(-0.001).toFixed(2)", "-0.00")]
    [TestCase("(125).toPrecision(2)", "1.3e+2")]
    [TestCase("(121).toPrecision(2)", "1.2e+2")]
    [TestCase("(99.5).toPrecision(2)", "1.0e+2")]
    [TestCase("(1e-6).toPrecision(3)", "0.00000100")]
    [TestCase("(-1e-7).toPrecision(3)", "-1.00e-7")]
    [TestCase("(4.5).toExponential(0)", "5e+0")]
    [TestCase("(3.5).toExponential(0)", "4e+0")]
    [TestCase("(-0).toExponential(2)", "0.00e+0")]
    [TestCase("(1.0000000000000002).toExponential()", "1.0000000000000002e+0")]
    [TestCase("Number.MIN_VALUE.toExponential(undefined)", "5e-324")]
    [TestCase("Number.MAX_VALUE.toExponential()", "1.7976931348623157e+308")]
    [TestCase("(2 ** -25).toExponential()", "2.9802322387695312e-8")]
    [TestCase("(-(2 ** -25)).toString()", "-2.9802322387695312e-8")]
    [TestCase("(2 ** 60).toExponential()", "1.152921504606847e+18")]
    [TestCase("(2 ** -1022).toExponential()", "2.2250738585072014e-308")]
    public void NumberPrototype_Formatting_Uses_Exact_Decimal_Rounding(
        string expression,
        string expected
    )
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.DefaultRealm;
        realm.Execute(expression);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(expected));
    }

    [Test]
    public void NumberPrototype_Formatting_Coerces_Before_Checking_Nonfinite_Values()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.DefaultRealm;
        realm.Execute(
            """
            let calls = 0;
            const digits = { valueOf() { calls++; return Infinity; } };
            let fixed;
            try { fixed = NaN.toFixed(digits); }
            catch (e) { fixed = e.name; }
            [fixed, NaN.toExponential(digits), Infinity.toPrecision(digits), calls].join("|");
            """
        );
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("RangeError|NaN|Infinity|3"));
    }

    [Test]
    public void NumberPrototype_Has_Formatting_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                Number.prototype.hasOwnProperty("toFixed") &&
                Number.prototype.hasOwnProperty("toExponential") &&
                Number.prototype.hasOwnProperty("toPrecision");
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Are_Not_Constructors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let ok = 0;
                try { new Number.prototype.toFixed(); } catch (e) { if (e && e.name === "TypeError") ok++; }
                try { new Number.prototype.toExponential(); } catch (e) { if (e && e.name === "TypeError") ok++; }
                try { new Number.prototype.toPrecision(); } catch (e) { if (e && e.name === "TypeError") ok++; }
                ok === 3;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Produce_Basic_Results()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                [
                  (10).toString(16),
                  (123.456).toExponential(2),
                  (123.456).toFixed(2),
                  (123.456).toPrecision(4),
                  (1).toExponential(),
                  (1).toFixed(),
                  (1).toPrecision()
                ].join("|");
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("a|1.23e+2|123.46|123.5|1e+0|1|1"));
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Handle_NumberPrototype_Test262_Repros()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                [
                  (25).toExponential(0),
                  (0.0001).toExponential(16),
                  (123.456).toExponential(20),
                  (new Number(1e21)).toFixed(),
                  (10).toPrecision(1),
                  (3).toPrecision(100)
                ].join("|");
                """
            )
        );

        realm.Execute(script);
        Assert.That(
            realm.Accumulator.ToString(),
            Is.EqualTo(
                "3e+1|1.0000000000000000e-4|1.23456000000000003070e+2|1e+21|1e+1|3.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
            )
        );
    }

    [Test]
    public void NumberPrototype_ToPrecision_Coerces_Precision_Before_NaN_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let calls = 0;
                const p = { valueOf() { calls++; return Infinity; } };
                [NaN.toPrecision(p), calls].join("|");
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("NaN|1"));
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Handle_NaN_And_Infinity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                Number.NaN.toExponential() === "NaN" &&
                Number.POSITIVE_INFINITY.toExponential() === "Infinity";
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NumberPrototype_Formatting_Methods_Validate_Ranges()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let ok = 0;
                try { (1).toExponential(-1); } catch (e) { if (e && e.name === "RangeError") ok++; }
                try { (1).toExponential(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
                try { (1).toFixed(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
                try { (1).toPrecision(0); } catch (e) { if (e && e.name === "RangeError") ok++; }
                try { (1).toPrecision(101); } catch (e) { if (e && e.name === "RangeError") ok++; }
                ok === 5;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
