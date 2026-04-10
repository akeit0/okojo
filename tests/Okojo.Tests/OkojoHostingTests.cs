using System.Net;
using Microsoft.Extensions.Time.Testing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class HostingTests
{
    private static readonly HostTaskQueueKey[] STimerDefaultOrder =
        [WebTaskQueueKeys.Timers, HostingTaskQueueKeys.Default];

    private static readonly HostTaskQueueKey[] SNetworkDefaultOrder =
        [WebTaskQueueKeys.Network, HostingTaskQueueKeys.Default];

    private static readonly HostTaskQueueKey[] SMessagesDefaultOrder =
        [WebTaskQueueKeys.Messages, HostingTaskQueueKeys.Default];

    [Test]
    public void ThreadPoolHosting_And_AgentThreadHost_Process_Worker_PostMessage()
    {
        var options = new JsRuntimeOptions().UseThreadPoolHosting();
        using var engine = JsRuntime.Create(options);
        var worker = engine.CreateWorkerAgent();

        using var host = new JsAgentThreadHost(worker);
        using var received = new ManualResetEventSlim(false);

        string? payloadText = null;
        worker.MessageReceived += (_, payload) =>
        {
            payloadText = payload as string;
            received.Set();
        };

        host.Start();
        engine.MainAgent.PostMessage(worker, "ping");

        Assert.That(received.Wait(TimeSpan.FromSeconds(2)), Is.True, "worker message was not processed in time");
        Assert.That(payloadText, Is.EqualTo("ping"));
        Assert.That(host.Stop(TimeSpan.FromSeconds(2)), Is.True, "agent host thread did not stop in time");
    }

    [Test]
    public void HostingMessageSerializer_Customizes_PostMessage_Bridging()
    {
        var options = new JsRuntimeOptions()
            .UseThreadPoolHosting()
            .UseWorkerGlobals()
            .UseHosting(static builder => builder.UseMessageSerializer(new PrefixHostingMessageSerializer()));

        using var engine = JsRuntime.Create(options);
        var mainRealm = engine.MainRealm;
        var workerRealm = engine.CreateWorkerAgent().MainRealm;

        _ = workerRealm.Eval("""
                             globalThis.recv = "";
                             onmessage = function (e) { recv = e.data; };
                             """);

        engine.MainAgent.PostMessage(workerRealm.Agent, "ping");
        workerRealm.PumpJobs();

        Assert.That(workerRealm.Global["recv"].AsString(), Is.EqualTo("in:host:ping"));

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           """);

        _ = workerRealm.Eval("postMessage('pong');");
        mainRealm.PumpJobs();

        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("in:host:js:pong"));
    }

    [Test]
    public void CreateWorkerRuntime_Loads_ModuleEntry_And_Pumps_On_Hosting_Side()
    {
        var loader = new InlineModuleLoader(new(StringComparer.Ordinal)
        {
            ["/worker-entry.js"] = """
                                   globalThis.workerValue = "ready";
                                   """
        });

        var options = new JsRuntimeOptions()
            .UseModuleSourceLoader(loader);
        using var engine = JsRuntime.Create(options);
        using var worker = engine.CreateWorkerRuntime(options => { options.ModuleEntry = "/worker-entry.js"; });

        worker.PumpUntilIdle();

        Assert.That(worker.Realm.Eval("workerValue").AsString(), Is.EqualTo("ready"));
    }

    [Test]
    public void RealmCreateWorkerRuntime_LoadsRelativeModuleEntry_WithExplicitModuleReferrer()
    {
        var loader = new InlineModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker-entry.js"] = """
                                        globalThis.workerValue = "ready";
                                        export const marker = "worker-entry";
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var realm = engine.MainRealm;
        using var worker = realm.CreateWorkerRuntime(options =>
        {
            options.ModuleEntry = "./worker-entry.js";
            options.ModuleReferrer = "/mods/owner.js";
        });
        worker.PumpUntilIdle();

        var started = worker.Realm.Eval("workerValue");
        Assert.That(started.AsString(), Is.EqualTo("ready"));
    }

    [Test]
    public void CreateWorkerRuntime_Can_Start_Background_Host()
    {
        var options = new JsRuntimeOptions().UseThreadPoolHosting();
        using var engine = JsRuntime.Create(options);
        using var worker = engine.CreateWorkerRuntime(options => { options.StartBackgroundHost = true; });
        using var received = new ManualResetEventSlim(false);

        string? payloadText = null;
        worker.Agent.MessageReceived += (_, payload) =>
        {
            payloadText = payload as string;
            received.Set();
        };

        engine.MainAgent.PostMessage(worker.Agent, "hosted-ping");

        Assert.That(received.Wait(TimeSpan.FromSeconds(2)), Is.True,
            "hosted worker did not process the message in time");
        Assert.That(payloadText, Is.EqualTo("hosted-ping"));
        Assert.That(worker.IsBackgroundHostRunning, Is.True);
        Assert.That(worker.StopBackgroundHost(TimeSpan.FromSeconds(2)), Is.True);
    }

    [Test]
    public void JsCreateWorker_Can_Use_Configured_Hosting_WorkerHost()
    {
        var options = new JsRuntimeOptions()
            .UseThreadPoolHosting()
            .UseWorkerGlobals()
            .UseHosting(static builder => builder.UseJsWorkerHost(
                new WorkerRuntimeHost(options => options.StartBackgroundHost = true)));

        using var engine = JsRuntime.Create(options);
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           globalThis.w = createWorker();
                           w.eval("onmessage = function (e) { postMessage('bg:' + e.data); };");
                           w.postMessage("ping");
                           """);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && mainRealm.Global["recv"].AsString() != "bg:ping")
        {
            mainRealm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("bg:ping"));
    }

    [Test]
    public void JsCreateWorker_LoadModule_Uses_Configured_Hosting_WorkerHost()
    {
        var loader = new InlineModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker.js"] = """
                                  export const value = "hosted-module";
                                  """
        });

        var options = new JsRuntimeOptions()
            .UseThreadPoolHosting()
            .UseWorkerGlobals()
            .UseModuleSourceLoader(loader)
            .UseHosting(builder => builder.UseJsWorkerHost(new WorkerRuntimeHost()));

        using var engine = JsRuntime.Create(options);
        var mainRealm = engine.MainRealm;

        var result = mainRealm.Eval("""
                                    var w = createWorker();
                                    var ns = w.loadModule("/mods/worker.js");
                                    ns.value;
                                    """);

        Assert.That(result.AsString(), Is.EqualTo("hosted-module"));
    }

    [Test]
    public void HostingBuilder_Can_Configure_TimeProvider_ModuleLoader_And_WorkerScriptLoader()
    {
        var moduleLoader = new InlineModuleLoader(new(StringComparer.Ordinal)
        {
            ["/main.js"] = "export const value = 'module';"
        });
        var workerLoader = new InlineWorkerScriptLoader("globalThis.workerLoaded = 'worker';");
        var timeProvider = new FakeTimeProvider();

        var options = new JsRuntimeOptions()
            .UseTimeProvider(timeProvider)
            .UseModuleSourceLoader(moduleLoader)
            .UseWorkerScriptSourceLoader(workerLoader);

        using var engine = JsRuntime.Create(options);

        Assert.That(engine.TimeProvider, Is.SameAs(timeProvider));
        Assert.That(engine.ModuleSourceLoader, Is.SameAs(moduleLoader));
        Assert.That(engine.WorkerScriptSourceLoader, Is.SameAs(workerLoader));
    }

    [Test]
    public async Task JsRuntimeBuilder_Composes_Core_Hosting_And_Web_Config()
    {
        using var httpClient = new HttpClient(new BuilderHttpMessageHandler());
        using var engine = JsRuntime.CreateBuilder()
            .UseThreadPoolHosting()
            .UseFetch(fetch => fetch.HttpClient = httpClient)
            .Build();

        var result = await engine.DefaultRealm.EvalAsync("""
                                                         (async () => {
                                                           const res = await fetch("https://builder.test/ping");
                                                           return [typeof fetch, res.status, await res.text()].join("|");
                                                         })()
                                                         """);

        Assert.That(result.AsString(), Is.EqualTo("function|204|builder"));
    }

    [Test]
    public async Task JsRuntimeCreate_Composes_Builder_Based_Hosting_And_Web_Config()
    {
        using var httpClient = new HttpClient(new BuilderHttpMessageHandler());
        using var engine = JsRuntime.Create(builder => builder
            .UseThreadPoolHosting()
            .UseFetch(fetch => fetch.HttpClient = httpClient));

        var result = await engine.DefaultRealm.EvalAsync("""
                                                         (async () => {
                                                           const res = await fetch("https://builder.test/factory");
                                                           return [typeof fetch, res.status, await res.text()].join("|");
                                                         })()
                                                         """);

        Assert.That(result.AsString(), Is.EqualTo("function|204|builder"));
    }

    [Test]
    public void JsRuntimeBuilder_BuildOptions_Returns_Isolated_Clone()
    {
        var timeProvider = new FakeTimeProvider();
        var builder = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider);

        var options = builder.BuildOptions();
        builder.UseModuleSourceLoader(new InlineModuleLoader(new(StringComparer.Ordinal)
        {
            ["/later.js"] = "export const value = 'later';"
        }));

        using var engine = JsRuntime.Create(options);

        Assert.That(engine.TimeProvider, Is.SameAs(timeProvider));
        Assert.That(engine.ModuleSourceLoader, Is.TypeOf<FileModuleSourceLoader>());
    }

    [Test]
    public void Custom_TaskScheduler_Can_Drive_Window_Timers()
    {
        var timeProvider = new FakeTimeProvider();
        var scheduler = new RecordingTaskScheduler();

        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(scheduler))
            .UseWebRuntimeGlobals()
            .Build();

        var realm = engine.DefaultRealm;
        _ = realm.Eval("""
                       globalThis.hit = 0;
                       setTimeout(function () { hit = 1; }, 10);
                       """);

        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        realm.PumpJobs();

        Assert.That(realm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(scheduler.BindCount, Is.EqualTo(1));
        Assert.That(scheduler.EnqueueCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void LowLevelHostOptions_Can_Configure_Core_Host_Seams_Directly()
    {
        var timeProvider = new FakeTimeProvider();
        var scheduler = new RecordingTaskScheduler();
        var options = new JsRuntimeOptions();
        options.Host.UseTimeProvider(timeProvider);
        options.LowLevelHost.UseTaskScheduler(scheduler);

        using var engine = JsRuntime.Create(options.UseWebRuntimeGlobals());
        var realm = engine.DefaultRealm;

        _ = realm.Eval("""
                       globalThis.hit = 0;
                       setTimeout(function () { hit = 1; }, 5);
                       """);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();

        Assert.That(realm.Global["hit"].Int32Value, Is.EqualTo(1));
        Assert.That(scheduler.BindCount, Is.EqualTo(1));
        Assert.That(scheduler.EnqueueCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void JsAgentExecutionBudgetExtensions_Reset_Instruction_Budget_And_Timeout()
    {
        using var engine = JsRuntime.CreateBuilder().Build();
        var agent = engine.MainAgent;

        agent.ApplyExecutionBudget(32, TimeSpan.FromMilliseconds(50), 8);
        agent.ResetExecutionBudget(16, TimeSpan.FromMilliseconds(10));

        Assert.That(agent.ResetExecutionTimeout(), Is.True);
    }

    [Test]
    public void HostPump_RunUntil_Completes_When_Timer_Task_Becomes_Ready()
    {
        var timeProvider = new FakeTimeProvider();
        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseWebRuntimeGlobals()
            .Build();

        var realm = engine.DefaultRealm;
        var pump = engine.CreateHostPump();

        _ = realm.Eval("""
                       globalThis.done = false;
                       setTimeout(function () { done = true; }, 5);
                       """);

        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        });

        var completed = pump.RunUntil(() => realm.Global["done"].IsTrue, TimeSpan.FromSeconds(1));

        Assert.That(completed, Is.True);
        Assert.That(realm.Global["done"].IsTrue, Is.True);
    }

    [Test]
    public void HostTurnRunner_RunTurn_Can_Process_Timer_Turn_On_Manual_Event_Loop()
    {
        var timeProvider = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(timeProvider);
        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseWebDelayScheduler(eventLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseWebRuntimeGlobals()
            .Build();

        var realm = engine.DefaultRealm;
        var pump = engine.CreateHostPump();
        _ = realm.Eval("""
                       globalThis.done = false;
                       setTimeout(function () { done = true; }, 5);
                       """);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        var ran = HostTurnRunner.RunTurn(eventLoop, pump, STimerDefaultOrder);

        Assert.That(ran, Is.True);
        Assert.That(realm.Global["done"].IsTrue, Is.True);
    }

    [Test]
    public void ThreadAffinityHostLoop_Can_Run_Window_Timer_On_Owner_Thread()
    {
        var timeProvider = new FakeTimeProvider();
        using var hostLoop = new ThreadAffinityHostLoop(timeProvider);
        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(hostLoop))
            .UseWebDelayScheduler(hostLoop)
            .UseWebRuntimeGlobals()
            .Build();

        var realm = engine.DefaultRealm;
        var pump = engine.CreateHostPump();

        _ = realm.Eval("""
                       globalThis.done = false;
                       setTimeout(function () { done = true; }, 5);
                       """);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        _ = hostLoop.RunOneTurn(TimeSpan.Zero, pump);

        Assert.That(realm.Global["done"].IsTrue, Is.True);
    }

    [Test]
    public void ManualEventLoop_Can_Deliver_Fetch_Completion_On_Network_Queue()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        using var httpClient = new HttpClient(new DeferredHttpMessageHandler(tcs));
        var timeProvider = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(timeProvider);

        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseFetch(fetch => fetch.HttpClient = httpClient)
            .UseFetchCompletionQueue(WebTaskQueueKeys.Network)
            .Build();

        var realm = engine.DefaultRealm;
        var pump = engine.CreateHostPump();
        _ = realm.Eval("""
                       globalThis.result = "pending";
                       fetch("https://queue.test/data")
                         .then(r => r.text())
                         .then(t => { result = t; });
                       """);

        tcs.SetResult(new(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, "https://queue.test/data"),
            Content = new StringContent("network-queue")
        });

        Assert.That(realm.Global["result"].AsString(), Is.EqualTo("pending"));
        var ran = HostTurnRunner.RunTurn(eventLoop, pump, SNetworkDefaultOrder);

        Assert.That(ran, Is.True);
        Assert.That(realm.Global["result"].AsString(), Is.EqualTo("network-queue"));
    }

    [Test]
    public void ManualEventLoop_Can_Deliver_Worker_Message_On_Message_Queue()
    {
        var timeProvider = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(timeProvider);

        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host =>
            {
                host.UseTaskScheduler(eventLoop);
                host.UseWorkerMessageQueue(WebTaskQueueKeys.Messages);
            })
            .UseWorkerGlobals()
            .Build();

        var mainRealm = engine.MainRealm;
        var workerRealm = engine.CreateWorkerAgent().MainRealm;
        var pump = engine.CreateHostPump();
        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           """);

        _ = workerRealm.Eval("postMessage('queued-message');");
        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo(string.Empty));

        var ran = HostTurnRunner.RunTurn(eventLoop, pump, SMessagesDefaultOrder);

        Assert.That(ran, Is.True);
        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("queued-message"));
    }

    [Test]
    public void ManualHostEventLoop_Snapshot_Reports_Queued_And_Delayed_Work()
    {
        var timeProvider = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(timeProvider);

        _ = eventLoop.ScheduleDelayed(TimeSpan.FromMilliseconds(5), WebTaskQueueKeys.Timers, static _ => { }, null);
        _ = eventLoop.ScheduleDelayed(TimeSpan.FromMilliseconds(7), WebTaskQueueKeys.Network, static _ => { }, null);

        var before = eventLoop.GetSnapshot();

        Assert.That(before.PendingDelayedCount, Is.EqualTo(2));
        Assert.That(before.NextDelayedDueAt, Is.Not.Null);
        Assert.That(before.Queues, Is.Empty);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        Assert.That(eventLoop.PumpReadyDelayed(), Is.EqualTo(1));

        var after = eventLoop.GetSnapshot();
        Assert.That(after.PendingDelayedCount, Is.EqualTo(1));
        Assert.That(after.Queues.Single(x => x.QueueKey == WebTaskQueueKeys.Timers).PendingTaskCount, Is.EqualTo(1));
    }

    [Test]
    public void ThreadAffinityHostLoop_Snapshot_Reports_Queued_And_Delayed_Work()
    {
        var timeProvider = new FakeTimeProvider();
        using var hostLoop = new ThreadAffinityHostLoop(timeProvider);

        _ = hostLoop.ScheduleDelayed(TimeSpan.FromMilliseconds(5), WebTaskQueueKeys.Messages, static _ => { }, null);
        var before = hostLoop.GetSnapshot();

        Assert.That(before.PendingDelayedCount, Is.EqualTo(1));
        Assert.That(before.NextDelayedDueAt, Is.Not.Null);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        Assert.That(hostLoop.WaitForWork(TimeSpan.Zero), Is.True);
        Assert.That(hostLoop.PumpQueue(WebTaskQueueKeys.Messages), Is.EqualTo(1));

        var after = hostLoop.GetSnapshot();
        Assert.That(after.PendingDelayedCount, Is.EqualTo(0));
        Assert.That(after.Queues.Single(x => x.QueueKey == WebTaskQueueKeys.Messages).PendingTaskCount, Is.EqualTo(0));
    }

    [Test]
    public void HostTurnRunner_Observer_Sees_Timer_Queue_And_Microtask_Checkpoint()
    {
        var timeProvider = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(timeProvider);
        using var engine = JsRuntime.CreateBuilder()
            .UseTimeProvider(timeProvider)
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseWebDelayScheduler(eventLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseWebRuntimeGlobals()
            .Build();

        var realm = engine.DefaultRealm;
        var pump = engine.CreateHostPump();
        var observer = new RecordingTurnObserver();

        _ = realm.Eval("""
                       globalThis.trace = "";
                       setTimeout(function () {
                         trace += "t";
                         Promise.resolve().then(function () { trace += "m"; });
                       }, 5);
                       """);

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        var ran = HostTurnRunner.RunTurn(eventLoop, pump, STimerDefaultOrder, observer);

        Assert.That(ran, Is.True);
        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("tm"));
        Assert.That(observer.Notifications.Select(x => x.Phase).ToArray(), Is.EqualTo(new[]
        {
            HostTurnPhase.BeforeTurn,
            HostTurnPhase.AfterHostTask,
            HostTurnPhase.BeforeMicrotaskCheckpoint,
            HostTurnPhase.AfterMicrotaskCheckpoint,
            HostTurnPhase.AfterTurn
        }));
        Assert.That(observer.Notifications[1].HostTaskQueueKey, Is.EqualTo(WebTaskQueueKeys.Timers));
        Assert.That(observer.Notifications[2].PendingJobCount, Is.GreaterThan(0));
        Assert.That(observer.Notifications[4].PendingJobCount, Is.EqualTo(0));
    }

    private sealed class PrefixHostingMessageSerializer : IHostingMessageSerializer
    {
        public object? CloneCrossAgentPayload(object? payload)
        {
            return payload is string text ? "host:" + text : payload;
        }

        public object? SerializeOutgoing(JsRealm realm, in JsValue value)
        {
            if (!value.IsString)
                throw new InvalidOperationException("test serializer expects string values");

            return "js:" + value.AsString();
        }

        public JsValue DeserializeIncoming(JsRealm realm, object? payload)
        {
            return payload is string text
                ? JsValue.FromString("in:" + text)
                : JsValue.Null;
        }
    }

    private sealed class InlineModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("/", StringComparison.Ordinal))
                return specifier;

            if (referrer is null)
                return "/" + specifier.TrimStart('/');

            var slash = referrer.LastIndexOf('/');
            var baseDir = slash >= 0 ? referrer[..(slash + 1)] : "/";
            if (specifier.StartsWith("./", StringComparison.Ordinal))
                return baseDir + specifier[2..];

            return baseDir + specifier;
        }

        public string LoadSource(string resolvedSpecifier)
        {
            return modules[resolvedSpecifier];
        }
    }

    private sealed class InlineWorkerScriptLoader(string source) : IWorkerScriptSourceLoader
    {
        public string LoadScript(string path, string? referrer = null)
        {
            return source;
        }
    }

    private sealed class BuilderHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                RequestMessage = request,
                Content = new StringContent("builder")
            });
        }
    }

    private sealed class DeferredHttpMessageHandler(TaskCompletionSource<HttpResponseMessage> completion)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return completion.Task;
        }
    }

    private sealed class RecordingTurnObserver : IHostTurnObserver
    {
        public List<HostTurnNotification> Notifications { get; } = [];

        public void OnTurnEvent(HostTurnNotification notification)
        {
            Notifications.Add(notification);
        }
    }

    private sealed class RecordingTaskScheduler : IHostTaskScheduler
    {
        public int BindCount;
        public int EnqueueCount;

        public IHostAgentScheduler CreateAgentScheduler(HostTaskTarget target)
        {
            Interlocked.Increment(ref BindCount);
            return new RecordingAgentScheduler(this, target);
        }

        private sealed class RecordingAgentScheduler(RecordingTaskScheduler owner, HostTaskTarget target)
            : IHostAgentScheduler
        {
            public void EnqueueTask(Action<object?> callback, object? state)
            {
                Interlocked.Increment(ref owner.EnqueueCount);
                target.EnqueueTask(callback, state);
            }
        }
    }
}
