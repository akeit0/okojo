using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class HostPromiseCapabilityTests
{
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
