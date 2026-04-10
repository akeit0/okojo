using Microsoft.Extensions.Time.Testing;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class WebRuntimeGlobalsTests
{
    [Test]
    public void BrowserGlobals_Are_Undefined_Without_Browser_Module()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var result = realm.Eval("""
                                [
                                  typeof window,
                                  typeof self,
                                  typeof AbortController,
                                  typeof AbortSignal,
                                  typeof atob,
                                  typeof btoa,
                                  typeof queueMicrotask,
                                  typeof setTimeout,
                                  typeof setInterval,
                                  typeof requestAnimationFrame,
                                  typeof cancelAnimationFrame
                                ].join("|")
                                """);

        Assert.That(result.AsString(),
            Is.EqualTo(
                "undefined|undefined|undefined|undefined|undefined|undefined|undefined|undefined|undefined|undefined|undefined"));
    }

    [Test]
    public void WebRuntimeGlobals_Do_Not_Install_Window_Or_Self()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                [typeof window, typeof self, typeof atob, typeof btoa].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("undefined|undefined|function|function"));
    }

    [Test]
    public void BrowserGlobals_Install_Window_Self_And_Base64_Helpers()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseBrowserGlobals()
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                [
                                  window === globalThis,
                                  self === globalThis,
                                  btoa("Hi"),
                                  atob("SGk=")
                                ].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true|SGk=|Hi"));
    }

    [Test]
    public void WebRuntimeGlobals_Install_AbortController()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                const controller = new AbortController();
                                const events = [];
                                controller.signal.addEventListener("abort", event => {
                                  events.push(`${event.type}|${event.target === controller.signal}|${controller.signal.aborted}`);
                                });
                                controller.abort("bye");
                                [
                                  typeof AbortController,
                                  typeof AbortSignal,
                                  controller.signal.aborted,
                                  controller.signal.reason,
                                  events.join(",")
                                ].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("function|function|true|bye|abort|true|true"));
    }

    [Test]
    public void AbortInterop_Can_Cancel_Host_Task_And_Reject_With_Abort_Reason()
    {
        using var runtime = JsRuntime.CreateBuilder()
            .UseWebRuntimeGlobals()
            .UseGlobals(globals => globals.Function("waitForAbort", 1, static (in info) =>
            {
                var abort = AbortInterop.Link(info.GetArgumentOrDefault(0, JsValue.Undefined));
                var task = Task.Delay(Timeout.InfiniteTimeSpan, abort.Token);
                return abort.WrapTask(info.Realm, task);
            }))
            .Build();

        var promise = runtime.MainRealm.Eval("""
                                             const controller = new AbortController();
                                             const pending = waitForAbort(controller.signal);
                                             controller.abort("bye");
                                             pending.catch(reason => reason);
                                             """);

        var result = runtime.MainRealm.ToTask(promise).GetAwaiter().GetResult();
        Assert.That(result.AsString(), Is.EqualTo("bye"));
    }

    [Test]
    public void AbortInterop_Can_Cancel_Host_ValueTask_With_Attached_Resources()
    {
        var tracker = new TrackingDisposable();

        using var runtime = JsRuntime.CreateBuilder()
            .UseWebRuntimeGlobals()
            .UseGlobals(globals => globals.Function("waitForCancelableValueTask", 1, (in info) =>
            {
                var abort = AbortInterop.Link(info.GetArgumentOrDefault(0, JsValue.Undefined));
                var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(abort.Token);
                return abort.WrapTask(info.Realm,
                    WaitForCancelAsync(linkedSource.Token),
                    linkedSource,
                    tracker);
            }))
            .Build();

        var promise = runtime.MainRealm.Eval("""
                                             const controller = new AbortController();
                                             const pending = waitForCancelableValueTask(controller.signal);
                                             controller.abort("bye");
                                             pending.catch(reason => reason);
                                             """);

        var result = runtime.MainRealm.ToTask(promise).GetAwaiter().GetResult();
        Assert.That(result.AsString(), Is.EqualTo("bye"));
        Assert.That(tracker.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void BrowserGlobals_Install_Window_And_Fetch_Together()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseBrowserGlobals()
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                [typeof window, typeof fetch, typeof setTimeout, typeof queueMicrotask].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("object|function|function|function"));
    }

    [Test]
    public void ServerRuntime_Installs_Fetch_Without_Window_And_Can_Add_Host_Module()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseServerRuntime(server => { server.RealmApiModules.Add(new ServerTestApiModule()); })
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                [typeof window, typeof Worker, typeof fetch, host.name].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("undefined|undefined|function|server"));
    }

    [Test]
    public void ServerRuntime_Installs_AbortController_Without_Window()
    {
        var realm = JsRuntime.CreateBuilder()
            .UseServerRuntime()
            .Build()
            .DefaultRealm;

        var result = realm.Eval("""
                                const controller = new AbortController();
                                controller.abort("stop");
                                [typeof window, typeof AbortController, typeof AbortSignal, controller.signal.reason].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("undefined|function|function|stop"));
    }

    [Test]
    public void AbortSignal_Timeout_Aborts_With_TimeoutError()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.signal = AbortSignal.timeout(5);
                       globalThis.snapshot = `${signal.aborted}|${signal.reason}`;
                       """);

        Assert.That(realm.Global["snapshot"].AsString(), Is.EqualTo("false|undefined"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();

        var result = realm.Eval("""
                                [signal.aborted, signal.reason.name, signal.reason.message].join("|")
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|TimeoutError|The operation timed out."));
    }

    [Test]
    public void QueueMicrotask_Runs_Before_Timer_Task()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.trace = "";
                       setTimeout(function () { trace += "t"; }, 1);
                       queueMicrotask(function () { trace += "m"; });
                       trace += "s";
                       """);

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("sm"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("smt"));
    }

    [Test]
    public void SetInterval_Uses_Shared_Timer_Ids_And_Can_Be_Cleared_Via_ClearTimeout()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.hits = 0;
                       const id = setInterval(function () {
                         hits++;
                         if (hits === 2) {
                           clearTimeout(id);
                         }
                       }, 5);
                       """);

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();
        Assert.That(realm.Global["hits"].Int32Value, Is.EqualTo(1));

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        realm.PumpJobs();
        Assert.That(realm.Global["hits"].Int32Value, Is.EqualTo(2));

        fakeTime.Advance(TimeSpan.FromMilliseconds(20));
        realm.PumpJobs();
        Assert.That(realm.Global["hits"].Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void RequestAnimationFrame_Runs_On_Next_Render_Turn_And_Drains_Microtasks()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseAnimationFrameInterval(TimeSpan.FromMilliseconds(16))
            .UseBrowserGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.trace = "";
                       globalThis.frameTimestamp = -1;
                       requestAnimationFrame(function (timestamp) {
                         trace += "r";
                         frameTimestamp = timestamp;
                         queueMicrotask(function () { trace += "m"; });
                       });
                       trace += "s";
                       """);

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("s"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(16));
        realm.PumpJobs();

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("srm"));
        Assert.That(realm.Global["frameTimestamp"].NumberValue, Is.GreaterThanOrEqualTo(0d));
    }

    [Test]
    public void CancelAnimationFrame_Prevents_Render_Callback()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseAnimationFrameInterval(TimeSpan.FromMilliseconds(16))
            .UseBrowserGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.trace = "";
                       const id = requestAnimationFrame(function () { trace += "r"; });
                       cancelAnimationFrame(id);
                       trace += "s";
                       """);

        fakeTime.Advance(TimeSpan.FromMilliseconds(16));
        realm.PumpJobs();

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("s"));
    }

    [Test]
    public void WebRuntimeGlobals_Can_Run_On_Manual_SingleThread_Event_Loop_With_Timer_Queue()
    {
        var fakeTime = new FakeTimeProvider();
        var eventLoop = new ManualHostEventLoop(fakeTime);
        var realm = JsRuntime.CreateBuilder()
            .UseTimeProvider(fakeTime)
            .UseLowLevelHost(host => host.UseTaskScheduler(eventLoop))
            .UseWebDelayScheduler(eventLoop)
            .UseWebTimerQueue(WebTaskQueueKeys.Timers)
            .UseWebRuntimeGlobals()
            .Build()
            .DefaultRealm;

        _ = realm.Eval("""
                       globalThis.trace = "";
                       setTimeout(function () { trace += "t"; }, 5);
                       trace += "s";
                       """);

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("s"));

        fakeTime.Advance(TimeSpan.FromMilliseconds(5));
        _ = eventLoop.PumpReadyDelayed();
        Assert.That(eventLoop.PumpQueue(WebTaskQueueKeys.Timers), Is.GreaterThanOrEqualTo(1));
        realm.PumpJobs();

        Assert.That(realm.Global["trace"].AsString(), Is.EqualTo("st"));
    }

    private static async ValueTask<int> WaitForCancelAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 1;
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class ServerTestApiModule : IRealmApiModule
    {
        public void Install(JsRealm realm)
        {
            var hostObject = new JsPlainObject(realm);
            hostObject.DefineDataProperty("name", JsValue.FromString("server"), JsShapePropertyFlags.Open);
            realm.Global["host"] = JsValue.FromObject(hostObject);
        }
    }
}
