using Okojo.Runtime;

namespace Okojo.Tests;

public class WorkerAgentTests
{
    [Test]
    public void CreateWorkerAgent_CreatesDistinctWorkerAgent()
    {
        var engine = JsRuntime.Create();
        var worker = engine.CreateWorkerAgent();

        Assert.That(engine.MainAgent.Kind, Is.EqualTo(JsAgentKind.Main));
        Assert.That(worker.Kind, Is.EqualTo(JsAgentKind.Worker));
        Assert.That(worker.Id, Is.Not.EqualTo(engine.MainAgent.Id));
        Assert.That(worker, Is.Not.SameAs(engine.MainAgent));
    }

    [Test]
    public void HelperStyle_GetReportAsync_Does_Not_Recurse()
    {
        var engine = JsRuntime.CreateBuilder()
            .UseWebRuntimeGlobals()
            .Build();
        var realm = engine.DefaultRealm;

        _ = realm.Eval("""
                       globalThis.count = 0;
                       globalThis.done = false;
                       globalThis.result = "";
                       globalThis.error = "";
                       globalThis.$262 = {
                         agent: {
                           getReport() {
                             count++;
                             return count >= 3 ? "ok" : null;
                           },
                           sleep() {}
                         }
                       };

                       {
                         let getReport = $262.agent.getReport.bind($262.agent);

                         $262.agent.getReport = function() {
                           var r;
                           while ((r = getReport()) == null) {
                             $262.agent.sleep(1);
                           }
                           return r;
                         };

                         $262.agent.setTimeout = setTimeout;

                         $262.agent.getReportAsync = function() {
                           return new Promise(function(resolve) {
                             (function loop() {
                               let result = getReport();
                               if (!result) {
                                 setTimeout(loop, 1);
                               } else {
                                 resolve(result);
                               }
                             })();
                           });
                         };
                       }

                       $262.agent.getReportAsync().then(function(value) {
                         result = value;
                         done = true;
                       }, function(err) {
                         error = String(err);
                         done = true;
                       });
                       """);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && !realm.Eval("done").IsTrue)
        {
            realm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(realm.Eval("done").IsTrue, Is.True);
        Assert.That(realm.Eval("error").AsString(), Is.EqualTo(string.Empty));
        Assert.That(realm.Eval("result").AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void PostMessage_DeliversOnlyWhenTargetAgentPumps()
    {
        var engine = JsRuntime.Create();
        var main = engine.MainAgent;
        var worker = engine.CreateWorkerAgent();

        object? received = null;
        worker.MessageReceived += (_, payload) => received = payload;

        main.PostMessage(worker, "hello");

        main.MainRealm.PumpJobs();
        Assert.That(received, Is.Null);

        worker.MainRealm.PumpJobs();
        Assert.That(received, Is.EqualTo("hello"));
    }

    [Test]
    public void PostMessage_UsesStructuredCloneSubset()
    {
        var engine = JsRuntime.Create();
        var main = engine.MainAgent;
        var worker = engine.CreateWorkerAgent();

        var payload = new Dictionary<string, object?>
        {
            ["name"] = "alpha",
            ["list"] = new List<object?> { 1, "x" }
        };

        Dictionary<string, object?>? received = null;
        worker.MessageReceived += (_, message) => received = (Dictionary<string, object?>)message!;

        main.PostMessage(worker, payload);
        worker.MainRealm.PumpJobs();

        Assert.That(received, Is.Not.Null);
        Assert.That(ReferenceEquals(received, payload), Is.False);

        payload["name"] = "mutated";
        Assert.That(received!["name"], Is.EqualTo("alpha"));
    }

    [Test]
    public void WorkerRealm_JsPostMessage_DeliversToMainOnMessage()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var workerRealm = engine.CreateWorkerAgent().MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           """);

        _ = workerRealm.Eval("postMessage('hello-from-worker');");
        mainRealm.PumpJobs();

        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("hello-from-worker"));
    }

    [Test]
    public void CreateWorker_IsUndefined_Without_WorkerGlobals()
    {
        using var engine = JsRuntime.Create();

        var result = engine.MainRealm.Eval("""
                                           [
                                             typeof createWorker,
                                             typeof onmessage,
                                             typeof onmessageerror,
                                             typeof postMessage
                                           ].join("|")
                                           """);

        Assert.That(result.AsString(), Is.EqualTo("undefined|undefined|undefined|undefined"));
    }

    [Test]
    public void MainRealm_CreateWorkerHandle_CanPostMessageToWorker()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        var result = mainRealm.Eval("""
                                    globalThis.recv = "";
                                    globalThis.w = createWorker();
                                    w.eval("onmessage = function (e) { postMessage('pong:' + e.data); };");
                                    onmessage = function (e) { recv = e.data; };
                                    w.postMessage("ping");
                                    w.pump();
                                    recv;
                                    """);

        Assert.That(result.Tag, Is.EqualTo(Tag.JsTagString));
        Assert.That(result.AsString(), Is.EqualTo("pong:ping"));
    }

    [Test]
    public void WorkerGlobal_OnMessageError_Fires_OnUnsupportedHostPayload()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var main = engine.MainAgent;
        var worker = engine.CreateWorkerAgent();
        var workerRealm = worker.MainRealm;

        _ = workerRealm.Eval("""
                             globalThis.err = "";
                             onmessageerror = function () { err = "global-error"; };
                             """);

        main.PostMessage(worker, new());
        workerRealm.PumpJobs();

        Assert.That(workerRealm.Global["err"].AsString(), Is.EqualTo("global-error"));
    }

    [Test]
    public void WorkerHandle_OnMessageError_Fires_OnUnsupportedHostPayload()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.err = "";
                           globalThis.w = createWorker();
                           w.onmessageerror = function () { err = "handle-error"; };
                           """);

        var worker = engine.Agents.First(a => a.Kind == JsAgentKind.Worker);
        worker.PostMessage(engine.MainAgent, new());
        mainRealm.PumpJobs();

        Assert.That(mainRealm.Global["err"].AsString(), Is.EqualTo("handle-error"));
    }

    [Test]
    public void WorkerHandle_Terminate_StopsFurtherMessageDelivery()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           globalThis.w = createWorker();
                           w.eval("onmessage = function (e) { postMessage('ok:' + e.data); };");
                           onmessage = function (e) { recv = recv + "|" + e.data; };
                           w.postMessage("a");
                           w.pump();
                           w.terminate();
                           w.postMessage("b");
                           w.pump();
                           recv;
                           """);

        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("|ok:a"));
    }
}
