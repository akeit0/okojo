using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class HostPromiseCapabilityTests
{
    [Test]
    public void PromiseTryPreservesIdentityAndInvokesCallbackBeforeConstruction()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        Assert.That(
            realm
                .Evaluate(
                    """
                    const order = [];
                    class P extends Promise { constructor(executor) { order.push('construct'); super(executor); } }
                    const p = P.resolve(1);
                    order.length = 0;
                    const same = P.try(() => { order.push('callback'); return p; }) === p;
                    P.try(() => { order.push('callback'); return 42; });
                    same && order.join(',') === 'callback,callback,construct'
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void PromiseTryRejectsNonCallableCallbackAndPropagatesConversionErrorsSynchronously()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        realm.Evaluate(
            "Promise.try(null).catch(e => globalThis.rejected = e instanceof TypeError);"
        );
        Assert.That(realm.Evaluate("rejected").IsTrue, Is.True);
        Assert.That(
            realm
                .Evaluate(
                    """
                    const reason = {};
                    const p = Promise.resolve(1);
                    Object.defineProperty(p, 'constructor', { get() { throw reason; } });
                    let caught = false;
                    try { Promise.try(() => p); } catch (e) { caught = e === reason; }
                    caught;
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void PromiseResolveAndTryAcceptMatchingNonConstructorObject()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        Assert.That(
            realm
                .Evaluate(
                    """
                    const p = Promise.resolve(1);
                    const receiver = {};
                    p.constructor = receiver;
                    Promise.resolve.call(receiver, p) === p && Promise.try.call(receiver, () => p) === p;
                    """
                )
                .IsTrue,
            Is.True
        );
    }

    private sealed class Trace : IDebuggerSession
    {
        internal readonly List<string> Instructions = [];

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint) =>
            Instructions.Add($"{checkpoint.ProgramCounter}: {checkpoint.CurrentOpcode}");
    }

    [Test]
    public void NativePromiseAdoptionObservesPrototypeThenWithVmTrace()
    {
        var trace = new Trace();
        using var runtime = JsRuntime
            .CreateBuilder()
            .UseAgent(agent =>
            {
                agent.DebuggerSession = trace;
                agent.SetCheckInterval(1);
            })
            .Build();
        var realm = runtime.MainRealm;
        realm.Evaluate(
            """
            const source = Promise.resolve('original');
            const observe = Promise.prototype.then;
            Promise.prototype.then = function(resolve) { resolve('custom'); };
            const result = new Promise(resolve => resolve(source));
            observe.call(result, value => { globalThis.observed = value; });
            """
        );
        Assert.That(trace.Instructions, Is.Not.Empty);
        TestContext.Out.WriteLine(string.Join("\n", trace.Instructions));
        Assert.That(realm.Global["observed"].AsString(), Is.EqualTo("custom"));
    }

    [Test]
    public void IntrinsicPromiseConversionChecksConstructorAndPreservesIdentity()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        var promise = realm.CreateResolvedPromise(42);
        Assert.That(realm.CreateResolvedPromise(promise), Is.EqualTo(promise));
        realm.Global["value"] = promise;
        realm.Evaluate(
            "Object.defineProperty(value, 'constructor', {get() { throw 'constructor'; }});"
        );
        var error = Assert.Throws<JsRuntimeException>(() => realm.CreateResolvedPromise(promise));
        Assert.That(error!.ThrownValue!.Value.AsString(), Is.EqualTo("constructor"));
    }

    [Test]
    public void RelatedRealmPromiseConversionCreatesPromiseInTargetRealm()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        var other = runtime.CreateRealm();
        var promise = other.CreateResolvedPromise(42);
        var converted = realm.CreateResolvedPromise(promise);
        Assert.That(converted, Is.Not.EqualTo(promise));
        Assert.That(converted.AsObject().Realm, Is.SameAs(realm));
    }

    [Test]
    public void ResolutionLocksBeforeThenableSettlesAndReactionsAreJobs()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        var capability = realm.CreatePromiseCapability();
        realm.Global["result"] = capability.Promise;
        var thenable = realm.Evaluate("({ then(resolve) { globalThis.complete = resolve; } })");
        var seen = new List<string>();
        capability.Observe(value => seen.Add(value.AsString()), _ => seen.Add("rejected"));
        capability.Resolve(thenable);
        capability.Reject("late");
        Assert.That(seen, Is.Empty);
        realm.Evaluate("0");
        Assert.That(seen, Is.Empty);
        realm.Evaluate("complete('first')");
        Assert.That(seen, Is.EqualTo(new[] { "first" }));
        Assert.That(realm.Evaluate("result instanceof Promise").IsTrue, Is.True);
    }

    [Test]
    public void NativePromiseAdoptionPreservesThenableJobOrdering()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        realm.Evaluate(
            """
            globalThis.order = [];
            const source = Promise.resolve(1);
            const target = new Promise(resolve => resolve(source));
            target.then(() => order.push('adopted'));
            Promise.resolve().then(() => order.push('one'))
                .then(() => order.push('two')).then(() => order.push('three'));
            """
        );
        Assert.That(
            realm.Evaluate("order.join(',')").AsString(),
            Is.EqualTo("one,two,adopted,three")
        );
    }

    [Test]
    public void SelfResolutionRejectsBeforeOwnThenGetter()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        realm.Evaluate(
            """
            let resolve;
            const promise = new Promise(r => resolve = r);
            globalThis.readThen = false;
            Object.defineProperty(promise, 'then', {get() { readThen = true; return null; }});
            resolve(promise);
            Promise.prototype.then.call(promise, () => globalThis.result = 'fulfilled', e => globalThis.result = e.name);
            """
        );
        Assert.That(realm.Evaluate("!readThen && result === 'TypeError'").IsTrue, Is.True);
    }

    [Test]
    public void HostObservationAddsOneReactionJobWithoutPromiseConversion()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        var promise = realm.CreateResolvedPromise(42);
        realm.Global["value"] = promise;
        realm.Evaluate(
            "Object.defineProperty(value, 'constructor', {get() {throw 'constructor';}}); value.then = () => { throw 'then'; };"
        );
        var order = new List<string>();
        realm.Agent.EnqueuePromiseJob(() => order.Add("before"));
        realm.ObservePromise(
            promise,
            _ => order.Add("observed"),
            _ => Assert.Fail("Unexpected rejection")
        );
        realm.Agent.EnqueuePromiseJob(() => order.Add("after"));
        Assert.That(order, Is.Empty);
        realm.Agent.RunPromiseJobs();
        Assert.That(order, Is.EqualTo(new[] { "before", "observed", "after" }));
    }

    [Test]
    public void HostCreationAndObservationIgnoreMutablePromiseProperties()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        realm.Evaluate(
            "Promise.prototype.then = () => { throw 'then'; }; globalThis.Promise = null;"
        );
        var capability = realm.CreatePromiseCapability();
        realm.Global["result"] = capability.Promise;
        realm.Evaluate(
            "Object.defineProperty(result, 'constructor', { get() { throw 'species'; } });"
        );
        JsValue seen = JsValue.Undefined;
        capability.Observe(value => seen = value, _ => Assert.Fail("Unexpected rejection"));
        capability.Resolve(42);
        Assert.That(seen.IsUndefined, Is.True);
        realm.Evaluate("0");
        Assert.That(seen.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ThrowingThenGetterPreservesRejectionIdentity()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var realm = runtime.MainRealm;
        var value = realm.Evaluate("globalThis.reason = {}; ({get then() {throw reason;}})");
        var capability = realm.CreatePromiseCapability();
        capability.Observe(
            _ => Assert.Fail("Unexpected fulfillment"),
            reason => realm.Global["observed"] = reason
        );
        capability.Resolve(value);
        realm.Agent.RunPromiseJobs();
        Assert.That(realm.Evaluate("observed === reason").IsTrue, Is.True);
    }

    [Test]
    public void ForeignAgentValueIsRejectedWithoutConsumingResolution()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        using var other = JsRuntime.CreateBuilder().Build();
        var capability = runtime.MainRealm.CreatePromiseCapability();
        var foreign = other.MainRealm.Evaluate("({})");
        Assert.Throws<ArgumentException>(() => capability.Resolve(foreign));
        capability.Resolve(7);
        Assert.That(
            ((JsPromiseObject)capability.Promise.AsObject()).SettledResult.Int32Value,
            Is.EqualTo(7)
        );
    }
}
