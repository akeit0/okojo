using Microsoft.Extensions.Time.Testing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AgentJobQueueTests
{
    [Test]
    public void Microtasks_RunBefore_TimerTasks()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime
            .CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval(
            """
            globalThis.order = "";
            Promise.resolve(0).then(function () { globalThis.order += "m"; });
            setTimeout(function () { globalThis.order += "t"; }, 1);
            globalThis.order += "s";
            """
        );

        // Execute() runs promise jobs; microtasks should run before timer tasks.
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sm"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("smt"));
    }

    [Test]
    public void WorkerStyle_AgentPump_IsIsolated()
    {
        var fakeTime = new FakeTimeProvider();
        var engine = JsRuntime
            .CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var workerRealm = engine.CreateWorkerAgent().MainRealm;

        _ = mainRealm.Eval(
            """
            globalThis.hit = 0;
            setTimeout(function () { globalThis.hit = 1; }, 10);
            """
        );
        _ = workerRealm.Eval(
            """
            globalThis.hit = 0;
            setTimeout(function () { globalThis.hit = 2; }, 10);
            """
        );

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

        realm.Agent.EnqueuePromiseJob(() =>
        {
            realm.Global["order"] = realm.Global["order"].AsString() + "a";
            realm.Agent.PumpJobs();
            realm.Global["order"] = realm.Global["order"].AsString() + "b";
        });
        realm.Agent.EnqueuePromiseJob(() =>
        {
            realm.Global["order"] = realm.Global["order"].AsString() + "c";
        });

        realm.Global["order"] = "s";
        realm.Agent.PumpJobs();

        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sabc"));
    }

    [Test]
    public void RunPromiseJobs_Manually_Drains_PromiseQueue()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Agent.EnqueuePromiseJob(() =>
            realm.Global["order"] = realm.Global["order"].AsString() + "a"
        );
        realm.Agent.EnqueuePromiseJob(() =>
            realm.Global["order"] = realm.Global["order"].AsString() + "b"
        );
        realm.Global["order"] = "s";

        var count = realm.Agent.RunPromiseJobs();

        Assert.That(count, Is.EqualTo(2));
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sab"));
        Assert.That(realm.Agent.GetJobCount(JobQueueName.PromiseJobs), Is.EqualTo(0));
    }

    [Test]
    public void RunJobs_NamedHostQueue_ExecutesOnlyThatQueue()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Agent.EnqueuePromiseJob(() =>
            realm.Global["order"] = realm.Global["order"].AsString() + "p"
        );
        realm.Agent.EnqueueJob(
            "my-queue",
            () => realm.Global["order"] = realm.Global["order"].AsString() + "h"
        );
        realm.Global["order"] = "s";

        var count = realm.Agent.RunJobs("my-queue");

        Assert.That(count, Is.EqualTo(1));
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("sh"));
        // Promise job is untouched until the host decides to run it.
        Assert.That(realm.Agent.GetJobCount(JobQueueName.PromiseJobs), Is.EqualTo(1));
        realm.Agent.RunPromiseJobs();
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("shp"));
    }

    [Test]
    public void RunScriptJobs_ExecutesScriptQueue()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Agent.EnqueueScriptJob(() =>
            realm.Global["order"] = realm.Global["order"].AsString() + "s"
        );
        realm.Global["order"] = "";

        Assert.That(realm.Agent.RunScriptJobs(), Is.EqualTo(1));
        Assert.That(realm.Global["order"].AsString(), Is.EqualTo("s"));
    }

    [Test]
    public void PumpJobs_DrainsPriorityJobsQueuedDuringPromiseCheckpoint()
    {
        using var engine = JsRuntime.Create();
        var agent = engine.MainAgent;
        var order = new List<string>();

        agent.EnqueuePromiseJob(() =>
        {
            order.Add("promise-1");
            agent.EnqueuePromiseJob(() => order.Add("promise-2"));
            agent.EnqueueHostPriorityJob(() => order.Add("next-tick"));
        });

        agent.PumpJobs();

        Assert.That(order, Is.EqualTo(new[] { "promise-1", "promise-2", "next-tick" }));
        Assert.That(agent.GetJobCount(JobQueueName.HostPriorityJobs), Is.EqualTo(0));
    }

    [Test]
    public void InitializationCallbacksCanPumpAfterHostQueueAttachment()
    {
        var order = new List<string>();
        using var engine = JsRuntime
            .CreateBuilder()
            .UseRealm(options =>
                options.Initialize = realm =>
                {
                    realm.Agent.EnqueuePromiseJob(() => order.Add("realm-promise"));
                    realm.PumpJobs();
                    order.Add("realm");
                }
            )
            .UseAgent(options =>
                options.Initialize = agent =>
                {
                    agent.EnqueuePromiseJob(() => order.Add("agent-promise"));
                    agent.PumpJobs();
                    order.Add("agent");
                }
            )
            .Build();

        Assert.That(
            order,
            Is.EqualTo(new[] { "realm-promise", "realm", "agent-promise", "agent" })
        );
    }
}
