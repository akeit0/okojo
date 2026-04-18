using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class MathBuiltinsTests
{
    [Test]
    public void Math_Surface_And_Descriptors_Are_Installed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(realm.Global["Math"].TryGetObject(out var math), Is.True);

        string[] functionNames =
        [
            "abs", "acos", "acosh", "asin", "asinh", "atan", "atan2", "atanh", "cbrt", "ceil", "clz32", "cos", "cosh",
            "exp", "expm1", "f16round", "floor", "fround", "hypot", "imul", "log", "log1p", "log2", "log10", "max",
            "min",
            "pow", "random", "round", "sign", "sin", "sinh", "sqrt", "sumPrecise", "tan", "tanh", "trunc"
        ];

        foreach (var name in functionNames)
        {
            var atom = realm.Atoms.InternNoCheck(name);
            Assert.That(math!.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor), Is.True, name);
            Assert.That(descriptor.Value.TryGetObject(out _), Is.True, name);
            Assert.That(descriptor.Writable, Is.True, name);
            Assert.That(descriptor.Enumerable, Is.False, name);
            Assert.That(descriptor.Configurable, Is.True, name);
        }

        string[] constantNames = ["E", "LN2", "LN10", "LOG2E", "LOG10E", "PI", "SQRT1_2", "SQRT2"];
        foreach (var name in constantNames)
        {
            var atom = realm.Atoms.InternNoCheck(name);
            Assert.That(math!.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor), Is.True, name);
            Assert.That(descriptor.Value.IsNumber, Is.True, name);
            Assert.That(descriptor.Writable, Is.False, name);
            Assert.That(descriptor.Enumerable, Is.False, name);
            Assert.That(descriptor.Configurable, Is.False, name);
        }
    }

    [Test]
    public void Math_Numeric_Behavior_Matches_Expected_Results()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const approx = function (a, b, e) { return Math.abs(a - b) <= (e === undefined ? 1e-12 : e); };
            approx(Math.abs(-2), 2) &&
            approx(Math.acos(1), 0) &&
            approx(Math.acosh(1), 0) &&
            approx(Math.asin(1), Math.PI / 2) &&
            approx(Math.asinh(0), 0) &&
            approx(Math.atan(1), Math.PI / 4) &&
            approx(Math.atan2(0, -0), Math.PI) &&
            approx(Math.atanh(0), 0) &&
            approx(Math.cbrt(27), 3) &&
            approx(Math.ceil(1.2), 2) &&
            Math.clz32() === 32 &&
            approx(Math.cos(0), 1) &&
            approx(Math.cosh(0), 1) &&
            approx(Math.exp(1), Math.E) &&
            approx(Math.expm1(1), Math.E - 1) &&
            approx(Math.f16round(1.337), 1.3369140625) &&
            approx(Math.floor(1.9), 1) &&
            approx(Math.fround(1.337), 1.3370000123977661, 1e-7) &&
            approx(Math.hypot(3, 4), 5) &&
            Math.imul(-1, 5) === -5 &&
            approx(Math.log(Math.E), 1) &&
            approx(Math.log1p(1), Math.LN2) &&
            approx(Math.log2(8), 3) &&
            approx(Math.log10(1000), 3) &&
            (1 / Math.max(-0, 0)) === Infinity &&
            (1 / Math.min(-0, 0)) === -Infinity &&
            approx(Math.pow(2, 3), 8) &&
            Math.random() >= 0 && Math.random() < 1 &&
            (1 / Math.round(-0.5)) === -Infinity &&
            (1 / Math.sign(-0)) === -Infinity &&
            approx(Math.sin(0), 0) &&
            approx(Math.sinh(0), 0) &&
            approx(Math.sqrt(9), 3) &&
            approx(Math.tan(0), 0) &&
            approx(Math.tanh(0), 0) &&
            approx(Math.trunc(-1.7), -1);
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Math_SumPrecise_Handles_Empty_Cancellation_And_TypeErrors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let typeError1 = false;
            try { Math.sumPrecise([1, "2"]); } catch (e) { typeError1 = e.name === "TypeError"; }
            let typeError2 = false;
            try { Math.sumPrecise(1); } catch (e) { typeError2 = e.name === "TypeError"; }
            (1 / Math.sumPrecise([])) === -Infinity &&
            Math.sumPrecise([1, 2, 3]) === 6 &&
            Math.abs(Math.sumPrecise([1e20, 0.1, -1e20]) - 0.1) < 1e-12 &&
            typeError1 &&
            typeError2;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Math_Hypot_Coerces_All_Arguments_Before_Inspecting_Them()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var counter = 0;
            var threw = false;
            try {
              Math.hypot(
                Infinity,
                -Infinity,
                NaN,
                0,
                -0,
                { valueOf: function() { throw new Error("boom"); } },
                { valueOf: function() { counter++; } }
              );
            } catch (e) {
              threw = e && e.name === "Error" && e.message === "boom";
            }

            threw && counter === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Math_SumPrecise_Closes_Iterator_On_NonNumber_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var coercions = 0;
            var objectWithValueOf = {
              valueOf: function() {
                ++coercions;
                throw new Error("valueOf should not be called");
              },
              toString: function() {
                ++coercions;
                throw new Error("toString should not be called");
              }
            };

            var nextCalls = 0;
            var returnCalls = 0;
            var iterator = {
              next: function () {
                ++nextCalls;
                return { done: false, value: objectWithValueOf };
              },
              return: function () {
                ++returnCalls;
                return {};
              }
            };
            var iterable = {
              [Symbol.iterator]: function () {
                return iterator;
              }
            };

            var threw = false;
            try {
              Math.sumPrecise(iterable);
            } catch (e) {
              threw = e && e.name === "TypeError";
            }

            threw && coercions === 0 && nextCalls === 1 && returnCalls === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
