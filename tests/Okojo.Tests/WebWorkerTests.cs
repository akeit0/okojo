using Okojo.Runtime;

namespace Okojo.Tests;

public class WebWorkerTests
{
    [Test]
    public void Worker_IsUndefined_Without_WebWorkerModule()
    {
        using var engine = JsRuntime.Create();
        var realm = engine.MainRealm;

        var result = realm.Eval("typeof Worker");

        Assert.That(result.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public void UseWebWorkers_StartsBackgroundHostedJsWorkers_AndUsesOwnerModuleReferrer()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/owner.js"] = """
                                 globalThis.recv = "";
                                 onmessage = function (e) { recv = e.data; };
                                 globalThis.w = createWorker("./worker-entry.js");
                                 """,
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("web:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWebWorkers()
            .Build();
        var realm = engine.MainRealm;

        _ = realm.LoadModule("/mods/owner.js");
        _ = realm.Eval("w.postMessage('ping');");

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && realm.Global["recv"].AsString() != "web:ping")
        {
            realm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo("web:ping"));
        Assert.That(loader.ResolveCalls.Any(c =>
            c.Specifier == "./worker-entry.js" && c.Referrer == "/mods/owner.js"), Is.True);
    }

    [Test]
    public void UseWebWorkers_InstallsWorkerConstructor()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("ctor:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWebWorkers()
            .Build();
        var realm = engine.MainRealm;

        _ = realm.Eval("""
                       globalThis.recv = "";
                       onmessage = function (e) { recv = e.data; };
                       globalThis.w = new Worker("/mods/worker-entry.js");
                       w.postMessage("ping");
                       """);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && realm.Global["recv"].AsString() != "ctor:ping")
        {
            realm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo("ctor:ping"));
        Assert.That(realm.Eval("Object.prototype.toString.call(w)").AsString(), Is.EqualTo("[object Worker]"));
        Assert.That(realm.Eval("typeof Worker").AsString(), Is.EqualTo("function"));
        Assert.That(realm.Eval("w instanceof Worker").IsTrue, Is.True);
        Assert.That(realm.Eval("typeof w.postMessage").AsString(), Is.EqualTo("function"));
        Assert.That(realm.Eval("typeof w.terminate").AsString(), Is.EqualTo("function"));
        Assert.That(realm.Eval("typeof w.eval").AsString(), Is.EqualTo("undefined"));
        Assert.That(realm.Eval("typeof w.loadModule").AsString(), Is.EqualTo("undefined"));
        Assert.That(realm.Eval("typeof w.pump").AsString(), Is.EqualTo("undefined"));
        Assert.That(realm.Eval("Object.prototype.hasOwnProperty.call(w, 'postMessage')").IsFalse, Is.True);
    }

    [Test]
    public void UseWebWorkers_ForwardOnMessageThroughWorkerWrapper()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("wrapped:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWebWorkers()
            .Build();
        var realm = engine.MainRealm;

        _ = realm.Eval("""
                       globalThis.recv = "";
                       globalThis.w = new Worker("/mods/worker-entry.js");
                       w.onmessage = function (e) { recv = e.data; };
                       w.postMessage("ping");
                       """);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && realm.Global["recv"].AsString() != "wrapped:ping")
        {
            realm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo("wrapped:ping"));
        Assert.That(realm.Eval("typeof w.onmessage").AsString(), Is.EqualTo("function"));
    }

    [Test]
    public void UseWebWorkers_WorkerConstructor_DoesNotDependOn_CreateWorker_Global()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("direct:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWebWorkers()
            .Build();
        var realm = engine.MainRealm;

        _ = realm.Eval("""
                       globalThis.recv = "";
                       delete globalThis.createWorker;
                       globalThis.w = new Worker("/mods/worker-entry.js");
                       w.onmessage = function (e) { recv = e.data; };
                       w.postMessage("ping");
                       """);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && realm.Global["recv"].AsString() != "direct:ping")
        {
            realm.PumpJobs();
            Thread.Sleep(5);
        }

        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo("direct:ping"));
    }

    [Test]
    public void UseWebWorkers_RejectsUnsupportedWorkerType()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseWebWorkers()
            .Build();
        var realm = engine.MainRealm;

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval("""
                                                                    new Worker("/mods/worker-entry.js", { type: "shared" });
                                                                    """));

        Assert.That(ex!.DetailCode, Is.EqualTo("WEB_WORKER_TYPE_INVALID"));
    }

    [Test]
    public void UseWebWorkers_CanDisableBackgroundHost_AndRequireExplicitPump()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("manual:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWebWorkers(options => options.StartBackgroundHost = false)
            .Build();
        var realm = engine.MainRealm;

        _ = realm.Eval("""
                       globalThis.recv = "";
                       onmessage = function (e) { recv = e.data; };
                       globalThis.w = createWorker("/mods/worker-entry.js");
                       w.postMessage("ping");
                       """);

        realm.PumpJobs();
        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo(string.Empty));

        _ = realm.Eval("w.pump();");
        realm.PumpJobs();

        Assert.That(realm.Global["recv"].AsString(), Is.EqualTo("manual:ping"));
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public readonly List<(string Specifier, string? Referrer)> ResolveCalls = [];

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            ResolveCalls.Add((specifier, referrer));
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

        public string LoadSource(string resolvedId)
        {
            return modules[resolvedId];
        }
    }
}
