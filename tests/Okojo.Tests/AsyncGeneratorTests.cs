using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AsyncGeneratorTests
{
    [Test]
    public void AsyncGeneratorExpression_IsTaggedAsAsyncGeneratorKind()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let g = async function* AG() {
                                                                       yield await "a";
                                                                   };
                                                                   """));

        var g = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "AG");
        Assert.That(g.Kind, Is.EqualTo(JsBytecodeFunctionKind.AsyncGenerator));
    }

    [Test]
    public void AsyncGenerator_YieldAwait_ProducesPromiseIteratorResults()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var results = [];
                                                                   var iter = (async function*() {
                                                                     yield await "a";
                                                                   })();

                                                                   iter.next().then(function(result) {
                                                                     results.push(result.value);
                                                                     results.push(result.done);
                                                                   });

                                                                   iter.next().then(function(result) {
                                                                     results.push(result.value);
                                                                     results.push(result.done);
                                                                   });

                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var results = realm.Global["results"].AsObject() as JsArray;
        Assert.That(results, Is.Not.Null);
        Assert.That(results!.Length, Is.EqualTo(4));
        Assert.That(results.TryGetElement(0, out var v0), Is.True);
        Assert.That(results.TryGetElement(1, out var v1), Is.True);
        Assert.That(results.TryGetElement(2, out var v2), Is.True);
        Assert.That(results.TryGetElement(3, out var v3), Is.True);
        Assert.That(v0.AsString(), Is.EqualTo("a"));
        Assert.That(v1.IsFalse, Is.True);
        Assert.That(v2.IsUndefined, Is.True);
        Assert.That(v3.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_FunctionPrototype_Is_Used_When_Object_And_Falls_Back_When_Not_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var fn = async function* () {};
                                                                   var intrinsicProto = Object.getPrototypeOf(fn.prototype);
                                                                   var direct = {};
                                                                   fn.prototype = direct;
                                                                   var a = Object.getPrototypeOf(fn()) === direct;
                                                                   fn.prototype = undefined;
                                                                   var b = Object.getPrototypeOf(fn()) === intrinsicProto;
                                                                   [a, b];
                                                                   """));

        realm.Execute(script);

        var result = realm.Accumulator.AsObject() as JsArray;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TryGetElement(0, out var a), Is.True);
        Assert.That(result.TryGetElement(1, out var b), Is.True);
        Assert.That(a.IsTrue, Is.True);
        Assert.That(b.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_DefaultParameterThrow_Throws_At_Call_Time()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   var f = async function*(_ = (function() { throw new Error("x"); }())) {
                                                                     callCount = callCount + 1;
                                                                   };
                                                                   try {
                                                                     f();
                                                                     "no-throw";
                                                                   } catch (e) {
                                                                     callCount;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void AsyncGenerator_DefaultParameterSelfReference_Throws_ReferenceError_At_Call_Time()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   var f = async function*(x = x) {
                                                                     callCount = callCount + 1;
                                                                   };
                                                                   try {
                                                                     f();
                                                                     "no-throw";
                                                                   } catch (e) {
                                                                     e.name + ":" + callCount;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ReferenceError:0"));
    }

    [Test]
    public void AsyncGenerator_DefaultParameterLaterReference_Throws_ReferenceError_At_Call_Time()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   var f = async function*(x = y, y) {
                                                                     callCount = callCount + 1;
                                                                   };
                                                                   try {
                                                                     f();
                                                                     "no-throw";
                                                                   } catch (e) {
                                                                     e.name + ":" + callCount;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ReferenceError:0"));
    }

    [Test]
    public void AsyncGenerator_Object_Is_Created_After_Eager_Parameter_Binding_For_Prototype_Resolution()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*(a = (g.prototype = null)) {}
                                                                   var oldPrototype = g.prototype;
                                                                   var it = g();
                                                                   Object.getPrototypeOf(it) !== oldPrototype;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGeneratorFunctionPrototype_Exposes_AsyncGeneratorPrototype_Via_Prototype_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function* g() {}
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
    public void AsyncGenerator_NamedExpression_StrictInnerArrow_ReassignName_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   let ref = async function * BindingIdentifier() {
                                                                     (() => {
                                                                       BindingIdentifier = 1;
                                                                     })();
                                                                     return BindingIdentifier;
                                                                   };

                                                                   var out;
                                                                   ref().next().then(
                                                                     function(v) { out = "resolved"; },
                                                                     function(e) { out = e.name; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("TypeError"));
    }

    [Test]
    public void AsyncGenerator_NamedExpression_SloppyReassignName_Leaves_FunctionBinding_Intact()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let callCount = 0;
                                                                   let ref = async function * BindingIdentifier() {
                                                                     callCount++;
                                                                     BindingIdentifier = 1;
                                                                     return BindingIdentifier;
                                                                   };

                                                                   var out = [];
                                                                   async function run() {
                                                                     try {
                                                                       let it = await ref();
                                                                       out.push(typeof it);
                                                                       out.push(typeof it.next);
                                                                       let step = await it.next();
                                                                       out.push(step.value === ref);
                                                                       out.push(callCount);
                                                                     } catch (e) {
                                                                       out.push(e.name);
                                                                       out.push(e.message);
                                                                     }
                                                                   }

                                                                   run();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.Length, Is.EqualTo(4));
        Assert.That(outArray.TryGetElement(0, out var t0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var t1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var t2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var t3), Is.True);
        Assert.That(t0.AsString(), Is.EqualTo("object"));
        Assert.That(t1.AsString(), Is.EqualTo("function"));
        Assert.That(t2.IsTrue, Is.True);
        Assert.That(t3.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncGenerator_NamedExpression_SloppyReassignName_Works_Through_Chained_Await_Next()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let callCount = 0;
                                                                   let ref = async function * BindingIdentifier() {
                                                                     callCount++;
                                                                     BindingIdentifier = 1;
                                                                     return BindingIdentifier;
                                                                   };

                                                                   var out;
                                                                   (async function() {
                                                                     try {
                                                                       out = (await (await ref()).next()).value === ref ? "ok:" + callCount : "bad:" + callCount;
                                                                     } catch (e) {
                                                                       out = e.name + ":" + e.message;
                                                                     }
                                                                   })();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok:1"));
    }

    [Test]
    public void AsyncGenerator_NamedExpression_SloppyReassignName_Works_Through_AsyncArrow_Helper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let callCount = 0;
                                                                   let ref = async function * BindingIdentifier() {
                                                                     callCount++;
                                                                     BindingIdentifier = 1;
                                                                     return BindingIdentifier;
                                                                   };

                                                                   var out = "pending";
                                                                   function asyncTest(f) {
                                                                     f().then(
                                                                       function() {},
                                                                       function(e) { out = e.name + ":" + e.message; }
                                                                     );
                                                                   }

                                                                   asyncTest(async () => {
                                                                     out = (await (await ref()).next()).value === ref ? "ok:" + callCount : "bad:" + callCount;
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok:1"));
    }

    [Test]
    public void AsyncGenerator_NamedExpression_SloppyReassignName_Works_As_First_Call_Argument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let callCount = 0;
                                                                   let ref = async function * BindingIdentifier() {
                                                                     callCount++;
                                                                     BindingIdentifier = 1;
                                                                     return BindingIdentifier;
                                                                   };

                                                                   var out = "pending";
                                                                   var assert = {
                                                                     sameValue(a, b) {
                                                                       out = a === b ? "ok:" + callCount : "bad:" + callCount;
                                                                     }
                                                                   };

                                                                   (async () => {
                                                                     assert.sameValue((await (await ref()).next()).value, ref);
                                                                   })().then(
                                                                     function() {},
                                                                     function(e) { out = e.name + ":" + e.message; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok:1"));
    }

    [Test]
    public void AsyncGenerator_YieldDelegate_To_AsyncGenerator_Completes_Through_AsyncIterator_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*() {};
                                                                   var out;
                                                                   (async function*() {
                                                                     yield*
                                                                     g();
                                                                   })().next().then(function(result) {
                                                                     out = result.done;
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Runtime_Exposes_SymbolAsyncIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof Symbol.asyncIterator === "symbol";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Instance_Sees_SymbolAsyncIterator_Through_PrototypeChain()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*() {};
                                                                   typeof g()[Symbol.asyncIterator] === "function";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Frame_Can_Read_SymbolAsyncIterator_Before_YieldDelegate_Call()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*() {};
                                                                   var out;
                                                                   (async function*() {
                                                                     var it = g();
                                                                     out = typeof it[Symbol.asyncIterator];
                                                                   })().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();
        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("function"));
    }

    [Test]
    public void AsyncGenerator_Instance_Can_Call_SymbolAsyncIterator_Directly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*() {};
                                                                   var it = g();
                                                                   it[Symbol.asyncIterator]() === it;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Frame_Can_Call_SymbolAsyncIterator_Directly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var g = async function*() {};
                                                                   var out;
                                                                   (async function*() {
                                                                     var it = g();
                                                                     out = it[Symbol.asyncIterator]() === it;
                                                                   })().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();
        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Declaration_Instance_Sees_SymbolAsyncIterator_Through_PrototypeChain()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function* g() {}
                                                                   typeof g()[Symbol.asyncIterator] === "function";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ClassAsyncGeneratorMethod_ForAwait_Rejects_With_Rejected_Value_And_Then_Closes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   let error = new Error();
                                                   globalThis.out = [];
                                                   globalThis.callCount = 0;

                                                   async function* readFile() {
                                                     yield Promise.reject(error);
                                                     yield "unreachable";
                                                   }

                                                   var C = class {
                                                     async *gen() {
                                                       callCount += 1;
                                                       for await (let line of readFile()) {
                                                         yield line;
                                                       }
                                                     }
                                                   };

                                                   var gen = C.prototype.gen;
                                                   var iter = gen();
                                                   iter.next().then(
                                                     () => out.push("resolved"),
                                                     reason => {
                                                       out.push(reason === error);
                                                       out.push(reason && reason.name);
                                                       iter.next().then(
                                                         ({ done, value }) => out.push(done === true && value === undefined),
                                                         err => out.push(err));
                                                     });
                                                   """,
            "test262/test/language/expressions/class/async-gen-method/yield-promise-reject-next-for-await-of-async-iterator.js");
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);
        realm.Agent.PumpJobs();
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(realm.Global["callCount"].Int32Value, Is.EqualTo(1));
        Assert.That(outArray!.TryGetElement(0, out var t0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var t1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var t2), Is.True);
        Assert.That(t0.IsTrue, Is.True);
        Assert.That(t1.AsString(), Is.EqualTo("Error"));
        Assert.That(t2.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_FunctionDeclaration_Prototype_Is_Preserved_Across_Closure_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function* readFile() {
                                                                     yield 1;
                                                                   }

                                                                   class C {}

                                                                   typeof readFile.prototype[Symbol.asyncIterator] + "|" +
                                                                   typeof readFile()[Symbol.asyncIterator];
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void ClassDeclarationAsyncGeneratorMethod_ForAwait_Rejects_With_Rejected_Value_And_Then_Closes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   let error = new Error();
                                                   globalThis.out = [];
                                                   globalThis.callCount = 0;

                                                   async function* readFile() {
                                                     yield Promise.reject(error);
                                                     yield "unreachable";
                                                   }

                                                   class C {
                                                     async *gen() {
                                                       callCount += 1;
                                                       for await (let line of readFile()) {
                                                         yield line;
                                                       }
                                                     }
                                                   }

                                                   var gen = C.prototype.gen;
                                                   var iter = gen();
                                                   iter.next().then(
                                                     () => out.push("resolved"),
                                                     reason => {
                                                       out.push(reason === error);
                                                       out.push(reason && reason.name);
                                                       iter.next().then(
                                                         ({ done, value }) => out.push(done === true && value === undefined),
                                                         err => out.push(err));
                                                     });
                                                   """,
            "test262/test/language/statements/class/async-gen-method/yield-promise-reject-next-for-await-of-async-iterator.js");
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        for (var i = 0; i < 16; i++)
        {
            realm.Agent.PumpJobs();
            if (outArray!.Length >= 3)
                break;
        }

        Assert.That(realm.Global["callCount"].Int32Value, Is.EqualTo(1));
        Assert.That(outArray!.TryGetElement(0, out var t0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var t1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var t2), Is.True);
        Assert.That(t0.IsTrue, Is.True);
        Assert.That(t1.AsString(), Is.EqualTo("Error"));
        Assert.That(t2.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_Throw_Preserves_Original_Error_When_IteratorReturn_Is_NonCallable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];
                                                                   const bodyError = new Error("body");
                                                                   const asyncIterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { done: false, value: null };
                                                                         },
                                                                         return: true
                                                                       };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     for await (const value of asyncIterable) {
                                                                       throw bodyError;
                                                                     }
                                                                   })().then(
                                                                     () => out.push(false),
                                                                     error => out.push(error === bodyError)
                                                                   );
                                                                   """));

        realm.Execute(script);
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        for (var i = 0; i < 16; i++)
        {
            realm.Agent.PumpJobs();
            if (outArray!.Length >= 1)
                break;
        }

        Assert.That(outArray!.TryGetElement(0, out var result), Is.True);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_Throw_Preserves_Original_Error_When_IteratorReturn_Getter_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];
                                                                   const bodyError = new Error("body");
                                                                   const asyncIterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { done: false, value: null };
                                                                         },
                                                                         get return() {
                                                                           throw new Error("inner");
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     for await (const value of asyncIterable) {
                                                                       throw bodyError;
                                                                     }
                                                                   })().then(
                                                                     () => out.push(false),
                                                                     error => out.push(error === bodyError)
                                                                   );
                                                                   """));

        realm.Execute(script);
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        for (var i = 0; i < 16; i++)
        {
            realm.Agent.PumpJobs();
            if (outArray!.Length >= 1)
                break;
        }

        Assert.That(outArray!.TryGetElement(0, out var result), Is.True);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ClassAsyncGeneratorMethod_YieldRejectedPromise_Rejects_And_Then_Closes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let error = new Error();
                                                                   globalThis.out = [];
                                                                   globalThis.callCount = 0;

                                                                   var C = class {
                                                                     async *gen() {
                                                                       callCount += 1;
                                                                       yield Promise.reject(error);
                                                                       yield "unreachable";
                                                                     }
                                                                   };

                                                                   var iter = C.prototype.gen();
                                                                   iter.next().then(
                                                                     () => out.push("resolved"),
                                                                     reason => {
                                                                       out.push(reason === error);
                                                                       iter.next().then(
                                                                         ({ done, value }) => out.push(done === true && value === undefined),
                                                                         err => out.push(err));
                                                                     });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(realm.Global["callCount"].Int32Value, Is.EqualTo(1));
        Assert.That(outArray!.TryGetElement(0, out var t0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var t1), Is.True);
        Assert.That(t0.IsTrue, Is.True);
        Assert.That(t1.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_YieldDelegate_Falls_Back_To_SyncIterator_When_AsyncIterator_Is_Missing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var iterable = {
                                                                     [Symbol.iterator]() {
                                                                       var done = false;
                                                                       return {
                                                                         next() {
                                                                           if (done) return { value: 9, done: true };
                                                                           done = true;
                                                                           return { value: 4, done: false };
                                                                         }
                                                                       };
                                                                     }
                                                                   };
                                                                   var out = [];
                                                                   var it = (async function*() {
                                                                     return yield* iterable;
                                                                   })();
                                                                   it.next().then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.next().then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.TryGetElement(0, out var v0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var v1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var v2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var v3), Is.True);
        Assert.That(v0.Int32Value, Is.EqualTo(4));
        Assert.That(v1.IsFalse, Is.True);
        Assert.That(v2.Int32Value, Is.EqualTo(9));
        Assert.That(v3.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Return_BrokenPromise_FromSuspendedYield_ResumesThroughCatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];
                                                                   let caughtErr;
                                                                   var g = async function*() {
                                                                     try {
                                                                       log.push("before-yield");
                                                                       yield;
                                                                       log.push("after-yield");
                                                                       return "never";
                                                                     } catch (err) {
                                                                       log.push("caught:" + (err && err.message));
                                                                       caughtErr = err;
                                                                       return 1;
                                                                     } finally {
                                                                       log.push("finally");
                                                                     }
                                                                   };

                                                                   let brokenPromise = Promise.resolve(42);
                                                                   Object.defineProperty(brokenPromise, "constructor", {
                                                                     get: function () {
                                                                       log.push("ctor-get");
                                                                       throw new Error("broken promise");
                                                                     }
                                                                   });

                                                                   var it = g();
                                                                   it.next().then(function () {
                                                                     log.push("after-next");
                                                                     return it.return(brokenPromise);
                                                                   }).then(function (ret) {
                                                                     log.push("resolved:" + ret.value + ":" + ret.done);
                                                                   }, function (err) {
                                                                     log.push("rejected:" + err);
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var log = realm.Global["log"].AsObject() as JsArray;
        Assert.That(log, Is.Not.Null);
        var entries = Enumerable.Range(0, (int)log!.Length)
            .Select(i => log.TryGetElement((uint)i, out var value) ? value.AsString() : "<missing>")
            .ToArray();
        Assert.That(string.Join("|", entries),
            Is.EqualTo("before-yield|after-next|ctor-get|caught:broken promise|finally|resolved:1:true"));
    }

    [Test]
    public void AsyncGenerator_ReturnAwait_RejectedPromise_Preserves_Rejection_Reason()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   let error = new SyntaxError("boom");

                                                                   async function* g() {
                                                                     return await Promise.reject(error);
                                                                   }

                                                                   g().next().then(
                                                                     function () { globalThis.out = "resolved"; },
                                                                     function (reason) { globalThis.out = reason === error ? reason.name : "wrong:" + (reason && reason.name); }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("SyntaxError"));
    }

    [Test]
    public void AsyncGenerator_Return_Queued_During_Execution_Completes_In_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var iter;
                                                                   var executionorder = 0;
                                                                   var valueisset = false;
                                                                   var log = [];

                                                                   async function* g() {
                                                                     iter.return(42).then(function(result) {
                                                                       log.push("return:" + executionorder + ":" + valueisset + ":" + result.value + ":" + result.done);
                                                                       executionorder++;
                                                                     });

                                                                     valueisset = true;
                                                                     yield 1;
                                                                     throw new Error("should not reach");
                                                                   }

                                                                   iter = g();
                                                                   iter.next().then(function(result) {
                                                                     log.push("next1:" + executionorder + ":" + result.value + ":" + result.done);
                                                                     executionorder++;
                                                                     iter.next().then(function(result) {
                                                                       log.push("next2:" + executionorder + ":" + result.value + ":" + result.done);
                                                                       executionorder++;
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var log = realm.Global["log"].AsObject() as JsArray;
        Assert.That(log, Is.Not.Null);
        var entries = Enumerable.Range(0, (int)log!.Length)
            .Select(i => log.TryGetElement((uint)i, out var value) ? value.AsString() : "<missing>")
            .ToArray();
        Assert.That(string.Join("|", entries),
            Is.EqualTo("next1:0:1:false|return:1:true:42:true|next2:2:undefined:true"));
    }

    [Test]
    public void AsyncGenerator_YieldStar_Throw_Awaits_Inner_Async_Result_Before_Resolving_Request()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];

                                                                   var iterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { value: "start", done: false };
                                                                         },
                                                                         throw(arg) {
                                                                           log.push("throw:" + arg);
                                                                           return {
                                                                             then(resolve) {
                                                                               log.push("then");
                                                                               resolve({
                                                                                 get done() {
                                                                                   log.push("done");
                                                                                   return false;
                                                                                 },
                                                                                 get value() {
                                                                                   log.push("value");
                                                                                   return "inner";
                                                                                 }
                                                                               });
                                                                             }
                                                                           };
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   var iter = ({
                                                                     async *gen() {
                                                                       yield* iterable;
                                                                     }
                                                                   }).gen();

                                                                   iter.next().then(function() {
                                                                     iter.throw("x").then(function(step) {
                                                                       out.push(step.value);
                                                                       out.push(step.done);
                                                                       out.push(log.join(","));
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.Length, Is.EqualTo(3));
        Assert.That(outArray.TryGetElement(0, out var value), Is.True);
        Assert.That(outArray.TryGetElement(1, out var done), Is.True);
        Assert.That(outArray.TryGetElement(2, out var trace), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("inner"));
        Assert.That(done.IsFalse, Is.True);
        Assert.That(trace.AsString(), Is.EqualTo("throw:x,then,done,value"));
    }

    [Test]
    public void AsyncGenerator_YieldStar_SecondThrow_DoneTrue_CompletesOuterYieldStar_Without_Reentering_Delegate()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];
                                                                   var iterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       var throwCount = 0;
                                                                       return {
                                                                         next() {
                                                                           log.push("get next");
                                                                           return { value: "start", done: false };
                                                                         },
                                                                         throw(arg) {
                                                                           log.push("throw:" + arg);
                                                                           throwCount++;
                                                                           return {
                                                                             then(resolve) {
                                                                               log.push("then:" + throwCount);
                                                                               resolve(throwCount === 1
                                                                                 ? {
                                                                                     get done() { log.push("done:1"); return false; },
                                                                                     get value() { log.push("value:1"); return "inner"; }
                                                                                   }
                                                                                 : {
                                                                                     get done() { log.push("done:2"); return true; },
                                                                                     get value() { log.push("value:2"); return "final"; }
                                                                                   });
                                                                             }
                                                                           };
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   var iter = ({
                                                                     async *gen() {
                                                                       var v = yield* iterable;
                                                                       log.push("after:" + v);
                                                                       return "done";
                                                                     }
                                                                   }).gen();

                                                                   iter.next().then(function() {
                                                                     iter.throw("x").then(function(step1) {
                                                                       out.push(step1.value);
                                                                       out.push(step1.done);
                                                                       iter.throw("y").then(function(step2) {
                                                                         out.push(step2.value);
                                                                         out.push(step2.done);
                                                                         out.push(log.join(","));
                                                                       });
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.Length, Is.EqualTo(5));
        Assert.That(outArray.TryGetElement(0, out var firstValue), Is.True);
        Assert.That(outArray.TryGetElement(1, out var firstDone), Is.True);
        Assert.That(outArray.TryGetElement(2, out var secondValue), Is.True);
        Assert.That(outArray.TryGetElement(3, out var secondDone), Is.True);
        Assert.That(outArray.TryGetElement(4, out var trace), Is.True);
        Assert.That(firstValue.AsString(), Is.EqualTo("inner"));
        Assert.That(firstDone.IsFalse, Is.True);
        Assert.That(secondValue.AsString(), Is.EqualTo("done"));
        Assert.That(secondDone.IsTrue, Is.True);
        Assert.That(trace.AsString(), Is.EqualTo(
            "get next,throw:x,then:1,done:1,value:1,throw:y,then:2,done:2,value:2,after:final"));
    }

    [Test]
    public void AsyncGenerator_YieldStar_SyncThrow_Awaits_Completed_Throw_Result_Before_Resuming()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];
                                                                   var iterable = {
                                                                     [Symbol.iterator]() {
                                                                       var throwCount = 0;
                                                                       return {
                                                                         next() { return { value: "start", done: false }; },
                                                                         throw(arg) {
                                                                           throwCount++;
                                                                           log.push("throw:" + throwCount + ":" + arg);
                                                                           if (throwCount === 1) {
                                                                             return {
                                                                               get done() { log.push("done:1"); return false; },
                                                                               get value() { log.push("value:1"); return "mid"; }
                                                                             };
                                                                           }

                                                                           return {
                                                                             get done() { log.push("done:2"); return true; },
                                                                             get value() { log.push("value:2"); return "end"; }
                                                                           };
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   var it = (async function*() {
                                                                     var v = yield* iterable;
                                                                     out.push("after:" + v);
                                                                     return "ret";
                                                                   })();

                                                                   it.next().then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.throw("a").then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.throw("b").then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var log = realm.Global["log"].AsObject() as JsArray;
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(log, Is.Not.Null);
        Assert.That(outArray, Is.Not.Null);
        Assert.That(log!.Length, Is.EqualTo(6));
        Assert.That(log.TryGetElement(0, out var log0), Is.True);
        Assert.That(log.TryGetElement(1, out var log1), Is.True);
        Assert.That(log.TryGetElement(2, out var log2), Is.True);
        Assert.That(log.TryGetElement(3, out var log3), Is.True);
        Assert.That(log.TryGetElement(4, out var log4), Is.True);
        Assert.That(log.TryGetElement(5, out var log5), Is.True);
        Assert.That(log0.AsString(), Is.EqualTo("throw:1:a"));
        Assert.That(log1.AsString(), Is.EqualTo("done:1"));
        Assert.That(log2.AsString(), Is.EqualTo("value:1"));
        Assert.That(log3.AsString(), Is.EqualTo("throw:2:b"));
        Assert.That(log4.AsString(), Is.EqualTo("done:2"));
        Assert.That(log5.AsString(), Is.EqualTo("value:2"));
        Assert.That(outArray!.TryGetElement(0, out var out0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var out1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var out2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var out3), Is.True);
        Assert.That(outArray.TryGetElement(4, out var out4), Is.True);
        Assert.That(outArray.TryGetElement(5, out var out5), Is.True);
        Assert.That(outArray.TryGetElement(6, out var out6), Is.True);
        Assert.That(out0.AsString(), Is.EqualTo("start"));
        Assert.That(out1.IsFalse, Is.True);
        Assert.That(out2.AsString(), Is.EqualTo("mid"));
        Assert.That(out3.IsFalse, Is.True);
        Assert.That(out4.AsString(), Is.EqualTo("after:end"));
        Assert.That(out5.AsString(), Is.EqualTo("ret"));
        Assert.That(out6.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_YieldStar_SyncReturn_Awaits_Completed_Return_Result_Before_Resolving()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];
                                                                   var iterable = {
                                                                     [Symbol.iterator]() {
                                                                       var returnCount = 0;
                                                                       return {
                                                                         next() { return { value: "start", done: false }; },
                                                                         return(arg) {
                                                                           returnCount++;
                                                                           log.push("return:" + returnCount + ":" + arg);
                                                                           if (returnCount === 1) {
                                                                             return {
                                                                               get done() { log.push("done:1"); return false; },
                                                                               get value() { log.push("value:1"); return "mid"; }
                                                                             };
                                                                           }

                                                                           return {
                                                                             get done() { log.push("done:2"); return true; },
                                                                             get value() { log.push("value:2"); return "end"; }
                                                                           };
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   var it = (async function*() {
                                                                     yield* iterable;
                                                                   })();

                                                                   it.next().then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.return("a").then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.return("b").then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var log = realm.Global["log"].AsObject() as JsArray;
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(log, Is.Not.Null);
        Assert.That(outArray, Is.Not.Null);
        Assert.That(log!.Length, Is.EqualTo(6));
        Assert.That(log.TryGetElement(0, out var log0), Is.True);
        Assert.That(log.TryGetElement(1, out var log1), Is.True);
        Assert.That(log.TryGetElement(2, out var log2), Is.True);
        Assert.That(log.TryGetElement(3, out var log3), Is.True);
        Assert.That(log.TryGetElement(4, out var log4), Is.True);
        Assert.That(log.TryGetElement(5, out var log5), Is.True);
        Assert.That(log0.AsString(), Is.EqualTo("return:1:a"));
        Assert.That(log1.AsString(), Is.EqualTo("done:1"));
        Assert.That(log2.AsString(), Is.EqualTo("value:1"));
        Assert.That(log3.AsString(), Is.EqualTo("return:2:b"));
        Assert.That(log4.AsString(), Is.EqualTo("done:2"));
        Assert.That(log5.AsString(), Is.EqualTo("value:2"));
        Assert.That(outArray!.TryGetElement(0, out var out0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var out1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var out2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var out3), Is.True);
        Assert.That(outArray.TryGetElement(4, out var out4), Is.True);
        Assert.That(outArray.TryGetElement(5, out var out5), Is.True);
        Assert.That(out0.AsString(), Is.EqualTo("start"));
        Assert.That(out1.IsFalse, Is.True);
        Assert.That(out2.AsString(), Is.EqualTo("mid"));
        Assert.That(out3.IsFalse, Is.True);
        Assert.That(out4.AsString(), Is.EqualTo("end"));
        Assert.That(out5.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_YieldStar_AsyncNext_Uses_Accessor_IteratorResult_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var log = [];
                                                                   var iterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       var count = 0;
                                                                       return {
                                                                         next() {
                                                                           count++;
                                                                           if (count === 1) {
                                                                             return Promise.resolve({
                                                                               get done() { log.push("done:1"); return false; },
                                                                               get value() { log.push("value:1"); return "step-1"; }
                                                                             });
                                                                           }

                                                                           return Promise.resolve({
                                                                             get done() { log.push("done:2"); return true; },
                                                                             get value() { log.push("value:2"); return "step-2"; }
                                                                           });
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   var out = [];
                                                                   var it = (async function*() {
                                                                     var v = yield* iterable;
                                                                     out.push("after:" + v);
                                                                     return "ret";
                                                                   })();

                                                                   it.next().then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   it.next("x").then(function(result) { out.push(result.value); out.push(result.done); });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var log = realm.Global["log"].AsObject() as JsArray;
        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(log, Is.Not.Null);
        Assert.That(outArray, Is.Not.Null);
        Assert.That(log!.Length, Is.EqualTo(4));
        Assert.That(log.TryGetElement(0, out var log0), Is.True);
        Assert.That(log.TryGetElement(1, out var log1), Is.True);
        Assert.That(log.TryGetElement(2, out var log2), Is.True);
        Assert.That(log.TryGetElement(3, out var log3), Is.True);
        Assert.That(log0.AsString(), Is.EqualTo("done:1"));
        Assert.That(log1.AsString(), Is.EqualTo("value:1"));
        Assert.That(log2.AsString(), Is.EqualTo("done:2"));
        Assert.That(log3.AsString(), Is.EqualTo("value:2"));
        Assert.That(outArray!.TryGetElement(0, out var out0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var out1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var out2), Is.True);
        Assert.That(outArray.TryGetElement(3, out var out3), Is.True);
        Assert.That(outArray.TryGetElement(4, out var out4), Is.True);
        Assert.That(out0.AsString(), Is.EqualTo("step-1"));
        Assert.That(out1.IsFalse, Is.True);
        Assert.That(out2.AsString(), Is.EqualTo("after:step-2"));
        Assert.That(out3.AsString(), Is.EqualTo("ret"));
        Assert.That(out4.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ClassMethod_ForAwaitOf_SyncIterator_Awaits_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let error = new Error("x");
                                                                   let iterable = [Promise.reject(error), "unreachable"];
                                                                   globalThis.out = false;

                                                                   class C {
                                                                     async *gen() {
                                                                       for await (let value of iterable) {
                                                                         yield value;
                                                                       }
                                                                     }
                                                                   }

                                                                   let iter = new C().gen();
                                                                   iter.next().then(
                                                                     function() { globalThis.out = false; },
                                                                     function(reason) { globalThis.out = reason === error; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Var_Object_Destructuring_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (var {...rest} of [{ a: 3, b: 4 }]) {
                                                                       globalThis.out = rest.a === 3 && rest.b === 4;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Var_Object_Rest_Skips_NonEnumerable_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   const source = { a: 3, b: 4 };
                                                                   Object.defineProperty(source, "x", { value: 5, enumerable: false });

                                                                   async function *run() {
                                                                     for await (var {...rest} of [source]) {
                                                                       globalThis.out =
                                                                         rest.a === 3 &&
                                                                         rest.b === 4 &&
                                                                         rest.x === undefined &&
                                                                         Object.prototype.propertyIsEnumerable.call(rest, "a") &&
                                                                         Object.prototype.propertyIsEnumerable.call(rest, "b") &&
                                                                         !Object.prototype.hasOwnProperty.call(rest, "x");
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Allows_Var_Object_Destructuring_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   for (var {...rest} of [{ a: 1, b: 2 }]) {
                                                                     globalThis.out = rest.a === 1 && rest.b === 2;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Var_Object_Rest_Skips_NonEnumerable_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   const source = { a: 1, b: 2 };
                                                                   Object.defineProperty(source, "x", { value: 7, enumerable: false });

                                                                   for (var {...rest} of [source]) {
                                                                     globalThis.out =
                                                                       rest.a === 1 &&
                                                                       rest.b === 2 &&
                                                                       rest.x === undefined &&
                                                                       Object.prototype.propertyIsEnumerable.call(rest, "a") &&
                                                                       Object.prototype.propertyIsEnumerable.call(rest, "b") &&
                                                                       !Object.prototype.hasOwnProperty.call(rest, "x");
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Allows_Var_Array_Destructuring_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   for (var [first, second] of [[5, 6]]) {
                                                                     globalThis.out = first === 5 && second === 6;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Var_Array_Rest_Destructuring_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (var [...rest] of [[1, 2, 3]]) {
                                                                       globalThis.out =
                                                                         Array.isArray(rest) &&
                                                                         rest.length === 3 &&
                                                                         rest[0] === 1 &&
                                                                         rest[1] === 2 &&
                                                                         rest[2] === 3;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Allows_Var_Array_Rest_Destructuring_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   for (var [...rest] of [[4, 5, 6]]) {
                                                                     globalThis.out =
                                                                       Array.isArray(rest) &&
                                                                       rest.length === 3 &&
                                                                       rest[0] === 4 &&
                                                                       rest[1] === 5 &&
                                                                       rest[2] === 6;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Let_Object_Property_Array_Default_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (let { w: [x, y, z] = [4, 5, 6] } of [{}]) {
                                                                       globalThis.out = x === 4 && y === 5 && z === 6;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void ForOf_Allows_Let_Object_Property_Array_Default_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   for (let { w: [x, y, z] = [7, 8, 9] } of [{}]) {
                                                                     globalThis.out = x === 7 && y === 8 && z === 9;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Var_Array_Element_Object_Default_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (var [{ x, y, z } = { x: 44, y: 55, z: 66 }] of [[]]) {
                                                                       globalThis.out = x === 44 && y === 55 && z === 66;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Const_Object_Property_Object_Default_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (const { w: { x, y, z } = { x: 4, y: 5, z: 6 } } of [{ w: { x: undefined, z: 7 } }]) {
                                                                       globalThis.out = x === undefined && y === undefined && z === 7;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Const_Object_Property_Array_Default_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   async function *run() {
                                                                     for await (const { w: [x, y, z] = [4, 5, 6] } of [{ w: [7, undefined] }]) {
                                                                       globalThis.out = x === 7 && y === undefined && z === undefined;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Assignment_Object_Rest_Target()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   let rest;

                                                                   async function *run() {
                                                                     for await ({ ...rest } of [{ a: 3, b: 4 }]) {
                                                                       globalThis.out = rest.a === 3 && rest.b === 4;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Assignment_Object_Property_Nested_Array_Target()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   let y;

                                                                   async function *run() {
                                                                     for await ({ x: [y] } of [{ x: [321] }]) {
                                                                       globalThis.out = y === 321;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Const_Array_Rest_Binding_And_Consumes_Iterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;

                                                                   const iter = (function* () {
                                                                     yield 1;
                                                                     yield 2;
                                                                   })();

                                                                   async function* run() {
                                                                     for await (const [...rest] of [iter]) {
                                                                       globalThis.out =
                                                                         Array.isArray(rest) &&
                                                                         rest.length === 2 &&
                                                                         rest[0] === 1 &&
                                                                         rest[1] === 2 &&
                                                                         iter.next().done === true;
                                                                       return;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Const_Object_Rest_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   const source = { a: 3, b: 4 };
                                                                   Object.defineProperty(source, "x", { value: 5, enumerable: false });

                                                                   async function *run() {
                                                                     for await (const {...rest} of [source]) {
                                                                       globalThis.out =
                                                                         rest.a === 3 &&
                                                                         rest.b === 4 &&
                                                                         rest.x === undefined &&
                                                                         Object.prototype.propertyIsEnumerable.call(rest, "a") &&
                                                                         Object.prototype.propertyIsEnumerable.call(rest, "b") &&
                                                                         !Object.prototype.hasOwnProperty.call(rest, "x");
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Var_Array_Binding_Propagates_IteratorStep_Error()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   const boom = new Error("boom");
                                                                   const g = {
                                                                     [Symbol.iterator]() {
                                                                       return {
                                                                         next() {
                                                                           throw boom;
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   async function *gen() {
                                                                     for await (var [x] of [g]) {
                                                                       return;
                                                                     }
                                                                   }

                                                                   gen().next().then(
                                                                     function () { globalThis.out = "resolved"; },
                                                                     function (reason) { globalThis.out = reason === boom ? reason.message : "wrong"; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("boom"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Var_Array_Binding_Propagates_Custom_IteratorStep_Error()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";

                                                                   function Test262Error(message) {
                                                                     this.message = message || "";
                                                                   }

                                                                   const g = {
                                                                     [Symbol.iterator]() {
                                                                       return {
                                                                         next() {
                                                                           throw new Test262Error("boom");
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   async function *gen() {
                                                                     for await (var [x] of [g]) {
                                                                       return;
                                                                     }
                                                                   }

                                                                   gen().next().then(
                                                                     function () { globalThis.out = "resolved"; },
                                                                     function (reason) { globalThis.out = reason && reason.constructor === Test262Error ? "ok" : "wrong"; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Var_Array_Rest_Binding_Propagates_Custom_IteratorStep_Error()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";

                                                                   function Test262Error(message) {
                                                                     this.message = message || "";
                                                                   }

                                                                   const iter = (function* () {
                                                                     throw new Test262Error("boom");
                                                                   })();

                                                                   async function *gen() {
                                                                     for await (var [...x] of [iter]) {
                                                                       return;
                                                                     }
                                                                   }

                                                                   gen().next().then(
                                                                     function () { globalThis.out = "resolved"; },
                                                                     function (reason) { globalThis.out = reason && reason.constructor === Test262Error ? "ok" : "wrong"; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Let_Object_Default_Function_Assigns_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";

                                                                   async function *run() {
                                                                     for await (let { fn = function() {} } of [{}]) {
                                                                       globalThis.out = fn.name;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("fn"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Let_Object_Default_InferredNames_DoNotCapture_WrappedHeadBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";

                                                                   async function *run() {
                                                                     for await (let { fn = function() {}, arrow = () => {}, gen = function*() {}, asyncFn = async function() {} } of [{}]) {
                                                                       globalThis.out =
                                                                         fn.name + "," +
                                                                         arrow.name + "," +
                                                                         gen.name + "," +
                                                                         asyncFn.name;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("fn,arrow,gen,asyncFn"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Object_Default_Function_Assigns_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   let fnexp;

                                                                   async function *run() {
                                                                     for await ({ fnexp = function() {} } of [{}]) {
                                                                       globalThis.out = fnexp.name;
                                                                     }
                                                                   }

                                                                   run().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("fnexp"));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Array_Element_Nested_Object_Default_Yield_Parses_And_Executes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];
                                                                   let x;

                                                                   async function *fn() {
                                                                     for await ([{ x = yield }] of [[{}]]) {
                                                                       globalThis.out.push(x);
                                                                     }
                                                                   }

                                                                   let iter = fn();
                                                                   iter.next().then(function (result) {
                                                                     globalThis.out.push(result.done);
                                                                     iter.next(4).then(function (nextResult) {
                                                                       globalThis.out.push(nextResult.done);
                                                                       globalThis.out.push(x);
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 8; i++)
        {
            realm.Agent.PumpJobs();
            if (realm.Global["out"].AsObject() is JsArray { Length: >= 3 })
                break;
        }

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(outArray!.TryGetElement(0, out var done0), Is.True);
        Assert.That(done0.IsFalse, Is.True);
        Assert.That(outArray.TryGetElement(outArray.Length - 1, out var xValue), Is.True);
        Assert.That(xValue.Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Array_Element_Computed_Target_With_Yield_Executes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];
                                                                   let value = [[22]];
                                                                   let x = {};

                                                                   async function *fn() {
                                                                     for await ([[x[yield]]] of [value]) {
                                                                       globalThis.out.push("loop");
                                                                     }
                                                                   }

                                                                   let iter = fn();
                                                                   iter.next().then(function (result) {
                                                                     globalThis.out.push(result.done);
                                                                     iter.next("prop").then(function (nextResult) {
                                                                       globalThis.out.push(nextResult.done);
                                                                       globalThis.out.push(x.prop);
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 8; i++)
        {
            realm.Agent.PumpJobs();
            if (realm.Global["out"].AsObject() is JsArray { Length: >= 3 })
                break;
        }

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.TryGetElement(0, out var done0), Is.True);
        Assert.That(done0.IsFalse, Is.True);
        Assert.That(outArray.TryGetElement(outArray.Length - 1, out var storedValue), Is.True);
        Assert.That(storedValue.Int32Value, Is.EqualTo(22));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Array_Rest_Computed_Target_With_Yield_Stores_Rest_Array()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];
                                                                   let x = {};

                                                                   async function *fn() {
                                                                     for await ([...x[yield]] of [[33, 44, 55]]) {
                                                                     }
                                                                   }

                                                                   let iter = fn();
                                                                   iter.next().then(function (result) {
                                                                     globalThis.out.push(result.done);
                                                                     globalThis.out.push(x.prop);
                                                                     iter.next("prop").then(function (nextResult) {
                                                                       globalThis.out.push(x.prop && x.prop.length);
                                                                       globalThis.out.push(x.prop && x.prop[0]);
                                                                       globalThis.out.push(x.prop && x.prop[1]);
                                                                       globalThis.out.push(x.prop && x.prop[2]);
                                                                       globalThis.out.push(nextResult.done);
                                                                     });
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 12; i++)
        {
            realm.Agent.PumpJobs();
            if (realm.Global["out"].AsObject() is JsArray { Length: >= 7 })
                break;
        }

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.TryGetElement(0, out var firstDone), Is.True);
        Assert.That(firstDone.IsFalse, Is.True);
        Assert.That(outArray.TryGetElement(1, out var beforeAssign), Is.True);
        Assert.That(beforeAssign.IsUndefined, Is.True);
        Assert.That(outArray.TryGetElement(2, out var lengthValue), Is.True);
        Assert.That(lengthValue.Int32Value, Is.EqualTo(3));
        Assert.That(outArray.TryGetElement(3, out var v0), Is.True);
        Assert.That(v0.Int32Value, Is.EqualTo(33));
        Assert.That(outArray.TryGetElement(4, out var v1), Is.True);
        Assert.That(v1.Int32Value, Is.EqualTo(44));
        Assert.That(outArray.TryGetElement(5, out var v2), Is.True);
        Assert.That(v2.Int32Value, Is.EqualTo(55));
        Assert.That(outArray.TryGetElement(6, out var secondDone), Is.True);
        Assert.That(secondDone.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_Assignment_Object_Rest_To_Property_Stores_Rest_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = false;
                                                                   let src = {};

                                                                   async function fn() {
                                                                     for await ({ ...src.y } of [{ x: 1, y: 2 }]) {
                                                                       globalThis.out =
                                                                         src.y &&
                                                                         src.y.x === 1 &&
                                                                         src.y.y === 2 &&
                                                                         Object.prototype.propertyIsEnumerable.call(src, "y");
                                                                     }
                                                                   }

                                                                   fn();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Default_To_Arguments_Uses_Function_Arguments_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];

                                                                   async function *fn() {
                                                                     for await ({ arguments = 4 } of [{}]) {
                                                                       globalThis.out.push(arguments);
                                                                     }
                                                                   }

                                                                   fn().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.TryGetElement(0, out var innerValue), Is.True);
        Assert.That(innerValue.Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Array_Element_Object_Default_Yield_Parses()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   async function * fn() {
                                                     for await ([ {} = yield ] of [[]]) {
                                                     }
                                                   }
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(1));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Return_Closes_Sync_Iterator_And_Propagates_NonObject_Return()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.results = [];
                                                                   let nextCount = 0;
                                                                   let returnCount = 0;
                                                                   let iterator = {
                                                                     next() {
                                                                       nextCount += 1;
                                                                       return { done: false, value: undefined };
                                                                     },
                                                                     return() {
                                                                       returnCount += 1;
                                                                       return null;
                                                                     }
                                                                   };
                                                                   let iterable = { [Symbol.iterator]() { return iterator; } };

                                                                   async function * fn() {
                                                                     for await ([ {} = yield ] of [iterable]) {
                                                                     }
                                                                   }

                                                                   let iter = fn();
                                                                   iter.next().then(function(result) {
                                                                     results.push(nextCount, returnCount, result.value, result.done);
                                                                     return iter.return();
                                                                   }).then(
                                                                     function() { results.push('fulfilled'); },
                                                                     function(error) { results.push(returnCount, error.constructor === TypeError); }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var results = (JsArray)realm.Global["results"].AsObject()!;
        Assert.That(results.Length, Is.EqualTo(6));
        Assert.That(results.TryGetElement(0, out var r0), Is.True);
        Assert.That(results.TryGetElement(1, out var r1), Is.True);
        Assert.That(results.TryGetElement(3, out var r3), Is.True);
        Assert.That(results.TryGetElement(4, out var r4), Is.True);
        Assert.That(results.TryGetElement(5, out var r5), Is.True);
        Assert.That(r0.Int32Value, Is.EqualTo(1));
        Assert.That(r1.Int32Value, Is.EqualTo(0));
        Assert.That(r3.IsFalse, Is.True);
        Assert.That(r4.Int32Value, Is.EqualTo(1));
        Assert.That(r5.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_Break_Closes_AsyncIterator_When_Return_Is_Null()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.results = [];
                                                                   var iterationCount = 0;
                                                                   var returnGets = 0;

                                                                   var iterable = {};
                                                                   iterable[Symbol.asyncIterator] = function() {
                                                                     return {
                                                                       next: function() {
                                                                         return { value: 1, done: false };
                                                                       },
                                                                       get return() {
                                                                         returnGets += 1;
                                                                         return null;
                                                                       }
                                                                     };
                                                                   };

                                                                   (async function() {
                                                                     for await (var _ of iterable) {
                                                                       iterationCount += 1;
                                                                       break;
                                                                     }
                                                                     results.push(iterationCount, returnGets);
                                                                   })();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var results = (JsArray)realm.Global["results"].AsObject()!;
        Assert.That(results.Length, Is.EqualTo(2));
        Assert.That(results.TryGetElement(0, out var r0), Is.True);
        Assert.That(results.TryGetElement(1, out var r1), Is.True);
        Assert.That(r0.Int32Value, Is.EqualTo(1));
        Assert.That(r1.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_AsyncFromSync_Uses_PromiseResolve_Constructor_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   async function f() {
                                                                     var p = Promise.resolve(0);
                                                                     Object.defineProperty(p, "constructor", {
                                                                       get() {
                                                                         throw new Error();
                                                                       }
                                                                     });
                                                                     actual.push("start");
                                                                     for await (var x of [p]);
                                                                     actual.push("never reached");
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"));

                                                                   f().catch(() => actual.push("catch"));
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        Assert.That(actual.Length, Is.EqualTo(4));
        Assert.That(actual.TryGetElement(0, out var a0), Is.True);
        Assert.That(actual.TryGetElement(1, out var a1), Is.True);
        Assert.That(actual.TryGetElement(2, out var a2), Is.True);
        Assert.That(actual.TryGetElement(3, out var a3), Is.True);
        Assert.That(a0.AsString(), Is.EqualTo("start"));
        Assert.That(a1.AsString(), Is.EqualTo("tick 1"));
        Assert.That(a2.AsString(), Is.EqualTo("tick 2"));
        Assert.That(a3.AsString(), Is.EqualTo("catch"));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_AsyncFromSync_Return_Without_Value_Does_Not_Pass_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.returnArgumentsLength = -1;

                                                                   var syncIterator = {
                                                                     [Symbol.iterator]() {
                                                                       return this;
                                                                     },
                                                                     next() {
                                                                       return { done: false };
                                                                     },
                                                                     return() {
                                                                       returnArgumentsLength = arguments.length;
                                                                       return { done: true };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     for await (let _ of syncIterator) {
                                                                       break;
                                                                     }
                                                                   })();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["returnArgumentsLength"].Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_AsyncFromSync_Return_Null_Completes_AsyncTest_Wrapper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.iterationCount = 0;
                                                                   globalThis.returnGets = 0;

                                                                   function asyncTest(testFunc) {
                                                                     try {
                                                                       testFunc().then(
                                                                         function () { done = "ok"; },
                                                                         function (error) { done = error; }
                                                                       );
                                                                     } catch (syncError) {
                                                                       done = syncError;
                                                                     }
                                                                   }

                                                                   var syncIterator = {
                                                                     [Symbol.iterator]() {
                                                                       return this;
                                                                     },
                                                                     next() {
                                                                       return { value: 1, done: false };
                                                                     },
                                                                     get return() {
                                                                       returnGets += 1;
                                                                       return null;
                                                                     }
                                                                   };

                                                                   asyncTest(async function() {
                                                                     for await (let _ of syncIterator) {
                                                                       iterationCount += 1;
                                                                       break;
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 16 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("ok"));
        Assert.That(realm.Global["iterationCount"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["returnGets"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_Throw_Preserves_Original_Error_When_Return_Get_Fails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.iterationCount = 0;

                                                                   function asyncTest(testFunc) {
                                                                     try {
                                                                       testFunc().then(
                                                                         function () { done = "unexpected"; },
                                                                         function (error) { done = error && error.name || typeof error; }
                                                                       );
                                                                     } catch (syncError) {
                                                                       done = syncError && syncError.name || typeof syncError;
                                                                     }
                                                                   }

                                                                   const asyncIterable = {};
                                                                   asyncIterable[Symbol.asyncIterator] = function() {
                                                                     return {
                                                                       next() {
                                                                         return { done: false, value: null };
                                                                       },
                                                                       get return() {
                                                                         throw { name: "inner error" };
                                                                       }
                                                                     };
                                                                   };

                                                                   asyncTest(async function() {
                                                                     for await (const x of asyncIterable) {
                                                                       iterationCount += 1;
                                                                       throw new Error("should not be overriden");
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 16 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("Error"));
        Assert.That(realm.Global["iterationCount"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_AsyncFromSync_Closes_Iterator_When_Value_Promise_Rejects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.returnCount = 0;
                                                                   globalThis.caught = false;

                                                                   const syncIterator = {
                                                                     [Symbol.iterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { value: Promise.reject("reject"), done: false };
                                                                         },
                                                                         return() {
                                                                           returnCount += 1;
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     try {
                                                                       for await (let _ of syncIterator);
                                                                     } catch (e) {
                                                                       caught = e === "reject";
                                                                     }
                                                                   })();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["returnCount"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["caught"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_AbuptCompletion_Awaits_Pending_IteratorReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.log = "";
                                                                   globalThis.done = "pending";
                                                                   globalThis.finishClose = null;

                                                                   function asyncTest(testFunc) {
                                                                     try {
                                                                       testFunc().then(
                                                                         function () { done = "ok"; },
                                                                         function (error) { done = error && error.message ? error.message : String(error); }
                                                                       );
                                                                     } catch (syncError) {
                                                                       done = syncError && syncError.message ? syncError.message : String(syncError);
                                                                     }
                                                                   }

                                                                   const asyncIterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return Promise.resolve({ done: false, value: 1 });
                                                                         },
                                                                         return() {
                                                                           log += "R";
                                                                           return new Promise(function(resolve) {
                                                                             finishClose = function() {
                                                                               log += "r";
                                                                               resolve({ done: true });
                                                                             };
                                                                           });
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   asyncTest(async function() {
                                                                     for await (const x of asyncIterable) {
                                                                       log += "B";
                                                                       throw new Error("boom");
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 16 && realm.Global["finishClose"].IsNull; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["log"].AsString(), Is.EqualTo("BR"));
        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("pending"));

        _ = realm.Eval("finishClose();");
        for (var i = 0; i < 16 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["log"].AsString(), Is.EqualTo("BRr"));
        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("boom"));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_HeadAssignmentThrow_Closes_AsyncIterator_Once()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.returnCount = 0;
                                                                   globalThis.bodyCount = 0;

                                                                   const target = {
                                                                     set value(v) {
                                                                       throw new Error("head boom");
                                                                     }
                                                                   };

                                                                   const asyncIterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { done: false, value: 1 };
                                                                         },
                                                                         return() {
                                                                           returnCount += 1;
                                                                           return Promise.resolve({ done: true });
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     try {
                                                                       for await (target.value of asyncIterable) {
                                                                         bodyCount += 1;
                                                                       }
                                                                     } catch (e) {
                                                                       done = [e.message, returnCount, bodyCount].join("|");
                                                                     }
                                                                   })();
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("head boom|1|0"));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_HeadAssignmentThrow_Closes_AsyncIterator_Once_In_NonSimple_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.done = "pending";
                                                                   globalThis.returnCount = 0;
                                                                   globalThis.bodyCount = 0;

                                                                   const target = {
                                                                     set value(v) {
                                                                       throw new Error("head boom");
                                                                     }
                                                                   };

                                                                   const asyncIterable = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { done: false, value: 1 };
                                                                         },
                                                                         return() {
                                                                           returnCount += 1;
                                                                           return Promise.resolve({ done: true });
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   (async function() {
                                                                     try {
                                                                       for await (target.value of asyncIterable) {
                                                                         bodyCount += 1;
                                                                         break;
                                                                       }
                                                                     } catch (e) {
                                                                       done = [e.message, returnCount, bodyCount].join("|");
                                                                     }
                                                                   })();
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["done"].AsString() == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["done"].AsString(), Is.EqualTo("head boom|1|0"));
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_SyncIterator_Uses_Await_PromiseResolve_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   async function f() {
                                                                     var p = Promise.resolve(0);
                                                                     actual.push("pre");
                                                                     for await (var x of [p]) {
                                                                       actual.push("loop");
                                                                     }
                                                                     actual.push("post");
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"))
                                                                     .then(() => actual.push("tick 3"))
                                                                     .then(() => actual.push("tick 4"));

                                                                   Object.defineProperty(Promise.prototype, "constructor", {
                                                                     get() {
                                                                       actual.push("constructor");
                                                                       return Promise;
                                                                     },
                                                                     configurable: true,
                                                                   });

                                                                   f();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        Assert.That(actual.Length, Is.EqualTo(10));
        string[] expected =
        [
            "pre",
            "constructor",
            "constructor",
            "tick 1",
            "tick 2",
            "loop",
            "constructor",
            "tick 3",
            "tick 4",
            "post"
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_CustomAsyncIterator_With_PromiseNext_Uses_Expected_Ticks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   function toAsyncIterator(iterable) {
                                                                     return {
                                                                       [Symbol.asyncIterator]() {
                                                                         var iter = iterable[Symbol.iterator]();
                                                                         return {
                                                                           next() {
                                                                             return Promise.resolve(iter.next());
                                                                           }
                                                                         };
                                                                       }
                                                                     };
                                                                   }

                                                                   async function f() {
                                                                     var p = Promise.resolve(0);
                                                                     actual.push("pre");
                                                                     for await (var x of toAsyncIterator([p])) {
                                                                       actual.push("loop");
                                                                     }
                                                                     actual.push("post");
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"));

                                                                   Object.defineProperty(Promise.prototype, "constructor", {
                                                                     get() {
                                                                       actual.push("constructor");
                                                                       return Promise;
                                                                     },
                                                                     configurable: true,
                                                                   });

                                                                   f();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        string[] expected =
        [
            "pre",
            "constructor",
            "tick 1",
            "loop",
            "constructor",
            "tick 2",
            "post"
        ];
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncFunction_ForAwaitOf_CustomAsyncIterator_With_SyncNext_Uses_Expected_Ticks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   function toAsyncIterator(iterable) {
                                                                     return {
                                                                       [Symbol.asyncIterator]() {
                                                                         return iterable[Symbol.iterator]();
                                                                       }
                                                                     };
                                                                   }

                                                                   async function f() {
                                                                     var p = Promise.resolve(0);
                                                                     actual.push("pre");
                                                                     for await (var x of toAsyncIterator([p])) {
                                                                       actual.push("loop");
                                                                     }
                                                                     actual.push("post");
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"));

                                                                   Object.defineProperty(Promise.prototype, "constructor", {
                                                                     get() {
                                                                       actual.push("constructor");
                                                                       return Promise;
                                                                     },
                                                                     configurable: true,
                                                                   });

                                                                   f();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        string[] expected =
        [
            "pre",
            "tick 1",
            "loop",
            "tick 2",
            "post"
        ];
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncGenerator_Implicit_And_Direct_Return_Undefined_Settle_Before_Explicit_Awaited_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function* g1() {}
                                                                   async function* g2() { return; }
                                                                   async function* g3() { return undefined; }
                                                                   async function* g4() { return void 0; }

                                                                   globalThis.actual = [];

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"));

                                                                   g1().next().then(v => actual.push("g1 ret"));
                                                                   g2().next().then(v => actual.push("g2 ret"));
                                                                   g3().next().then(v => actual.push("g3 ret"));
                                                                   g4().next().then(v => actual.push("g4 ret"));
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        string[] expected =
        [
            "tick 1",
            "g1 ret",
            "g2 ret",
            "tick 2",
            "g3 ret",
            "g4 ret"
        ];
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncGenerator_Return_From_Suspended_Yield_Awaits_Thenable_Once_With_Correct_Ticks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   async function* f() {
                                                                     actual.push("start");
                                                                     yield 123;
                                                                     actual.push("stop - never reached");
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"));

                                                                   var it = f();
                                                                   it.next();
                                                                   it.return({
                                                                     get then() {
                                                                       actual.push("get then");
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        string[] expected =
        [
            "start",
            "tick 1",
            "get then",
            "tick 2"
        ];
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncGenerator_YieldStar_AsyncIterator_Does_Not_Unwrap_Promise_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.same = false;
                                                                   var innerPromise = Promise.resolve("unwrapped value");

                                                                   var asyncIter = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return this;
                                                                     },
                                                                     next() {
                                                                       return {
                                                                         done: false,
                                                                         value: innerPromise,
                                                                       };
                                                                     }
                                                                   };

                                                                   async function* f() {
                                                                     yield* asyncIter;
                                                                   }

                                                                   f().next().then(v => {
                                                                     globalThis.same = v.value === innerPromise;
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["same"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_YieldStar_Return_Without_DelegateMethod_Awaits_Return_Value_Again()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   var asyncIter = {
                                                                     [Symbol.asyncIterator]() {
                                                                       return this;
                                                                     },
                                                                     next() {
                                                                       return { done: false };
                                                                     },
                                                                     get return() {
                                                                       actual.push("get return");
                                                                     }
                                                                   };

                                                                   async function* f() {
                                                                     actual.push("start");
                                                                     yield* asyncIter;
                                                                   }

                                                                   Promise.resolve(0)
                                                                     .then(() => actual.push("tick 1"))
                                                                     .then(() => actual.push("tick 2"))
                                                                     .then(() => actual.push("tick 3"));

                                                                   var it = f();
                                                                   it.next();
                                                                   it.return({
                                                                     get then() {
                                                                       actual.push("get then");
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        string[] expected =
        [
            "start",
            "tick 1",
            "get then",
            "tick 2",
            "get return",
            "get then",
            "tick 3"
        ];
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual.TryGetElement((uint)i, out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo(expected[i]));
        }
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Assignment_Array_Default_To_Arguments_Uses_Function_Arguments_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];

                                                                   async function *fn() {
                                                                     for await ([arguments = 4, eval = 5] of [[]]) {
                                                                       globalThis.out.push(arguments);
                                                                       globalThis.out.push(eval);
                                                                     }
                                                                   }

                                                                   fn().next();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = realm.Global["out"].AsObject() as JsArray;
        Assert.That(outArray, Is.Not.Null);
        Assert.That(outArray!.TryGetElement(0, out var argumentsValue), Is.True);
        Assert.That(outArray.TryGetElement(1, out var evalValue), Is.True);
        Assert.That(argumentsValue.Int32Value, Is.EqualTo(4));
        Assert.That(evalValue.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void AsyncGenerator_YieldStar_SyncIterator_Next_PromiseResolve_Abrupt_Closes_Iterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.returnCount = 0;
                                                                   globalThis.caught = false;

                                                                   function *gen() {
                                                                     try {
                                                                       const p = Promise.resolve("FAIL");
                                                                       Object.defineProperty(p, "constructor", {
                                                                         get() {
                                                                           throw new Error("boom");
                                                                         }
                                                                       });
                                                                       yield p;
                                                                     } finally {
                                                                       returnCount += 1;
                                                                     }
                                                                   }

                                                                   async function *iter() {
                                                                     yield* gen();
                                                                   }

                                                                   iter().next().then(
                                                                     function () { globalThis.caught = false; },
                                                                     function (error) { globalThis.caught = error && error.message === "boom"; }
                                                                   );
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["returnCount"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["caught"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_YieldStar_SyncIterator_Throw_RejectedPromise_Closes_Iterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.returnCount = 0;
                                                                   globalThis.caught = false;

                                                                   function Reject() {}

                                                                   var obj = {
                                                                     [Symbol.iterator]() {
                                                                       return {
                                                                         next() {
                                                                           return { value: 1, done: false };
                                                                         },
                                                                         throw() {
                                                                           return {
                                                                             value: Promise.reject(new Reject()),
                                                                             done: false
                                                                           };
                                                                         },
                                                                         return() {
                                                                           returnCount += 1;
                                                                         }
                                                                       };
                                                                     }
                                                                   };

                                                                   async function* asyncg() {
                                                                     return yield* obj;
                                                                   }

                                                                   let iter = asyncg();
                                                                   iter.next().then(function() {
                                                                     iter.throw().then(
                                                                       function() { globalThis.caught = false; },
                                                                       function(error) { globalThis.caught = error instanceof Reject; }
                                                                     );
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["returnCount"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["caught"].IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Allows_Let_ExpressionStatement_Followed_By_Block_Across_Newline()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   async function* f() {
                                                     for await (var x of []) let
                                                     {}
                                                   }
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(1));
    }

    [Test]
    public void AsyncGenerator_Yield_Thenable_Resolve_Function_Has_Length_One()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = [];

                                                                   var thenable = {
                                                                     then: function(resolve) {
                                                                       resolve(resolve);
                                                                     },
                                                                   };

                                                                   var iter = (async function*() {
                                                                     yield thenable;
                                                                   }());

                                                                   iter.next().then(function(result) {
                                                                     var resolve = result.value;
                                                                     out.push(typeof resolve);
                                                                     out.push(resolve.length);
                                                                     out.push(resolve.name);
                                                                   });
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var outArray = (JsArray)realm.Global["out"].AsObject()!;
        Assert.That(outArray.TryGetElement(0, out var t0), Is.True);
        Assert.That(outArray.TryGetElement(1, out var t1), Is.True);
        Assert.That(outArray.TryGetElement(2, out var t2), Is.True);
        Assert.That(t0.AsString(), Is.EqualTo("function"));
        Assert.That(t1.Int32Value, Is.EqualTo(1));
        Assert.That(t2.AsString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void AsyncGenerator_ForAwaitOf_Object_Rest_Parses_When_Rhs_Array_Contains_Getter_Object()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   async function *fn() {
                                                     for await (let {...x} of [{ get v() { return 2; } }]) {
                                                       x.v;
                                                     }
                                                   }
                                                   """);

        Assert.That(program.Statements.Count, Is.EqualTo(1));
    }

    [Test]
    public void AsyncGenerator_Return_On_SuspendedStart_Unwraps_Promise_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.actual = [];

                                                                   async function* g() {
                                                                     throw new Error("unreachable");
                                                                   }

                                                                   var resolve;
                                                                   var promise = new Promise(function(resolver) { resolve = resolver; });
                                                                   var it = g();

                                                                   it.return(promise).then(function(result) {
                                                                     actual.push(result.value);
                                                                     actual.push(result.done);
                                                                   });

                                                                   resolve("unwrapped");
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        var actual = (JsArray)realm.Global["actual"].AsObject()!;
        Assert.That(actual.TryGetElement(0, out var v0), Is.True);
        Assert.That(actual.TryGetElement(1, out var v1), Is.True);
        Assert.That(v0.AsString(), Is.EqualTo("unwrapped"));
        Assert.That(v1.IsTrue, Is.True);
    }

    [Test]
    public void AsyncGenerator_Return_On_Completed_Rejected_PromiseResolve_Path_Rejects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.message = "";

                                                                   let unblock;
                                                                   let blocking = new Promise(function(resolve) { unblock = resolve; });

                                                                   async function* g() {
                                                                     await blocking;
                                                                   }

                                                                   var it = g();
                                                                   var brokenPromise = Promise.resolve(42);
                                                                   Object.defineProperty(brokenPromise, "constructor", {
                                                                     get: function() {
                                                                       throw new Error("broken promise");
                                                                     }
                                                                   });

                                                                   it.next().then(function() {
                                                                     it.return(brokenPromise).then(
                                                                       function() { message = "resolved"; },
                                                                       function(error) { message = error.message; }
                                                                     );
                                                                   });

                                                                   unblock();
                                                                   """));

        realm.Execute(script);
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["message"].AsString(), Is.EqualTo("broken promise"));
    }

    [Test]
    public void SloppyBlockAsyncGeneratorDeclaration_ForAwaitBody_IsInitializedBeforeCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   {
                                                                     let iterCount = 0;
                                                                     async function * fn() {
                                                                       for await ({ x: unresolvable } of [{}]) {
                                                                         iterCount += 1;
                                                                       }
                                                                     }

                                                                     fn().next().then(function() {
                                                                       out = unresolvable === undefined && iterCount === 1;
                                                                     }, function(err) {
                                                                       out = err.name;
                                                                     });
                                                                   }
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["out"].TryGetString(out var outStr) && outStr == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }

    [Test]
    public void SloppyBlockAsyncFunctionDeclaration_ForAwaitBody_IsInitializedBeforeCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "pending";
                                                                   {
                                                                     let iterCount = 0;
                                                                     async function fn() {
                                                                       for await ({ x: unresolvable } of [{}]) {
                                                                         iterCount += 1;
                                                                       }
                                                                     }

                                                                     fn().then(function() {
                                                                       out = unresolvable === undefined && iterCount === 1;
                                                                     }, function(err) {
                                                                       out = err.name;
                                                                     });
                                                                   }
                                                                   """));

        realm.Execute(script);
        for (var i = 0; i < 20 && realm.Global["out"].TryGetString(out var outStr) && outStr == "pending"; i++)
            realm.Agent.PumpJobs();

        Assert.That(realm.Global["out"].IsTrue, Is.True);
    }
}
