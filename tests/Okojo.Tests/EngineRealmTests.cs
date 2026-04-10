using Okojo.Runtime;

namespace Okojo.Tests;

public class EngineRealmTests
{
    [Test]
    public void SharedEngine_UsesSharedAtomTableAcrossRealms()
    {
        using var engine = JsRuntime.Create();
        var realm1 = engine.MainRealm;
        var realm2 = engine.CreateRealm();

        Assert.That(ReferenceEquals(realm1.Atoms, realm2.Atoms), Is.True);
        Assert.That(realm1.Engine, Is.SameAs(engine));
        Assert.That(realm2.Engine, Is.SameAs(engine));
        Assert.That(realm1.Agent, Is.SameAs(engine.MainAgent));
        Assert.That(realm2.Agent, Is.SameAs(engine.MainAgent));
        Assert.That(realm1.Id, Is.EqualTo(0));
        Assert.That(realm2.Id, Is.EqualTo(1));
        Assert.That(engine.Realms.Count, Is.EqualTo(2));

        var a1 = realm1.Atoms.InternNoCheck("shared-key");
        var a2 = realm2.Atoms.InternNoCheck("shared-key");
        Assert.That(a2, Is.EqualTo(a1));
    }

    [Test]
    public void DifferentAgents_IsolateAtomTables()
    {
        using var engine = JsRuntime.Create();
        var agent1 = engine.MainAgent;
        var agent2 = engine.CreateWorkerAgent();

        Assert.That(agent2, Is.Not.SameAs(agent1));
        Assert.That(engine.Agents.Count, Is.EqualTo(2));
        Assert.That(ReferenceEquals(agent1.Atoms, agent2.Atoms), Is.False);

        _ = agent1.Atoms.InternNoCheck("worker-only-key");
        Assert.That(agent2.Atoms.TryGetInterned("worker-only-key", out _), Is.False);
    }

    [Test]
    public void WorkerStyleAgentIsolation_DoesNotLeakGlobals()
    {
        using var engine = JsRuntime.Create();
        var mainRealm = engine.MainRealm;
        var workerAgent = engine.CreateWorkerAgent();
        var workerRealm = workerAgent.MainRealm;

        _ = mainRealm.Eval("globalThis.sharedFromMain = 123;");
        var workerType = workerRealm.Eval("typeof sharedFromMain;");

        Assert.That(workerType.IsString, Is.True);
        Assert.That(workerType.AsString(), Is.EqualTo("undefined"));

        _ = workerRealm.Eval("globalThis.sharedFromWorker = 456;");
        var mainType = mainRealm.Eval("typeof sharedFromWorker;");

        Assert.That(mainType.IsString, Is.True);
        Assert.That(mainType.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public void ThrowTypeError_Is_Shared_Per_Realm_And_Distinct_Across_Realms()
    {
        using var engine = JsRuntime.Create();
        var realm1 = engine.MainRealm;
        var realm2 = engine.CreateRealm();

        var thrower1 = realm1.Eval("""
                                   Object.getOwnPropertyDescriptor((function() {
                                     "use strict";
                                     return arguments;
                                   }()), "callee").get;
                                   """);
        var thrower1Again = realm1.Eval("""
                                        Object.getOwnPropertyDescriptor(Function.prototype, "caller").get;
                                        """);
        var thrower2 = realm2.Eval("""
                                   Object.getOwnPropertyDescriptor((function() {
                                     "use strict";
                                     return arguments;
                                   }()), "callee").get;
                                   """);

        Assert.That(thrower1.TryGetObject(out var thrower1Object), Is.True);
        Assert.That(thrower1Again.TryGetObject(out var thrower1AgainObject), Is.True);
        Assert.That(thrower2.TryGetObject(out var thrower2Object), Is.True);
        Assert.That(thrower1Object, Is.SameAs(thrower1AgainObject));
        Assert.That(thrower2Object, Is.Not.SameAs(thrower1Object));
    }

    [Test]
    public void CrossRealm_ThrowTypeError_Uses_Callee_Realm_TypeError()
    {
        using var engine = JsRuntime.Create();
        var realm1 = engine.MainRealm;
        var realm2 = engine.CreateRealm();

        var thrower = realm2.Eval("""
                                  Object.getOwnPropertyDescriptor((function() {
                                    "use strict";
                                    return arguments;
                                  }()), "callee").get;
                                  """);
        realm1.Global["otherThrowTypeError"] = thrower;
        realm2.Global["otherThrowTypeError"] = thrower;

        var result = realm1.Eval("""
                                 var ok;
                                 try {
                                   otherThrowTypeError();
                                   ok = false;
                                 } catch (e) {
                                   ok = e instanceof TypeError;
                                 }
                                 ok;
                                 """);

        var otherResult = realm2.Eval("""
                                      var ok;
                                      try {
                                        otherThrowTypeError();
                                        ok = false;
                                      } catch (e) {
                                        ok = e instanceof TypeError;
                                      }
                                      ok;
                                      """);

        Assert.That(result.IsFalse, Is.True);
        Assert.That(otherResult.IsTrue, Is.True);
    }
}
