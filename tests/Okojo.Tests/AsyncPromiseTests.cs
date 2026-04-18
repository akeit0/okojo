using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AsyncPromiseTests
{
    [Test]
    public void AsyncFunction_ReturnsPromise_AndThenHandlerRuns()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function f() {
                                                                       return 41;
                                                                   }
                                                                   globalThis.out = 0;
                                                                   globalThis.p = f();
                                                                   globalThis.p.then(function (v) { globalThis.out = v + 1; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["p"].TryGetObject(out var obj), Is.True);
        Assert.That(obj, Is.TypeOf<JsPromiseObject>());
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void AsyncFunction_Throw_RejectsPromise_AndCatchHandlerRuns()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   async function f() {
                                                                       throw 7;
                                                                   }
                                                                   globalThis.out = 0;
                                                                   f().catch(function (e) { globalThis.out = e; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void PromiseResolve_Thenable_Assimilates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   const thenable = {
                                                                       then: function (resolve, reject) { resolve(21); }
                                                                   };
                                                                   Promise.resolve(thenable).then(function (v) { globalThis.out = v * 2; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void PromiseResolve_ThenGetterThrows_RejectsWithThrownValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var thenable = new JsPlainObject(realm);
        var getter = new JsHostFunction(realm,
            static (in info) =>
            {
                throw new JsRuntimeException(JsErrorKind.InternalError, "then getter throw", "TEST_THROW",
                    JsValue.FromInt32(9));
            }, "then_getter", 0);
        thenable.DefineAccessorProperty("then", getter, null,
            JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.Open);
        realm.Global["t"] = JsValue.FromObject(thenable);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   Promise.resolve(t).catch(function (e) { globalThis.out = e; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void PromiseResolve_Thenable_MultipleResolveReject_UsesFirstCallOnly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   const thenable = {
                                                                       then: function (resolve, reject) {
                                                                           resolve(1);
                                                                           reject(9);
                                                                           resolve(2);
                                                                       }
                                                                   };
                                                                   Promise.resolve(thenable).then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseResolve_Thenable_NestedChain_AssimilatesToFinalValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   const t1 = { then: function (resolve, reject) { resolve({ then: function (r2, rj2) { r2(5); } }); } };
                                                                   Promise.resolve(t1).then(function (v) { globalThis.out = v + 1; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void PromiseReject_WithoutHandler_RaisesUnhandledRejectionEvent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var observed = JsValue.Undefined;
        realm.UnhandledRejection += value => observed = value;

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Promise.reject(3);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(observed.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void PromiseReject_WithCatch_DoesNotRaiseUnhandledRejectionEvent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var unhandled = false;
        realm.UnhandledRejection += _ => unhandled = true;

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   Promise.reject(3).catch(function (e) { return e; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(unhandled, Is.False);
    }

    [Test]
    public void PromiseCatch_IsGenericOverThenables()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   var thenable = {
                                                                       then: function (onFulfilled, onRejected) {
                                                                           globalThis.out = typeof onFulfilled + "," + typeof onRejected;
                                                                           return 17;
                                                                       }
                                                                   };
                                                                   globalThis.result = Promise.prototype.catch.call(thenable, function () {});
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("undefined,function"));
        Assert.That(realm.Global["result"].Int32Value, Is.EqualTo(17));
    }

    [Test]
    public void PromiseFinally_IsInstalledAsNonEnumerableMethod()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var desc = Object.getOwnPropertyDescriptor(Promise.prototype, "finally");
                                                                   globalThis.out = [typeof Promise.prototype.finally, desc.writable, desc.enumerable, desc.configurable].join(",");
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("function,true,false,true"));
    }

    [Test]
    public void PromiseResolve_QueuesForeignThenableInvocation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   var thenable = {
                                                                       then: function (resolve) {
                                                                           globalThis.out += "c";
                                                                           resolve("ok");
                                                                       }
                                                                   };
                                                                   var p = Promise.resolve(thenable);
                                                                   globalThis.out += "b";
                                                                   p.then(function (value) { globalThis.out += value; });
                                                                   globalThis.out = "a" + globalThis.out;
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("abcok"));
    }

    [Test]
    public void PromiseTry_ForwardsArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   Promise.try(function () {
                                                                       globalThis.out = Array.prototype.join.call(arguments, ",");
                                                                   }, 1, 2, 3);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("1,2,3"));
    }

    [Test]
    public void PromiseRace_ResolvesFromFirstSettledInput()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   var p1 = Promise.resolve(1);
                                                                   var p2 = new Promise(function () {});
                                                                   Promise.race([p1, p2]).then(function (value) { globalThis.out += value; });
                                                                   Promise.resolve().then(function () { globalThis.out += "a"; }).then(function () { globalThis.out += "b"; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("a1b"));
    }

    [Test]
    public void PromiseAll_PrimitiveInputs_RemainAsync()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   Promise.all([1]).then(function (values) { globalThis.out += values[0]; });
                                                                   Promise.resolve().then(function () { globalThis.out += "a"; }).then(function () { globalThis.out += "b"; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].ToString(), Is.EqualTo("a1b"));
    }

    [Test]
    public void PromiseAll_UsesOverriddenResolveWhenPresent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.calls = 0;
                                                                   var originalResolve = Promise.resolve;
                                                                   Promise.resolve = function (value) {
                                                                       globalThis.calls++;
                                                                       return originalResolve.call(this, value);
                                                                   };
                                                                   Promise.all([1]);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["calls"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseThen_UsesOverriddenThenOnReturnedPromise()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   var value = {};
                                                                   var reject;
                                                                   var thenable = new Promise(function (resolve) { resolve(); });
                                                                   var p1 = new Promise(function (_, r) { reject = r; });
                                                                   thenable.then = function (resolve) { resolve(value); };
                                                                   var p2 = p1.then(function () {}, function () { return thenable; });
                                                                   p2.then(function (x) { globalThis.out = x === value ? 1 : 2; }, function () { globalThis.out = 3; });
                                                                   reject();
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseCatch_CoercesPrimitiveReceiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   Boolean.prototype.then = function () { globalThis.out += 1; };
                                                                   Promise.prototype.catch.call(true);
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseWithResolvers_UsesReceiverConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   class SubPromise extends Promise {}
                                                                   var instance = Promise.withResolvers.call(SubPromise);
                                                                   globalThis.out = instance.promise instanceof SubPromise && instance.promise.constructor === SubPromise ? 1 : 0;
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseAny_IteratorValueAbrupt_PreservesThrownValue_AndDoesNotClose()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.same = 0;
                                                                   globalThis.callCount = 0;
                                                                   globalThis.returnCount = 0;
                                                                   var error = { tag: 1 };
                                                                   var poisoned = { done: false };
                                                                   Object.defineProperty(poisoned, "value", {
                                                                       get() {
                                                                           globalThis.callCount++;
                                                                           throw error;
                                                                       }
                                                                   });
                                                                   var iterable = {
                                                                       [Symbol.iterator]() {
                                                                           globalThis.callCount++;
                                                                           return {
                                                                               next() {
                                                                                   globalThis.callCount++;
                                                                                   return poisoned;
                                                                               },
                                                                               return() {
                                                                                   globalThis.returnCount++;
                                                                                   return {};
                                                                               }
                                                                           };
                                                                       }
                                                                   };
                                                                   Promise.any(iterable).then(
                                                                       function () { globalThis.same = -1; },
                                                                       function (reason) { globalThis.same = reason === error ? 1 : 0; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["same"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["callCount"].Int32Value, Is.EqualTo(3));
        Assert.That(realm.Global["returnCount"].Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void PromiseAny_PoisonedIteratorGetter_PreservesThrownValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.same = 0;
                                                                   var error = { tag: 1 };
                                                                   var iterable = [];
                                                                   Object.defineProperty(iterable, Symbol.iterator, {
                                                                       get() {
                                                                           throw error;
                                                                       }
                                                                   });
                                                                   Promise.any(iterable).then(
                                                                       function () { globalThis.same = -1; },
                                                                       function (reason) { globalThis.same = reason === error ? 1 : 0; }
                                                                   );
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["same"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void PromiseAny_Test262StyleError_PreservesIdentity_AndPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.same = 0;
                                                                   globalThis.instance = 0;
                                                                   globalThis.callCount = 0;
                                                                   globalThis.returnCount = 0;
                                                                   function Test262Error(message) {
                                                                       this.message = message || "";
                                                                   }
                                                                   let error = new Test262Error("x");
                                                                   let poisoned = { done: false };
                                                                   Object.defineProperty(poisoned, "value", {
                                                                       get() {
                                                                           globalThis.callCount++;
                                                                           throw error;
                                                                       }
                                                                   });
                                                                   let iterable = {
                                                                       [Symbol.iterator]() {
                                                                           globalThis.callCount++;
                                                                           return {
                                                                               next() {
                                                                                   globalThis.callCount++;
                                                                                   return poisoned;
                                                                               },
                                                                               return() {
                                                                                   globalThis.returnCount++;
                                                                                   return {};
                                                                               }
                                                                           };
                                                                       }
                                                                   };
                                                                   Promise.any(iterable).then(() => {
                                                                       globalThis.same = -1;
                                                                   }, (reason) => {
                                                                       globalThis.same = reason === error ? 1 : 0;
                                                                       globalThis.instance = reason instanceof Test262Error ? 1 : 0;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["same"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["instance"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["callCount"].Int32Value, Is.EqualTo(3));
        Assert.That(realm.Global["returnCount"].Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void PromiseAny_Test262StyleIteratorGetterThrow_PreservesPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.instance = 0;
                                                                   globalThis.proto = 0;
                                                                   function Test262Error(message) {
                                                                       this.message = message || "";
                                                                   }
                                                                   let poison = [];
                                                                   Object.defineProperty(poison, Symbol.iterator, {
                                                                       get() {
                                                                           throw new Test262Error("x");
                                                                       }
                                                                   });
                                                                   Promise.any(poison).then(() => {
                                                                       globalThis.instance = -1;
                                                                   }, (error) => {
                                                                       globalThis.instance = error instanceof Test262Error ? 1 : 0;
                                                                       globalThis.proto = Object.getPrototypeOf(error) === Test262Error.prototype ? 1 : 0;
                                                                   });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["instance"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["proto"].Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void MultipleHoistedFunctions_PreserveConstructorPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.instance = 0;
                                                                   globalThis.proto = 0;
                                                                   function Test262Error(message) {
                                                                       this.message = message || "";
                                                                   }
                                                                   function assert(mustBeTrue, message) {
                                                                       if (mustBeTrue === true) {
                                                                           return;
                                                                       }
                                                                       throw new Test262Error(message);
                                                                   }
                                                                   Test262Error.prototype.toString = function () {
                                                                       return "Test262Error: " + this.message;
                                                                   };
                                                                   let error = new Test262Error("x");
                                                                   globalThis.instance = error instanceof Test262Error ? 1 : 0;
                                                                   globalThis.proto = Object.getPrototypeOf(error) === Test262Error.prototype ? 1 : 0;
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["instance"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["proto"].Int32Value, Is.EqualTo(1));
    }
}
