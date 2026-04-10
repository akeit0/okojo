using Microsoft.Extensions.Time.Testing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AgentJobQueueTests
{
    [Test]
    public void Microtasks_RunBefore_TimerTasks()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;

        _ = realm.Eval("""
                       globalThis.order = "";
                       Promise.resolve(0).then(function () { globalThis.order += "m"; });
                       setTimeout(function () { globalThis.order += "t"; }, 1);
                       globalThis.order += "s";
                       """);

        // Execute() pumps jobs; microtasks should run before timer tasks.
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sm"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("smt"));
    }

    [Test]
    public void WorkerStyle_AgentPump_IsIsolated()
    {
        var fakeTime = new FakeTimeProvider();
        var engine = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build();
        var mainRealm = engine.MainRealm;
        var workerRealm = engine.CreateWorkerAgent().MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.hit = 0;
                           setTimeout(function () { globalThis.hit = 1; }, 10);
                           """);
        _ = workerRealm.Eval("""
                             globalThis.hit = 0;
                             setTimeout(function () { globalThis.hit = 2; }, 10);
                             """);

        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        mainRealm.PumpJobs();

        Assert.That(mainRealm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(workerRealm.Global["hit"].Int32Value, Is.EqualTo(0));

        workerRealm.PumpJobs();
        Assert.That(workerRealm.Global["hit"].Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void PumpJobs_Reentrant_Call_Does_Not_Drain_Queue_Inside_Current_Job()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Agent.EnqueueMicrotask(() =>
        {
            realm.Global["order"] = realm.Global["order"].AsString() + "a";
            realm.Agent.PumpJobs();
            realm.Global["order"] = realm.Global["order"].AsString() + "b";
        });
        realm.Agent.EnqueueMicrotask(() => { realm.Global["order"] = realm.Global["order"].AsString() + "c"; });

        realm.Global["order"] = "s";
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sabc"));
    }
}
