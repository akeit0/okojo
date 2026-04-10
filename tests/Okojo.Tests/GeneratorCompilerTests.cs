using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class GeneratorCompilerTests
{
    [Test]
    public void Generator_Call_DoesNotExecuteBody_UntilNext()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let ran = 0;
                                                                   function* g() {
                                                                       ran = 1;
                                                                       yield 2;
                                                                   }
                                                                   let it = g();
                                                                   ran;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void Generator_Yield_Resumes_With_Next_Input()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       const x = yield 1;
                                                                       return x;
                                                                   }
                                                                   let it = g();
                                                                   let first = it.next();
                                                                   let second = it.next(9);
                                                                   first.value + second.value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void Generator_Return_BeforeFirstNext_CompletesImmediately()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       yield 1;
                                                                       return 2;
                                                                   }
                                                                   let it = g();
                                                                   let r = it.return(7);
                                                                   r.value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Generator_IsNotConstructible()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {}
                                                                   new g();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Does.Contain("not a constructor"));
    }

    [Test]
    public void Generator_Return_OnSuspendedYield_DoesNotRunTrailingCode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let side = 0;
                                                                   function* g() {
                                                                       yield 1;
                                                                       side = 99;
                                                                       return 2;
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   let r = it.return(7);
                                                                   side + r.value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Generator_Throw_OnSuspendedYield_ThrowsPassedValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       yield 1;
                                                                       return 2;
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   it.throw(5);
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("JS_THROW_VALUE"));
        Assert.That(ex.ThrownValue.HasValue, Is.True);
        Assert.That(ex.ThrownValue!.Value.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void Generator_Throw_AfterCompletion_ThrowsPassedValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       return 1;
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   it.throw(9);
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("JS_THROW_VALUE"));
        Assert.That(ex.ThrownValue.HasValue, Is.True);
        Assert.That(ex.ThrownValue!.Value.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void YieldDelegate_DelegatesYieldedValues_AndFinalValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* inner() {
                                                                       yield 1;
                                                                       yield 2;
                                                                       return 3;
                                                                   }
                                                                   function* outer() {
                                                                       const r = yield* inner();
                                                                       return r + 10;
                                                                   }
                                                                   let it = outer();
                                                                   let a = it.next().value;
                                                                   let b = it.next().value;
                                                                   let c = it.next().value;
                                                                   a + b + c;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(16));
    }

    [Test]
    public void YieldDelegate_PassesNextValueToInnerGenerator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* inner() {
                                                                       const x = yield 1;
                                                                       return x;
                                                                   }
                                                                   function* outer() {
                                                                       return yield* inner();
                                                                   }
                                                                   let it = outer();
                                                                   it.next();
                                                                   let r = it.next(7);
                                                                   r.value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void YieldDelegate_UsesSymbolIteratorProtocol_NotDirectNextOnOperand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const iterable = {};
                                                                   iterable.next = function () {
                                                                       return { value: 999, done: true };
                                                                   };
                                                                   iterable[Symbol.iterator] = function () {
                                                                       return {
                                                                           next: function () {
                                                                               return { value: 4, done: true };
                                                                           }
                                                                       };
                                                                   };
                                                                   function* outer() {
                                                                       return yield* iterable;
                                                                   }
                                                                   let it = outer();
                                                                   it.next().value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void YieldDelegate_Return_ForwardsToDelegateReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const inner = {
                                                                       next: function () { return { value: 1, done: false }; },
                                                                       return: function (v) { return { value: 42, done: true }; }
                                                                   };
                                                                   inner[Symbol.iterator] = function () { return inner; };
                                                                   function* outer() {
                                                                       return yield* inner;
                                                                   }
                                                                   let it = outer();
                                                                   it.next();
                                                                   it.return(9).value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void YieldDelegate_Throw_ForwardsToDelegateThrow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const inner = {
                                                                       next: function () { return { value: 1, done: false }; },
                                                                       throw: function (e) { return { value: e + 1, done: true }; }
                                                                   };
                                                                   inner[Symbol.iterator] = function () { return inner; };
                                                                   function* outer() {
                                                                       return yield* inner;
                                                                   }
                                                                   let it = outer();
                                                                   it.next();
                                                                   it.throw(4).value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void GeneratorSuspend_Uses_CurrentLiveRegisterCount_NotFinalScriptCount()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g(a) {
                                                                       yield a;
                                                                       let x = (a + 1) + ((a + 2) + (a + 3));
                                                                       return x;
                                                                   }
                                                                   """));

        var g = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "g");
        var code = g.Script.Bytecode;
        var suspendPc = Array.IndexOf(code, (byte)JsOpCode.SuspendGenerator);
        Assert.That(suspendPc, Is.GreaterThanOrEqualTo(0));

        var firstReg = code[suspendPc + 2];
        var liveCount = code[suspendPc + 3];

        Assert.That(firstReg, Is.EqualTo(0));
        Assert.That(liveCount, Is.GreaterThan(0));
        Assert.That(liveCount, Is.LessThan(g.Script.RegisterCount));
    }

    [Test]
    public void Generator_Emits_SwitchOnGeneratorState_Table_ForSuspendTargets()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       yield 1;
                                                                       yield 2;
                                                                       return 3;
                                                                   }
                                                                   """));

        var g = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "g");
        var code = g.Script.Bytecode;
        Assert.That(code.Length, Is.GreaterThanOrEqualTo(4));
        Assert.That((JsOpCode)code[0], Is.EqualTo(JsOpCode.SwitchOnGeneratorState));
        var generatorStateReg = code[1];
        Assert.That(generatorStateReg, Is.EqualTo(0xFF));
        Assert.That(g.Script.GeneratorSwitchTargets, Is.Not.Null);

        int tableStart = code[2];
        int tableLen = code[3];
        Assert.That(tableLen, Is.EqualTo(2));
        var table = g.Script.GeneratorSwitchTargets!;
        Assert.That(tableStart + tableLen, Is.LessThanOrEqualTo(table.Length));

        for (var i = 0; i < tableLen; i++)
        {
            var targetPc = table[tableStart + i];
            Assert.That((uint)targetPc, Is.LessThan((uint)code.Length));
            Assert.That((JsOpCode)code[targetPc], Is.EqualTo(JsOpCode.ResumeGenerator));
            Assert.That(code[targetPc + 1], Is.EqualTo((byte)0xFF));
        }
    }


    [Test]
    public void Generator_Return_OnSuspendedYield_RunsFinally_AndKeepsReturnValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let side = 0;
                                                                   function* g() {
                                                                       try {
                                                                           yield 1;
                                                                       } finally {
                                                                           side = side + 1;
                                                                       }
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   let r = it.return(9);
                                                                   side + r.value;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void Generator_Throw_OnSuspendedYield_RunsFinally_BeforePropagating()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let side = 0;
                                                                   function* g() {
                                                                       try {
                                                                           yield 1;
                                                                       } finally {
                                                                           side = side + 1;
                                                                       }
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   try {
                                                                       it.throw(7);
                                                                   } catch (e) {
                                                                   }
                                                                   side;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Generator_FinallyReturn_OverridesAbruptReturnAndThrow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       try {
                                                                           yield 1;
                                                                       } finally {
                                                                           return 42;
                                                                       }
                                                                   }
                                                                   let a = g();
                                                                   a.next();
                                                                   let r = a.return(9).value;
                                                                   let b = g();
                                                                   b.next();
                                                                   let t = b.throw(8).value;
                                                                   r + t;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(84));
    }

    [Test]
    public void GeneratorFunctionPrototype_Exposes_GeneratorPrototype_Via_Prototype_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {}
                                                                   var proto = Object.getPrototypeOf(g);
                                                                   var desc = Object.getOwnPropertyDescriptor(proto, "prototype");
                                                                   [
                                                                       typeof proto.prototype,
                                                                       proto.prototype === Object.getPrototypeOf(g()),
                                                                       desc.writable,
                                                                       desc.enumerable,
                                                                       desc.configurable
                                                                   ].join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("object,false,false,false,true"));
    }

    [Test]
    public void GeneratorFunction_Instance_Prototype_Has_No_Own_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {}
                                                                   var ownProperties = Object.getOwnPropertyNames(g.prototype);
                                                                   ownProperties.length;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void Subclassed_GeneratorFunction_Instance_Prototype_Is_Plain_Object_Without_Constructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var GeneratorFunction = Object.getPrototypeOf(function* () {}).constructor;
                                                                   class GFn extends GeneratorFunction {}
                                                                   var gfn = new GFn(";");
                                                                   var desc = Object.getOwnPropertyDescriptor(gfn, "prototype");
                                                                   [
                                                                       Object.keys(gfn.prototype).length,
                                                                       gfn.prototype.hasOwnProperty("constructor"),
                                                                       desc.writable,
                                                                       desc.enumerable,
                                                                       desc.configurable
                                                                   ].join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0,false,true,false,false"));
    }

    [Test]
    public void Generator_Throw_Inside_Nested_Try_Catch_Yields_Caught_Value_Before_Finally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       try {
                                                                           yield 1;
                                                                           try {
                                                                               yield 2;
                                                                           } catch (e) {
                                                                               yield e;
                                                                           }
                                                                           yield 3;
                                                                       } finally {
                                                                           yield 4;
                                                                       }
                                                                       yield 5;
                                                                   }
                                                                   var it = g();
                                                                   var error = {};
                                                                   var out = [];
                                                                   out.push(it.next().value);
                                                                   out.push(it.next().value);
                                                                   out.push(it.throw(error).value === error);
                                                                   out.push(it.next().value);
                                                                   out.push(it.next().value);
                                                                   out.push(it.next().value);
                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,2,true,3,4,5"));
    }

    [Test]
    public void Generator_Return_Inside_Nested_Try_Catch_Runs_Finally_Without_Corrupting_Handler_State()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       try {
                                                                           yield 1;
                                                                           try {
                                                                               yield 2;
                                                                           } catch (e) {
                                                                               yield 20;
                                                                           }
                                                                           yield 3;
                                                                       } finally {
                                                                           yield 4;
                                                                       }
                                                                       yield 5;
                                                                   }
                                                                   var it = g();
                                                                   var out = [];
                                                                   out.push(it.next().value);
                                                                   out.push(it.next().value);
                                                                   out.push(it.return(9).value);
                                                                   out.push(it.next().value);
                                                                   out.push(it.next().done);
                                                                   out.join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,2,4,9,true"));
    }

    [Test]
    public void YieldDelegate_Preserves_Inner_IteratorResult_Object_On_Incomplete_Steps()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var results = [{ value: 1 }, { value: 8 }, { value: 34, done: true }];
                                                                   var idx = 0;
                                                                   var iterator = {};
                                                                   iterator[Symbol.iterator] = function() {
                                                                       return {
                                                                           next: function() {
                                                                               return results[idx++];
                                                                           }
                                                                       };
                                                                   };
                                                                   function* g() {
                                                                       yield* iterator;
                                                                   }
                                                                   var it = g();
                                                                   var a = it.next();
                                                                   var b = it.next();
                                                                   [a.value, a.done, b.value, b.done].join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,,8,"));
    }

    [Test]
    public void YieldDelegate_Return_Completion_Flows_Through_Finally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var iterable = {
                                                                       [Symbol.iterator]: function() {
                                                                           return {
                                                                               next: function() { return { done: false }; },
                                                                               return: function(x) { return { done: true, value: x + 1 }; }
                                                                           };
                                                                       }
                                                                   };
                                                                   var trace = [];
                                                                   function* g() {
                                                                       try {
                                                                           yield* iterable;
                                                                           trace.push("body");
                                                                       } finally {
                                                                           trace.push("finally");
                                                                       }
                                                                   }
                                                                   var it = g();
                                                                   it.next();
                                                                   var r = it.return(9);
                                                                   [r.value, r.done, trace.join("|")].join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("10,true,finally"));
    }

    [Test]
    public void Generator_Yield_Conditional_Consequent_Can_Omit_Rhs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       return (yield) ? yield : yield;
                                                                   }
                                                                   let it = g();
                                                                   let a = it.next().value;
                                                                   let b = it.next(false).value;
                                                                   let c = it.next(9).value;
                                                                   (a === undefined) && (b === undefined) && (c === 9);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Generator_Yield_Inside_Template_Expression_Uses_Generator_Context()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let str = '';
                                                                   function* g() {
                                                                       str = `1${ yield }3${ 4 }5`;
                                                                   }
                                                                   let it = g();
                                                                   it.next();
                                                                   it.next(2);
                                                                   str;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("12345"));
    }

    [Test]
    public void Generator_Arguments_Object_Preserves_Extra_Actual_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function* g() {
                                                                       yield arguments[0];
                                                                       yield arguments[1];
                                                                       yield arguments[2];
                                                                       yield arguments[3];
                                                                   }
                                                                   let it = g(23, 45, 33);
                                                                   let a = it.next();
                                                                   let b = it.next();
                                                                   let c = it.next();
                                                                   let d = it.next();
                                                                   a.value === 23 &&
                                                                   b.value === 45 &&
                                                                   c.value === 33 &&
                                                                   d.value === undefined &&
                                                                   a.done === false &&
                                                                   b.done === false &&
                                                                   c.done === false &&
                                                                   d.done === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
