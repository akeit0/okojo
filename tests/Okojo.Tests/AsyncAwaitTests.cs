using Microsoft.Extensions.Time.Testing;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AsyncAwaitTests
{
    [Test]
    public void AsyncAwait_Context()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                function awaitBench() {
                    globalThis.out = 0;
                    async function f(x) {return x;}
                    (async function () {
                        let s = 0;
                        for (let i = 0; i < 100; i++) {
                            s += await f(i);
                        }
                        globalThis.out = s;
                    })();
                }

                awaitBench();
                globalThis.out
                """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(4950));
    }

    [Test]
    public void AsyncAwait_NonPromiseValue_Resolves()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       return await 41;
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(41));
    }

    [Test]
    public void AsyncAwait_PromiseResolve_Resolves()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       return await Promise.resolve(5);
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void AsyncAwait_RejectedPromise_CatchInFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await Promise.reject(7);
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 1;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(8));
    }

    [Test]
    public void AsyncAwait_Thenable_Fulfilled_Resolves()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       return await { then: function (resolve, reject) { resolve(33); } };
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(33));
    }

    [Test]
    public void AsyncAwait_Thenable_Rejected_GoesToCatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await { then: function (resolve, reject) { reject(11); } };
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 1;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(12));
    }

    [Test]
    public void AsyncAwait_ThenGetterThrows_GoesToCatch()
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

        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await t;
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 1;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void AsyncAwait_Thenable_ResolveThenReject_FirstWins()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       return await {
                                                                           then: function (resolve, reject) {
                                                                               resolve(4);
                                                                               reject(99);
                                                                           }
                                                                       };
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void AsyncAwait_Thenable_ResolveThenThrow_StillResolves()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       return await {
                                                                           then: function (resolve, reject) {
                                                                               resolve(6);
                                                                               throw 88;
                                                                           }
                                                                       };
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void AsyncAwait_SetTimeout_SequentialAwaits_ResumeInOrder()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       const a = await new Promise(function (resolve) { setTimeout(resolve, 10, 1); });
                                                                       const b = await new Promise(function (resolve) { setTimeout(resolve, 20, 2); });
                                                                       return a + b;
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(20));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void AsyncAwait_SetTimeout_Rejection_CaughtInAsync()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve, reject) { setTimeout(reject, 5, 7); });
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 2;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void AsyncAwait_Finally_Runs_OnSuccess_AndPreservesReturn()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.side = 0;
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve) { setTimeout(resolve, 5, 3); });
                                                                           return 10;
                                                                       } finally {
                                                                           globalThis.side = globalThis.side + 1;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();

        Assert.That(realm.Global["side"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void AsyncAwait_Finally_Runs_OnRejection_AndCatchObservesError()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.side = 0;
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve, reject) { setTimeout(reject, 5, 7); });
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 1;
                                                                       } finally {
                                                                           globalThis.side = globalThis.side + 1;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();

        Assert.That(realm.Global["side"].Int32Value, Is.EqualTo(1));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(8));
    }

    [Test]
    public void AsyncAwait_FinallyReturn_Overrides_ThrowFromCatch()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve, reject) { setTimeout(reject, 1, 5); });
                                                                           return 0;
                                                                       } catch (e) {
                                                                           throw e + 100;
                                                                       } finally {
                                                                           return 42;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void AsyncAwait_FinallyThrow_Overrides_Return()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve) { setTimeout(resolve, 1, 2); });
                                                                           return 9;
                                                                       } finally {
                                                                           throw 33;
                                                                       }
                                                                   }
                                                                   f().catch(function (e) { globalThis.out = e; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(33));
    }

    [Test]
    public void AsyncAwait_NestedTryFinally_WithTimerAwait_Ordering()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.trace = "";
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           try {
                                                                               await new Promise(function (resolve) { setTimeout(resolve, 3, 1); });
                                                                               globalThis.trace = globalThis.trace + "A";
                                                                           } finally {
                                                                               globalThis.trace = globalThis.trace + "B";
                                                                           }
                                                                       } finally {
                                                                           globalThis.trace = globalThis.trace + "C";
                                                                       }
                                                                       return 7;
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(3));
        realm.PumpJobs();

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("ABC"));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void AsyncAwait_SuspendedPromise_DoesNotResumeBeforeTimerFires()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.trace = "";
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       globalThis.trace = globalThis.trace + "S";
                                                                       const v = await new Promise(function (resolve) { setTimeout(resolve, 10, 5); });
                                                                       globalThis.trace = globalThis.trace + "R";
                                                                       return v + 1;
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("S"));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(9));
        realm.PumpJobs();
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("S"));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("SR"));
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void AsyncAwait_NestedSuspendedAwait_ResumesOuterAfterInner()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   globalThis.inner = async function () {
                                                                       return await new Promise(function (resolve) { setTimeout(resolve, 7, 4); });
                                                                   };
                                                                   async function outer() {
                                                                       const v = await globalThis.inner();
                                                                       return v + 3;
                                                                   }
                                                                   outer().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(6));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void AsyncAwait_ConcurrentSuspendedPromises_CompleteByTimerOrder()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.trace = "";
                                                                   async function a() {
                                                                       const v = await new Promise(function (resolve) { setTimeout(resolve, 5, 1); });
                                                                       globalThis.trace = globalThis.trace + "A" + v;
                                                                   }
                                                                   async function b() {
                                                                       const v = await new Promise(function (resolve) { setTimeout(resolve, 10, 2); });
                                                                       globalThis.trace = globalThis.trace + "B" + v;
                                                                   }
                                                                   a();
                                                                   b();
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo(string.Empty));

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("A1"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("A1B2"));
    }

    [Test]
    public void AsyncAwait_SuspendedRejection_PropagatesToCatch()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function f() {
                                                                       try {
                                                                           await new Promise(function (resolve, reject) { setTimeout(reject, 8, 10); });
                                                                           return 0;
                                                                       } catch (e) {
                                                                           return e + 5;
                                                                       }
                                                                   }
                                                                   f().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(7));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(15));
    }
}
