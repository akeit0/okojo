using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SharedArrayBufferAtomicsFeatureTests
{
    [Test]
    public void SharedArrayBuffer_And_Atomics_Globals_Are_Installed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sabDesc = Object.getOwnPropertyDescriptor(globalThis, "SharedArrayBuffer");
            const atomicsDesc = Object.getOwnPropertyDescriptor(globalThis, "Atomics");
            typeof SharedArrayBuffer === "function" &&
            typeof Atomics === "object" &&
            sabDesc.writable === true &&
            sabDesc.enumerable === false &&
            sabDesc.configurable === true &&
            atomicsDesc.writable === true &&
            atomicsDesc.enumerable === false &&
            atomicsDesc.configurable === true &&
            Object.prototype.toString.call(Atomics) === "[object Atomics]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SharedArrayBuffer_Constructs_Grows_And_Slices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sab = new SharedArrayBuffer(4, { maxByteLength: 8 });
            const view = new Uint8Array(sab);
            view.set([1, 2, 3, 4]);
            sab.grow(8);
            const slice = sab.slice(1, 3);

            Object.getPrototypeOf(sab) === SharedArrayBuffer.prototype &&
            Object.prototype.toString.call(sab) === "[object SharedArrayBuffer]" &&
            sab.byteLength === 8 &&
            sab.growable === true &&
            sab.maxByteLength === 8 &&
            typeof SharedArrayBuffer[Symbol.species] === "undefined" &&
            slice.byteLength === 2 &&
            Object.getPrototypeOf(slice) === SharedArrayBuffer.prototype &&
            new Uint8Array(slice)[0] === 2 &&
            new Uint8Array(slice)[1] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Atomics_ReadModifyWrite_Works_For_Int32_And_BigInt64()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const i32 = new Int32Array(new SharedArrayBuffer(8));
            const i64 = new BigInt64Array(new SharedArrayBuffer(16));

            Atomics.store(i32, 0, 3);
            const addOld = Atomics.add(i32, 0, 2);
            const xorOld = Atomics.xor(i32, 0, 7);
            const cmpOld = Atomics.compareExchange(i32, 0, 2, 9);

            Atomics.store(i64, 0, 5n);
            const exchangeOld = Atomics.exchange(i64, 0, 9n);
            const andOld = Atomics.and(i64, 0, 3n);

            addOld === 3 &&
            xorOld === 5 &&
            cmpOld === 2 &&
            Atomics.load(i32, 0) === 9 &&
            exchangeOld === 5n &&
            andOld === 9n &&
            Atomics.load(i64, 0) === 1n &&
            Atomics.isLockFree(1) === true &&
            Atomics.isLockFree(2) === true &&
            Atomics.isLockFree(3) === false &&
            Atomics.isLockFree(4) === true &&
            Atomics.isLockFree(8) === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Atomics_Wait_And_WaitAsync_Immediate_Paths_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const i32 = new Int32Array(new SharedArrayBuffer(4));
            Atomics.store(i32, 0, 1);

            const notEqual = Atomics.wait(i32, 0, 0);
            const timedOut = Atomics.wait(i32, 0, 1, 0);
            const waitAsyncNotEqual = Atomics.waitAsync(i32, 0, 0);
            const waitAsyncTimedOut = Atomics.waitAsync(i32, 0, 1, 0);

            notEqual === "not-equal" &&
            timedOut === "timed-out" &&
            waitAsyncNotEqual.async === false &&
            waitAsyncNotEqual.value === "not-equal" &&
            waitAsyncTimedOut.async === false &&
            waitAsyncTimedOut.value === "timed-out";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Atomics_Operates_On_NonShared_Integer_TypedArrays_Except_WaitNotify()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const view = new Int32Array(new ArrayBuffer(4));
            const old = Atomics.add(view, 0, 1);
            const loaded = Atomics.load(view, 0);
            const notifyCount = Atomics.notify(view, 0, 1);

            old === 0 &&
            loaded === 1 &&
            notifyCount === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Atomics_Wait_And_Notify_Work_Across_Worker_Agent()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.DefaultRealm;

        _ = mainRealm.Eval("""
                           globalThis.reports = [];
                           globalThis.w = createWorker();
                           onmessage = function (e) { reports.push(e.data); };
                           """);

        var worker = engine.Agents.Single(agent => agent.Kind == JsAgentKind.Worker);
        worker.MainRealm.Eval("""
                              onmessage = function (e) {
                                const i32 = new Int32Array(e.data);
                                Atomics.add(i32, 1, 1);
                                postMessage("ready");
                                const status = Atomics.wait(i32, 0, 0, 1000);
                                postMessage(status + ":" + Atomics.load(i32, 0));
                              };
                              """);

        using var cts = new CancellationTokenSource();
        var thread = StartWorkerRunner(worker, cts);
        try
        {
            _ = mainRealm.Eval("""
                               globalThis.sab = new SharedArrayBuffer(8);
                               globalThis.i32 = new Int32Array(sab);
                               w.postMessage(sab);
                               """);

            Assert.That(SpinUntil(() =>
            {
                mainRealm.PumpJobs();
                return mainRealm.Eval("reports.length").Int32Value == 1;
            }), Is.True, "worker did not report wait readiness");
            Assert.That(mainRealm.Eval("reports[0]").AsString(), Is.EqualTo("ready"));
            Assert.That(mainRealm.Eval("Atomics.load(i32, 1)").Int32Value, Is.EqualTo(1));
            Assert.That(SpinUntil(() => HasSharedWaiterAtInt32Index(mainRealm, "sab", 0)),
                Is.True, "worker did not register shared waiter");

            _ = mainRealm.Eval("""
                               Atomics.store(i32, 0, 1);
                               Atomics.notify(i32, 0, 1);
                               """);

            Assert.That(SpinUntil(() =>
            {
                mainRealm.PumpJobs();
                return mainRealm.Eval("reports.length").Int32Value == 2;
            }), Is.True, "worker did not report wake result");

            Assert.That(mainRealm.Eval("reports[1]").AsString(), Is.EqualTo("ok:1"));
        }
        finally
        {
            cts.Cancel();
            Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker runner did not stop in time");
        }
    }

    [Test]
    public void Atomics_WaitAsync_Works_Across_Worker_Agent()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.DefaultRealm;

        _ = mainRealm.Eval("""
                           globalThis.reports = [];
                           globalThis.w = createWorker();
                           onmessage = function (e) { reports.push(e.data); };
                           """);

        var worker = engine.Agents.Single(agent => agent.Kind == JsAgentKind.Worker);
        worker.MainRealm.Eval("""
                              onmessage = function (e) {
                                const i32 = new Int32Array(e.data);
                                Atomics.add(i32, 1, 1);
                                postMessage("ready");
                                Atomics.waitAsync(i32, 0, 0).value.then(function (status) {
                                  postMessage(status + ":" + Atomics.load(i32, 0));
                                });
                              };
                              """);

        using var cts = new CancellationTokenSource();
        var thread = StartWorkerRunner(worker, cts);
        try
        {
            _ = mainRealm.Eval("""
                               globalThis.sab = new SharedArrayBuffer(8);
                               globalThis.i32 = new Int32Array(sab);
                               w.postMessage(sab);
                               """);

            Assert.That(SpinUntil(() =>
            {
                mainRealm.PumpJobs();
                return mainRealm.Eval("reports.length").Int32Value == 1;
            }), Is.True, "worker did not report waitAsync readiness");
            Assert.That(mainRealm.Eval("reports[0]").AsString(), Is.EqualTo("ready"));
            Assert.That(mainRealm.Eval("Atomics.load(i32, 1)").Int32Value, Is.EqualTo(1));

            _ = mainRealm.Eval("""
                               Atomics.store(i32, 0, 1);
                               Atomics.notify(i32, 0, 1);
                               """);

            Assert.That(SpinUntil(() =>
            {
                mainRealm.PumpJobs();
                return mainRealm.Eval("reports.length").Int32Value == 2;
            }), Is.True, "worker did not report waitAsync result");

            Assert.That(mainRealm.Eval("reports[1]").AsString(), Is.EqualTo("ok:1"));
        }
        finally
        {
            cts.Cancel();
            Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker runner did not stop in time");
        }
    }

    [Test]
    public void Atomics_WaitAsync_BigInt_NotEqual_Reports_Immediately_Across_Worker_Agent()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.DefaultRealm;

        _ = mainRealm.Eval("""
                           globalThis.reports = [];
                           globalThis.w = createWorker();
                           onmessage = function (e) { reports.push(e.data); };
                           """);

        var worker = engine.Agents.Single(agent => agent.Kind == JsAgentKind.Worker);
        worker.MainRealm.Eval("""
                              onmessage = function (e) {
                                const i64 = new BigInt64Array(e.data);
                                Atomics.add(i64, 1, 1n);
                                postMessage(String(Atomics.store(i64, 0, 42n)));
                                postMessage(String(Atomics.waitAsync(i64, 0, 0n).value));
                              };
                              """);

        using var cts = new CancellationTokenSource();
        var thread = StartWorkerRunner(worker, cts);
        try
        {
            _ = mainRealm.Eval("""
                               globalThis.sab = new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT * 4);
                               globalThis.i64 = new BigInt64Array(sab);
                               w.postMessage(sab);
                               """);

            Assert.That(SpinUntil(() => mainRealm.Eval("Atomics.load(i64, 1)").AsBigInt().Value == 1), Is.True,
                "worker did not reach immediate BigInt waitAsync path");

            Assert.That(SpinUntil(() =>
            {
                mainRealm.PumpJobs();
                return mainRealm.Eval("reports.length").Int32Value == 2;
            }), Is.True, "worker did not report immediate BigInt waitAsync results");

            Assert.That(mainRealm.Eval("reports[0]").AsString(), Is.EqualTo("42"));
            Assert.That(mainRealm.Eval("reports[1]").AsString(), Is.EqualTo("not-equal"));
        }
        finally
        {
            cts.Cancel();
            Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker runner did not stop in time");
        }
    }

    private static Thread StartWorkerRunner(JsAgent worker, CancellationTokenSource cts)
    {
        var runner = new JsAgentRunner(worker);
        var thread = new Thread(() => runner.Run(cts.Token));
        thread.Start();
        return thread;
    }

    private static bool SpinUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }

        return condition();
    }

    private static bool HasSharedWaiterAtInt32Index(JsRealm realm, string globalName, uint index)
    {
        var value = realm.Eval(globalName);
        Assert.That(value.TryGetObject(out var obj), Is.True, $"global {globalName} is not an object");
        Assert.That(obj, Is.TypeOf<JsArrayBufferObject>(), $"global {globalName} is not a SharedArrayBuffer");

        var buffer = (JsArrayBufferObject)obj!;
        var storage = buffer.GetSharedStorage();
        var byteIndex = index * sizeof(int);

        lock (storage.SyncRoot)
        {
            return storage.WaitersByByteIndex.TryGetValue(byteIndex, out var waiters) && waiters.Count > 0;
        }
    }
}
