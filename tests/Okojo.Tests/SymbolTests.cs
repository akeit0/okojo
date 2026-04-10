using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SymbolTests
{
    [Test]
    public void SymbolIterator_IsPredefinedSymbol_AndStableIdentity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   Symbol.iterator === Symbol.iterator;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SymbolKeyedProperty_RoundTripsThroughBracketAccess()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const s = Symbol("x");
                                                                   const o = {};
                                                                   o[s] = 123;
                                                                   o[s];
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(123));
    }

    [Test]
    public void ObjectGetOwnPropertyDescriptor_WorksForSymbolKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const s = Symbol("k");
                                                                   const o = {};
                                                                   o[s] = 77;
                                                                   Object.getOwnPropertyDescriptor(o, s).value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(77));
    }

    [Test]
    public void SymbolPrototype_ToString_WorksForPrimitiveAndBoxed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const s = Symbol("x");
                                                                   s.toString() + "|" + Object(s).toString();
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Symbol(x)|Symbol(x)"));
    }

    [Test]
    public void SymbolPrototype_ValueOf_OnBoxed_ReturnsOriginalSymbol()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const s = Symbol("k");
                                                                   Object(s).valueOf() === s;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SymbolPrototype_Description_WorksForUserAndWellKnownSymbols()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const custom = Symbol("demo").description;
                                                                   const wellKnown = Symbol.asyncIterator.description;
                                                                   custom + "|" + wellKnown;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("demo|Symbol.asyncIterator"));
    }

    [Test]
    public void SymbolToStringTag_IsPredefinedSymbol_AndStableIdentity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   Symbol.toStringTag === Symbol.toStringTag;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeToString_UsesSymbolToStringTag_WhenString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const o = {};
                                                                   o[Symbol.toStringTag] = "DemoTag";
                                                                   o.toString();
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("[object DemoTag]"));
    }

    [Test]
    public void Symbol_Constructor_Coerces_Description_And_Preserves_Undefined_Vs_Empty_String()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var calls = "";
                                                                   var desc = {
                                                                     toString() {
                                                                       calls += "toString";
                                                                       return {};
                                                                     },
                                                                     valueOf() {
                                                                       calls += "valueOf";
                                                                       return "done";
                                                                     }
                                                                   };

                                                                   var empty = Symbol("").description;
                                                                   var implicitUndefined = Symbol().description;
                                                                   var explicitUndefined = Symbol(undefined).description;
                                                                   var coerced = Symbol(desc).description;
                                                                   var threw = false;
                                                                   try { Symbol(Symbol("x")); } catch (e) { threw = e instanceof TypeError; }

                                                                   empty === "" &&
                                                                   implicitUndefined === undefined &&
                                                                   explicitUndefined === undefined &&
                                                                   coerced === "done" &&
                                                                   calls === "toStringvalueOf" &&
                                                                   threw;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Symbol_Constructor_And_Prototype_Descriptors_Match_Spec_Shape()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var lengthDesc = Object.getOwnPropertyDescriptor(Symbol, "length");
                                                                   var forDesc = Object.getOwnPropertyDescriptor(Symbol, "for");
                                                                   var keyForDesc = Object.getOwnPropertyDescriptor(Symbol, "keyFor");
                                                                   var descriptionDesc = Object.getOwnPropertyDescriptor(Symbol.prototype, "description");
                                                                   var toPrimitiveDesc = Object.getOwnPropertyDescriptor(Symbol.prototype, Symbol.toPrimitive);
                                                                   var toStringTagDesc = Object.getOwnPropertyDescriptor(Symbol.prototype, Symbol.toStringTag);

                                                                   lengthDesc.value === 0 &&
                                                                   lengthDesc.writable === false &&
                                                                   lengthDesc.enumerable === false &&
                                                                   lengthDesc.configurable === true &&
                                                                   forDesc.enumerable === false &&
                                                                   keyForDesc.enumerable === false &&
                                                                   descriptionDesc.enumerable === false &&
                                                                   descriptionDesc.configurable === true &&
                                                                   descriptionDesc.set === undefined &&
                                                                   toPrimitiveDesc.writable === false &&
                                                                   toPrimitiveDesc.enumerable === false &&
                                                                   toPrimitiveDesc.configurable === true &&
                                                                   toStringTagDesc.value === "Symbol" &&
                                                                   toStringTagDesc.writable === false &&
                                                                   toStringTagDesc.enumerable === false &&
                                                                   toStringTagDesc.configurable === true;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Strict_Assignment_To_Symbol_Primitive_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function assignNamed() {
                                                                     "use strict";
                                                                     var sym = Symbol("x");
                                                                     try { sym.toString = 0; return false; } catch (e) { return e instanceof TypeError; }
                                                                   }
                                                                   function assignKeyed() {
                                                                     "use strict";
                                                                     var sym = Symbol("x");
                                                                     try { sym["ab"] = 0; return false; } catch (e) { return e instanceof TypeError; }
                                                                   }
                                                                   function assignIndexed() {
                                                                     "use strict";
                                                                     var sym = Symbol("x");
                                                                     try { sym[62] = 0; return false; } catch (e) { return e instanceof TypeError; }
                                                                   }
                                                                   assignNamed() && assignKeyed() && assignIndexed();
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArraySpecies_Getter_Has_BuiltIn_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   Object.getOwnPropertyDescriptor(Array, Symbol.species).get.name === "get [Symbol.species]";
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
