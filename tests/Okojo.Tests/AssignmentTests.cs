using System.Text;
using System.Text.RegularExpressions;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AssignmentTests
{
    [Test]
    public void CompoundNamedMemberAssignment_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { x: 1 };
                                                                   o.x += 3;
                                                                   o.x === 4;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundKeyedMemberAssignment_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { 0: 1 };
                                                                   o[0] += 2;
                                                                   o[0] === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ComputedMemberAssignment_Evaluates_RightHandSide_Before_ToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function DummyError() {}
                                                                   var check1 = false;
                                                                   var check2 = false;
                                                                   try {
                                                                     var base = {};
                                                                     var prop = function() { throw new DummyError(); };
                                                                     var expr = function() { throw new Error("rhs"); };
                                                                     base[prop()] = expr();
                                                                   } catch (error) {
                                                                     check1 = error instanceof DummyError;
                                                                   }
                                                                   try {
                                                                     var base2 = {};
                                                                     var prop2 = { toString: function() { throw new Error("key"); } };
                                                                     var expr2 = function() { throw new DummyError(); };
                                                                     base2[prop2] = expr2();
                                                                   } catch (error) {
                                                                     check2 = error instanceof DummyError;
                                                                   }
                                                                   check1 && check2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ComputedMemberAssignment_Stores_RightHandSide_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var key = "x";
                                                                   var obj = {};
                                                                   obj[key] = "value";
                                                                   obj.x === "value";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ComputedMemberAssignment_ToSetter_Passes_RightHandSide_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var key = "x";
                                                                   var received = "unset";
                                                                   var obj = {
                                                                     set [key](value) { received = value; }
                                                                   };
                                                                   obj[key] = "payload";
                                                                   received === "payload";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Var_Reassignment_And_Redeclaration_Can_Store_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x = 1;
                                                                   x = undefined;
                                                                   var y = 2;
                                                                   var y = undefined;

                                                                   x === undefined && y === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Assignment_ToPrimitiveBase_Property_Uses_Boxed_Prototype_Set()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var numberCount = 0;
                                                                   var stringCount = 0;
                                                                   var booleanCount = 0;
                                                                   var symbolCount = 0;
                                                                   var spy;

                                                                   spy = new Proxy({}, { set: function() { numberCount += 1; return true; } });
                                                                   Object.setPrototypeOf(Number.prototype, spy);
                                                                   0..test262 = null;

                                                                   spy = new Proxy({}, { set: function() { stringCount += 1; return true; } });
                                                                   Object.setPrototypeOf(String.prototype, spy);
                                                                   ''.test262 = null;

                                                                   spy = new Proxy({}, { set: function() { booleanCount += 1; return true; } });
                                                                   Object.setPrototypeOf(Boolean.prototype, spy);
                                                                   true.test262 = null;

                                                                   spy = new Proxy({}, { set: function() { symbolCount += 1; return true; } });
                                                                   Object.setPrototypeOf(Symbol.prototype, spy);
                                                                   Symbol().test262 = null;

                                                                   numberCount === 1 && stringCount === 1 && booleanCount === 1 && symbolCount === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Strict_Assignment_ToPrimitiveBase_Property_Uses_Boxed_Prototype_Set_Before_Throwing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   var count = 0;
                                                                   var spy = new Proxy({}, { set: function() { count += 1; return true; } });
                                                                   Object.setPrototypeOf(String.prototype, spy);
                                                                   ''.test262 = null;
                                                                   count === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_NonReference_Still_Evaluates_Operand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var called = false;
                                                                   function mark() {
                                                                     called = true;
                                                                     return {};
                                                                   }
                                                                   var result = delete mark();
                                                                   result === true && called === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ExponentiationAssignment_Identifier_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let value = 3;
                                                                   let result = (value **= 2);
                                                                   result === 9 && value === 9;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ExponentiationAssignment_PrivateField_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class C {
                                                                     #x = 3;
                                                                     run() {
                                                                       let result = (this.#x **= 2);
                                                                       return result === 9 && this.#x === 9;
                                                                     }
                                                                   }
                                                                   new C().run();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ParenthesizedIdentifierAssignment_DoesNot_Infer_Anonymous_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var fn;
                                                                   (fn) = function() {};
                                                                   Object.getOwnPropertyDescriptor(fn, "name").value === "";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundNamedMemberAssignment_SharesFeedbackSlot_ForLoadAndStore()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let o = { x: 1 };
                                                                       o.x += 3;
                                                                       return o.x;
                                                                   }
                                                                   t();
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        var loadMatches = Regex.Matches(disasm, @"LdaNamedProperty .* slot:(\d+)");
        var storeMatches = Regex.Matches(disasm, @"StaNamedProperty .* slot:(\d+)");
        Assert.That(loadMatches.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(storeMatches.Count, Is.GreaterThanOrEqualTo(1));

        var loadSlot = int.Parse(loadMatches[0].Groups[1].Value);
        var storeSlot = int.Parse(storeMatches[0].Groups[1].Value);
        Assert.That(storeSlot, Is.EqualTo(loadSlot));
    }

    [Test]
    public void CompoundKeyedMemberAssignment_UsesKeyedOpsWithoutNamedIc()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let o = { 0: 1 };
                                                                       o[0] += 2;
                                                                       return o[0];
                                                                   }
                                                                   t();
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("LdaKeyedProperty"));
        Assert.That(disasm, Does.Contain("StaKeyedProperty"));
        Assert.That(disasm, Does.Not.Contain("LdaNamedProperty"));
        Assert.That(disasm, Does.Not.Contain("StaNamedProperty"));
        Assert.That(disasm, Does.Contain("LdaKeyedProperty obj:r"));
        Assert.That(Regex.IsMatch(disasm, @"LdaKeyedProperty .*slot:"), Is.False);
        Assert.That(Regex.IsMatch(disasm, @"StaKeyedProperty .*slot:"), Is.False);
        Assert.That(Regex.IsMatch(disasm, @"DefineOwnKeyedProperty .*slot:"), Is.False);
    }

    [Test]
    public void KeyedMemberAssignment_WithManyLocals_UsesWideOperands_Without_Throwing()
    {
        var source = new StringBuilder();
        source.AppendLine("function t() {");
        source.AppendLine("  let o = {};");
        source.AppendLine("  let k = 0;");
        for (var i = 0; i < 320; i++)
            source.AppendLine($"  let v{i} = {i};");
        source.AppendLine("  o[k] = 1;");
        source.AppendLine("  return o[k] === 1;");
        source.AppendLine("}");
        source.AppendLine("t();");

        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript(source.ToString()));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateNamedMember_Postfix_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { x: 1 };
                                                                   let old = o.x++;
                                                                   old === 1 && o.x === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateNamedMember_Prefix_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { x: 1 };
                                                                   let now = ++o.x;
                                                                   now === 2 && o.x === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateKeyedMember_Postfix_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { 0: 1 };
                                                                   let old = o[0]++;
                                                                   old === 1 && o[0] === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateNamedMember_SharesFeedbackSlot_ForLoadAndStore()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let o = { x: 1 };
                                                                       return o.x++;
                                                                   }
                                                                   t();
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        var loadMatches = Regex.Matches(disasm, @"LdaNamedProperty .* slot:(\d+)");
        var storeMatches = Regex.Matches(disasm, @"StaNamedProperty .* slot:(\d+)");
        Assert.That(loadMatches.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(storeMatches.Count, Is.GreaterThanOrEqualTo(1));

        var loadSlot = int.Parse(loadMatches[0].Groups[1].Value);
        var storeSlot = int.Parse(storeMatches[0].Groups[1].Value);
        Assert.That(storeSlot, Is.EqualTo(loadSlot));
    }

    [Test]
    public void LogicalAssignment_Identifier_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let a = 0;
                                                                   let r1 = (a &&= 5);
                                                                   let b = 0;
                                                                   let r2 = (b ||= 6);
                                                                   r1 === 0 && a === 0 &&
                                                                   r2 === 6 && b === 6;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LogicalAssignment_NamedMember_ShortCircuit_DoesNotEvaluateRhs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let access = 0;
                                                                   let rhs = 0;
                                                                   function rv() { rhs += 1; return 5; }
                                                                   let o = {
                                                                     _x: 0,
                                                                     get x() { access += 1; return this._x; },
                                                                     set x(v) { access += 1; this._x = v; }
                                                                   };
                                                                   let r = (o.x &&= rv());
                                                                   r === 0 && o._x === 0 && rhs === 0 && access === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LogicalAssignment_ComputedMember_EvaluatesKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let keyCount = 0;
                                                                   function k() { keyCount += 1; return "x"; }
                                                                   let o = { x: 1 };
                                                                   let r = (o[k()] &&= 2);
                                                                   r === 2 && o.x === 2 && keyCount === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NullishAssignment_Identifier_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let a = null;
                                                                   let r1 = (a ??= 5);
                                                                   let b;
                                                                   let r2 = (b ??= 6);
                                                                   let c = 7;
                                                                   let r3 = (c ??= 9);
                                                                   r1 === 5 && a === 5 &&
                                                                   r2 === 6 && b === 6 &&
                                                                   r3 === 7 && c === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NullishAssignment_ComputedMember_EvaluatesKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let keyCount = 0;
                                                                   function k() { keyCount += 1; return "x"; }
                                                                   let o = { x: null };
                                                                   let r = (o[k()] ??= 2);
                                                                   r === 2 && o.x === 2 && keyCount === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundAssignment_GlobalDeleteDuringGet_StrictPutValueThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var count = 0;
                                                                   Object.defineProperty(this, "x", {
                                                                     configurable: true,
                                                                     get: function() {
                                                                       delete this.x;
                                                                       return 2;
                                                                     }
                                                                   });

                                                                   (function() {
                                                                     "use strict";
                                                                     try {
                                                                       count++;
                                                                       x ^= 3;
                                                                       count++;
                                                                     } catch (e) {
                                                                       if (!(e instanceof ReferenceError)) count = -100;
                                                                       count++;
                                                                     }
                                                                   })();

                                                                   count === 2 && !("x" in this);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NullishCoalescing_Binary_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (null ?? 3) === 3 &&
                                                                   (undefined ?? 4) === 4 &&
                                                                   (0 ?? 5) === 0 &&
                                                                   (false ?? 6) === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundBitwiseXor_BoxedNumberAndBoolean_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x;
                                                                   x = new Number(1); x ^= 1; if (x !== 0) throw new Error("n^n");
                                                                   x = 1; x ^= new Number(1); if (x !== 0) throw new Error("n^N");
                                                                   x = new Boolean(true); x ^= 1; if (x !== 0) throw new Error("b^n");
                                                                   x = 1; x ^= new Boolean(true); if (x !== 0) throw new Error("n^B");
                                                                   true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryBitwiseNot_BoxedNumber_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ~new Number(3) === -4;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UnaryBitwiseNot_Literal_Uses_JavaScript_ToInt32_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ~2147483648 === ~-2147483648 &&
                                                                   ~2147483649 === ~-2147483647 &&
                                                                   ~4294967296 === ~0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NotEquals_ObjectToPrimitive_StringCases_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ({valueOf: function() {return {}}, toString: function() {return "+1"}} != "1") === true &&
                                                                   ({valueOf: function() {return {}}, toString: function() {return "+1"}} != "+1") === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_ValueOf_ToString_UsePredefinedAtoms()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ({ valueOf: function(){ return 1; }, toString: function(){ return "x"; }});
                                                                   """));

        var shape = script.ObjectConstants.OfType<StaticNamedPropertyLayout>().First();
        var atoms = shape.EnumerateSlotInfos().Select(kv => kv.Key).ToHashSet();
        Assert.That(atoms, Does.Contain(AtomTable.IdValueOf));
        Assert.That(atoms, Does.Contain(AtomTable.IdToString));
    }

    [Test]
    public void NotEquals_ObjectToPrimitive_ValueOfThrow_IsCatchable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var out;
                                                                   try {
                                                                     ({valueOf: function() { throw "error"; }, toString: function() { return 1; }} != 1);
                                                                     out = "no";
                                                                   } catch (e) {
                                                                     out = e;
                                                                   }
                                                                   out === "error";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void PostfixIncrement_Object_UsesToPrimitiveNumber()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var object = { valueOf: function() { return 1; }, toString: function() { throw "bad"; } };
                                                                   var y = object++;
                                                                   (y === 1) && (object === 2);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void PostfixIncrement_ComputedMember_NullBase_ThrowsBeforeKeyEvaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var threwTypeError = false;
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = { toString: function() { throw "key-evaluated"; } };
                                                                     baseObj[prop]++;
                                                                   } catch (e) {
                                                                     threwTypeError = e instanceof TypeError;
                                                                   }
                                                                   threwTypeError === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void PostfixIncrement_ComputedMember_NullBase_EvaluatesPropertyExpressionBeforeTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var gotDummy = false;
                                                                   function DummyError() {}
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = function() { throw new DummyError(); };
                                                                     baseObj[prop()]++;
                                                                   } catch (e) {
                                                                     gotDummy = (e.constructor === DummyError);
                                                                   }
                                                                   gotDummy === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ComputedMemberAssignment_NullBase_EvaluatesRhsBeforeToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function DummyError() {}
                                                                   var threwDummy = false;
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = { toString: function() { throw new Error("key"); } };
                                                                     var expr = function() { throw new DummyError(); };
                                                                     baseObj[prop] = expr();
                                                                   } catch (e) {
                                                                     threwDummy = (e.constructor === DummyError);
                                                                   }
                                                                   threwDummy === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LogicalOrAssignment_ComputedMember_NullBase_ThrowsTypeErrorBeforeToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var threwTypeError = false;
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = { toString: function() { throw new Error("key"); } };
                                                                     var expr = function() { throw new Error("rhs"); };
                                                                     baseObj[prop] ||= expr();
                                                                   } catch (e) {
                                                                     threwTypeError = (e instanceof TypeError);
                                                                   }
                                                                   threwTypeError === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundAssignment_ComputedMember_NullBase_ThrowsTypeErrorBeforeToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var threwTypeError = false;
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = { toString: function() { throw new Error("key"); } };
                                                                     var expr = function() { throw new Error("rhs"); };
                                                                     baseObj[prop] *= expr();
                                                                   } catch (e) {
                                                                     threwTypeError = (e instanceof TypeError);
                                                                   }
                                                                   threwTypeError === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void PostfixDecrement_ComputedMember_NullBase_ThrowsTypeErrorBeforeToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var threwTypeError = false;
                                                                   try {
                                                                     var baseObj = null;
                                                                     var prop = { toString: function() { throw new Error("key"); } };
                                                                     baseObj[prop]--;
                                                                   } catch (e) {
                                                                     threwTypeError = (e instanceof TypeError);
                                                                   }
                                                                   threwTypeError === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompoundAssignment_ComputedMember_CallsToPropertyKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var propKeyEvaluated = false;
                                                                   var baseObj = {};
                                                                   var prop = {
                                                                     toString: function() {
                                                                       if (propKeyEvaluated) throw new Error("twice");
                                                                       propKeyEvaluated = true;
                                                                       return "";
                                                                     }
                                                                   };
                                                                   var expr = function() { return 0; };
                                                                   baseObj[prop] *= expr();
                                                                   propKeyEvaluated === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LogicalOrAssignment_ComputedMember_CallsToPropertyKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var propKeyEvaluated = false;
                                                                   var obj = {};
                                                                   var prop = {
                                                                     toString: function() {
                                                                       if (propKeyEvaluated) throw new Error("twice");
                                                                       propKeyEvaluated = true;
                                                                       return "";
                                                                     }
                                                                   };
                                                                   var expr = function() { return 1; };
                                                                   obj[prop] ||= expr();
                                                                   propKeyEvaluated === true && obj[""] === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void PostfixDecrement_ComputedMember_CallsToPropertyKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var propKeyEvaluated = false;
                                                                   var obj = { 1: 1 };
                                                                   var prop = {
                                                                     toString: function() {
                                                                       if (propKeyEvaluated) throw new Error("twice");
                                                                       propKeyEvaluated = true;
                                                                       return 1;
                                                                     }
                                                                   };
                                                                   obj[prop]--;
                                                                   propKeyEvaluated === true && obj[1] === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Identifier_Assignment_Assigns_Anonymous_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var value = function() {};
                                value.name === "value";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Identifier_LogicalOr_Assignment_Assigns_Anonymous_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var value = 0;
                                value ||= function() {};
                                value.name === "value";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Identifier_LogicalAnd_Assignment_Assigns_Anonymous_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var value = 1;
                                value &&= function() {};
                                value.name === "value";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Identifier_Nullish_Assignment_Assigns_Anonymous_Function_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var value = null;
                                value ??= function() {};
                                value.name === "value";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
