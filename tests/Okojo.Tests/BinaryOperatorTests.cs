using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class BinaryOperatorTests
{
    [Test]
    public void Subtraction_Applies_ToNumeric_Left_Before_Right()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var trace = "";
                                                                   var ok = false;
                                                                   try {
                                                                     ({
                                                                       valueOf: function() {
                                                                         trace += "1";
                                                                         return Symbol("lhs");
                                                                       }
                                                                     }) - ({
                                                                       valueOf: function() {
                                                                         trace += "2";
                                                                         throw new Error("rhs should not run");
                                                                       }
                                                                     });
                                                                   } catch (e) {
                                                                     ok = e.constructor === TypeError;
                                                                   }
                                                                   ok && trace === "1";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RightShift_Applies_ToNumeric_Left_Before_Right()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var trace = "";
                                                                   var ok = false;
                                                                   try {
                                                                     ({
                                                                       valueOf: function() {
                                                                         trace += "1";
                                                                         return Symbol("lhs");
                                                                       }
                                                                     }) >> ({
                                                                       valueOf: function() {
                                                                         trace += "2";
                                                                         throw new Error("rhs should not run");
                                                                       }
                                                                     });
                                                                   } catch (e) {
                                                                     ok = e.constructor === TypeError;
                                                                   }
                                                                   ok && trace === "1";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnsignedRightShift_Returns_UInt32_Result_For_Negative_Int32()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   (-2147483647 >>> 0) === 2147483649;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arithmetic_Smi_FastPaths_Reject_BigInt_Number_Mixing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var ok = true;
                                                                   try { 1n - 1; ok = false; } catch (e) { ok = ok && e.constructor === TypeError; }
                                                                   try { 1n * 1; ok = false; } catch (e) { ok = ok && e.constructor === TypeError; }
                                                                   try { 1n % 1; ok = false; } catch (e) { ok = ok && e.constructor === TypeError; }
                                                                   try { 1n ** 1; ok = false; } catch (e) { ok = ok && e.constructor === TypeError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ModSmi_Preserves_NegativeZero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Object.is(-1 % 1, -0);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RelationalComparison_BigInt_And_Boolean_Uses_Numeric_Comparison()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                (0n < false) === false &&
                                (0n < true) === true &&
                                (false < 1n) === true &&
                                (1n > false) === true &&
                                (true > 0n) === true &&
                                (false > -3n) === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Exponentiation_Number_Edge_Cases_Match_JavaScript()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                Object.is(NaN ** -0, 1) &&
                                Number.isNaN((-1) ** Infinity) &&
                                Number.isNaN((-1) ** -Infinity) &&
                                Number.isNaN((1) ** Infinity) &&
                                Number.isNaN((1) ** -Infinity);
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void LogicalNot_BigInt_Literal_Uses_Truthiness()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                (!0n) === true && (!1n) === false && (!(-1n)) === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void AbstractEquality_Uses_Default_ToPrimitive_Hint()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let count = 0;
                                let obj = {
                                  [Symbol.toPrimitive](hint) {
                                    count += 1;
                                    return hint;
                                  }
                                };
                                (true == obj) === false &&
                                count === 1 &&
                                (obj == true) === false &&
                                count === 2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Addition_Uses_Default_ToPrimitive_Hint()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let log = "";
                                let leftHint;
                                let rightHint;
                                let left = {
                                  [Symbol.toPrimitive](hint) {
                                    log += "L";
                                    leftHint = hint;
                                    return "1";
                                  }
                                };
                                let right = {
                                  [Symbol.toPrimitive](hint) {
                                    log += "R";
                                    rightHint = hint;
                                    return "2";
                                  }
                                };
                                (left + right) === "12" &&
                                log === "LR" &&
                                leftHint === "default" &&
                                rightHint === "default";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Addition_Uses_Date_Default_String_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let date = new Date(0);
                                date + 0 === date.toString() + "0";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Addition_DefaultHint_Does_Not_ReRead_SymbolToPrimitive_On_Fallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let callCount = 0;
                                let counter = {};
                                Object.defineProperty(counter, Symbol.toPrimitive, {
                                  get() {
                                    callCount += 1;
                                    return undefined;
                                  }
                                });
                                let ok = false;
                                try {
                                  counter + {
                                    get [Symbol.toPrimitive]() { throw new Error("rhs should not run first"); }
                                  };
                                } catch (e) {
                                  ok = e.message === "rhs should not run first";
                                }
                                ok && callCount === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_Uses_SymbolHasInstance_Before_Callability_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var F = {};
                                var callCount = 0;
                                var thisValue;
                                var arg0;
                                F[Symbol.hasInstance] = function(value) {
                                  thisValue = this;
                                  arg0 = value;
                                  callCount += 1;
                                  return {};
                                };
                                (0 instanceof F) === true &&
                                callCount === 1 &&
                                thisValue === F &&
                                arg0 === 0;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_Propagates_SymbolHasInstance_Getter_Error()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var F = {};
                                var marker = {};
                                try {
                                  Object.defineProperty(F, Symbol.hasInstance, {
                                    get() { throw marker; }
                                  });
                                  0 instanceof F;
                                  false;
                                } catch (err) {
                                  err === marker;
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_Coerces_SymbolHasInstance_Result_To_Boolean()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var F = {};
                                F[Symbol.hasInstance] = function() { return undefined; };
                                var a = 0 instanceof F;
                                F[Symbol.hasInstance] = function() { return 1; };
                                var b = 0 instanceof F;
                                F[Symbol.hasInstance] = function() { return {}; };
                                var c = 0 instanceof F;
                                a === false && b === true && c === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void PrivateIdentifier_In_Uses_Private_Brand_Presence_Without_Invoking_Method()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let count = 0;
                                class C {
                                  #method() { count += 1; }
                                  static has(value) { return #method in value; }
                                }
                                C.has({}) === false &&
                                C.has(new C()) === true &&
                                count === 0;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void PrivateIdentifier_In_Throws_TypeError_For_NonObject_RightHandSide()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let caught = null;
                                class C {
                                  #field;
                                  static run() {
                                    try {
                                      return #field in ({} << 0);
                                    } catch (error) {
                                      caught = error;
                                    }
                                  }
                                }
                                C.run();
                                caught instanceof TypeError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void PrivateIdentifier_In_Propagates_Unresolvable_Reference()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let caught = null;
                                class C {
                                  #field;
                                  static run() {
                                    try {
                                      return #field in test262unresolvable;
                                    } catch (error) {
                                      caught = error;
                                    }
                                  }
                                }
                                C.run();
                                caught instanceof ReferenceError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
