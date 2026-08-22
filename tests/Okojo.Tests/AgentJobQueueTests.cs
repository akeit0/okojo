using Microsoft.Extensions.Time.Testing;
using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

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
    public void RunPromiseJobs_ReentrantCheckpoint_DoesNotRunNestedCheckpoint()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var order = new List<string>();

        realm.Agent.EnqueuePromiseJob(() =>
        {
            order.Add("outer");
            realm.Agent.EnqueuePromiseJob(() => order.Add("queued"));
            Assert.That(realm.Agent.RunPromiseJobs(), Is.EqualTo(0));
            order.Add("outer-end");
        });

        Assert.That(realm.Agent.RunPromiseJobs(), Is.EqualTo(2));
        Assert.That(order, Is.EqualTo(new[] { "outer", "outer-end", "queued" }));
    }

    [Test]
    public void HostSelectsOneTask_ThenExplicitCheckpoint_DoesNotPumpAnotherHostTask()
    {
        var scheduler = new SelectingHostTaskScheduler();
        using var engine = JsRuntime
            .CreateBuilder()
            .UseLowLevelHost(host => host.UseTaskScheduler(scheduler))
            .Build();
        var worker = engine.CreateWorkerAgent();
        var order = new List<string>();

        worker.MessageReceived += (_, _) =>
        {
            order.Add("task");
            worker.EnqueuePromiseJob(() => order.Add("promise"));
        };

        engine.MainAgent.PostMessage(worker, "ping");
        engine.MainAgent.PostMessage(worker, "second");
        Assert.That(scheduler.PendingCount, Is.EqualTo(2));
        Assert.That(scheduler.PumpOne(), Is.True);
        Assert.That(scheduler.PendingCount, Is.EqualTo(1));
        Assert.That(worker.RunOneHostJob(), Is.True);
        Assert.That(order, Is.EqualTo(new[] { "task" }));

        Assert.That(worker.RunPromiseJobs(), Is.EqualTo(1));
        Assert.That(order, Is.EqualTo(new[] { "task", "promise" }));
        Assert.That(scheduler.PendingCount, Is.EqualTo(1));
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

    private sealed class SelectingHostTaskScheduler : IHostTaskScheduler
    {
        private readonly Queue<(
            HostTaskTarget Target,
            Action<object?> Callback,
            object? State
        )> pending = new();

        public int PendingCount
        {
            get
            {
                lock (pending)
                    return pending.Count;
            }
        }

        public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
        {
            return new AgentScheduler(this, target);
        }

        public bool PumpOne()
        {
            (HostTaskTarget Target, Action<object?> Callback, object? State) task;
            lock (pending)
            {
                if (pending.Count == 0)
                    return false;
                task = pending.Dequeue();
            }

            task.Target.EnqueueTask(task.Callback, task.State);
            return true;
        }

        private void Enqueue(HostTaskTarget target, Action<object?> callback, object? state)
        {
            lock (pending)
                pending.Enqueue((target, callback, state));
        }

        private sealed class AgentScheduler(SelectingHostTaskScheduler owner, HostTaskTarget target)
            : IQueuedHostAgentScheduler
        {
            public void EnqueueTask(Action<object?> callback, object? state)
            {
                owner.Enqueue(target, callback, state);
            }

            public void EnqueueTask(
                HostTaskQueueKey queueKey,
                Action<object?> callback,
                object? state
            )
            {
                owner.Enqueue(target, callback, state);
            }
        }
    }
}
