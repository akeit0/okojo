using System.Numerics;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class BigIntFeatureTests
{
    [Test]
    public void BigInt_Literals_Parse_And_Evaluate()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            typeof 1n === "bigint" &&
            1n === BigInt(1) &&
            0b101n === 5n &&
            0o77n === 63n &&
            0xffn === 255n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Constructor_And_Static_Methods_Follow_Node_Behavior()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ctorTypeError = false;
            try { new BigInt(1); } catch (e) { ctorTypeError = e.name === "TypeError"; }

            let rangeError = false;
            try { BigInt(1.5); } catch (e) { rangeError = e.name === "RangeError"; }

            let syntaxError = false;
            try { BigInt("1.5"); } catch (e) { syntaxError = e.name === "SyntaxError"; }

            ctorTypeError &&
            rangeError &&
            syntaxError &&
            BigInt(true) === 1n &&
            BigInt("0xff") === 255n &&
            BigInt.asIntN(4, 15n) === -1n &&
            BigInt.asUintN(4, -1n) === 15n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Has_Construct_Internal_Slot_But_New_Still_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function isConstructor(value) {
              try {
                Reflect.construct(function() {}, [], value);
                return true;
              } catch (e) {
                return false;
              }
            }

            let threw = false;
            try { new BigInt(1); } catch (e) { threw = e instanceof TypeError; }
            isConstructor(BigInt) && threw;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Constructor_Uses_SymbolToPrimitive_Once_With_Number_Hint()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let calls = 0;
            const value = {
              [Symbol.toPrimitive](hint) {
                calls++;
                return hint === "number" ? "42" : "bad";
              }
            };
            BigInt(value) === 42n && calls === 1;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Signed_NonDecimal_Strings_Throw_SyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let negHex = false;
            try { BigInt("-0x1"); } catch (e) { negHex = e.name === "SyntaxError"; }
            let negOct = false;
            try { BigInt("-0o7"); } catch (e) { negOct = e.name === "SyntaxError"; }
            let posBin = false;
            try { BigInt("+0b1"); } catch (e) { posBin = e.name === "SyntaxError"; }
            negHex && negOct && posBin;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Empty_Or_Whitespace_String_Yields_Zero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            BigInt("") === 0n && BigInt(" ") === 0n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_AsIntN_Rejects_Number_Input()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval("BigInt.asIntN(0, 0);"));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void BigInt_AsIntN_ToIndex_Truncates_Negative_Fractions_To_Zero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            BigInt.asIntN(-0.9, 1n) === 0n && BigInt.asIntN(0.9, 1n) === 0n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_AsIntN_ToIndex_Throws_For_Negative_Integer_TooLarge_And_BigInt_Bits()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let negative = false;
            try { BigInt.asIntN(-1, 0n); } catch (e) { negative = e.name === "RangeError"; }
            let tooLarge = false;
            try { BigInt.asIntN(9007199254740992, 0n); } catch (e) { tooLarge = e.name === "RangeError"; }
            let bigintBits = false;
            try { BigInt.asIntN(0n, 0n); } catch (e) { bigintBits = e.name === "TypeError"; }
            let symbolBits = false;
            try { BigInt.asIntN(Symbol("1"), 0n); } catch (e) { symbolBits = e.name === "TypeError"; }
            negative && tooLarge && bigintBits && symbolBits;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_AsUintN_ToIndex_Throws_For_Symbol_Input()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let primitiveSymbol = false;
            try { BigInt.asUintN(Symbol("1"), 0n); } catch (e) { primitiveSymbol = e.name === "TypeError"; }
            let boxedSymbol = false;
            try { BigInt.asUintN(Object(Symbol("1")), 0n); } catch (e) { boxedSymbol = e.name === "TypeError"; }
            primitiveSymbol && boxedSymbol;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Prototype_Methods_Use_ThisBigIntValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let badThis = false;
            try { BigInt.prototype.toString.call(1); } catch (e) { badThis = e.name === "TypeError"; }

            const boxed = Object(1n);
            badThis &&
            BigInt.prototype.toString.call(boxed) === "1" &&
            BigInt.prototype.valueOf.call(boxed) === 1n &&
            Object.prototype.toString.call(boxed) === "[object BigInt]";
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Prototype_ToString_Length_Is_Zero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var desc = Object.getOwnPropertyDescriptor(BigInt.prototype.toString, "length");
            desc.value === 0 &&
            desc.writable === false &&
            desc.enumerable === false &&
            desc.configurable === true;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_ToLocaleString_Delegates_To_Intl_NumberFormat()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let localeTypeError = false;
                                try { 0n.toLocaleString(null); } catch (e) { localeTypeError = e && e.name === "TypeError"; }
                                [
                                  localeTypeError,
                                  12345n.toLocaleString("en"),
                                  12345n.toLocaleString("de"),
                                  88776655n.toLocaleString("de-DE", { maximumSignificantDigits: 4, style: "percent" }),
                                  0n.toLocaleString("th-u-nu-thai", { minimumFractionDigits: 3 })
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|12,345|12.345|8.878.000.000\u00A0%|๐.๐๐๐"));
    }

    [Test]
    public void BigInt_Invalid_Literal_Forms_Are_Rejected()
    {
        Assert.That(() => JavaScriptParser.ParseScript("1.5n;"), Throws.InstanceOf<JsParseException>());
        Assert.That(() => JavaScriptParser.ParseScript("1e3n;"), Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void BigInt_Binary_Arithmetic_And_Unary_Negate_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const sum = 10n + 3n;
            const diff = 10n - 3n;
            const prod = 10n * 3n;
            const quot = 10n / 3n;
            const rem = 10n % 3n;
            const pow = 2n ** 5n;
            const neg = -1n;
            sum === 13n &&
            diff === 7n &&
            prod === 30n &&
            quot === 3n &&
            rem === 1n &&
            pow === 32n &&
            neg === -1n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Mixed_Number_Arithmetic_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval("1n + 1;"));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void BigInt_Shift_And_BitwiseNot_Work_For_BigIntOperands()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (1n << 2n) === 4n &&
            (8n >> 2n) === 2n &&
            (~1n) === -2n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Shift_With_Number_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval("1n << 2;"));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void BigInt_Update_Expressions_Use_BigInt_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var b = 1000n;
            var old = b++;
            var afterPrefix = ++b;
            old === 1000n && b === 1002n && afterPrefix === 1002n;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BigInt_Bytecode_Literal_Executes_To_BigInt_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Execute(new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("1n;")));
        Assert.That(realm.Accumulator.IsBigInt, Is.True);
        Assert.That(realm.Accumulator.AsBigInt().Value, Is.EqualTo(new BigInteger(1)));
    }
}
