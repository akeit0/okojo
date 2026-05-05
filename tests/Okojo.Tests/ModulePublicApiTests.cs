using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModulePublicApiTests
{
    [Test]
    public void Realm_LoadModule_And_Call_Exports_Work()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export function f(x) { return x + 1; }
                                """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var realm = engine.MainRealm;
        var module = realm.LoadModule("/mods/main.js");

        Assert.That(module.IsCompleted, Is.True);
        Assert.That(module.Object.TryGetProperty("f", out var fnValue), Is.True);
        var result = realm.Call(fnValue, JsValue.Undefined, JsValue.FromInt32(41));
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Realm_LoadModule_Provides_Module_Oriented_Access()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export function f(x) { return x + 1; }
                                export const value = 7;
                                """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var module = engine.MainRealm.LoadModule("/mods/main.js");

        Assert.That(module.ResolvedId, Is.EqualTo("/mods/main.js"));
        Assert.That(module.GetExport("value").Int32Value, Is.EqualTo(7));
        Assert.That(module.CallExport("f", JsValue.FromInt32(41)).Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Realm_LoadModule_ForSyncModule_IsAlreadyCompleted()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.js"] = """export const value = 7;"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var result = engine.MainRealm.LoadModule("/mods/value.js");

        Assert.That(result.IsCompleted, Is.True);
        Assert.That(result.CompletionValue.TryGetObject(out var obj), Is.True);
        Assert.That(obj, Is.SameAs(result.Object));
        Assert.That(result.GetExport("value").Int32Value, Is.EqualTo(7));
    }

    [Test]
    public async Task Realm_LoadModule_ForTopLevelAwaitModule_CanBeAwaited_FromCSharp()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/async.js"] = """
                                 export const stage = await Promise.resolve("ready");
                                 export function read() { return stage; }
                                 """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var result = engine.MainRealm.LoadModule("/mods/async.js");

        Assert.That(result.IsCompleted, Is.False);
        var completion = await engine.MainRealm.ToTask(result.CompletionValue);
        Assert.That(completion.TryGetObject(out var completionObj), Is.True);
        Assert.That(completionObj, Is.SameAs(result.Object));

        var module = await result.ToTask();
        Assert.That(module.GetExport("stage").AsString(), Is.EqualTo("ready"));
        Assert.That(module.CallExport("read").AsString(), Is.EqualTo("ready"));
    }

    [Test]
    public void Engine_LoadModule_Exposes_Namespace_Object()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.js"] = """export const value = 7;"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var module = engine.LoadModule("/mods/value.js");

        Assert.That(module.Object.TryGetProperty("value", out var value), Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Engine_LoadModule_Uses_Default_Realm()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.js"] = """export const value = 7;"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var module = engine.LoadModule("/mods/value.js");

        Assert.That(module.Realm, Is.SameAs(engine.MainRealm));
        Assert.That(module.GetExport("value").Int32Value, Is.EqualTo(7));
    }

    [Test]
    public async Task Engine_LoadModule_Supports_Awaitable_Completion_In_Default_Realm()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/value.js"] = """
                                 export const value = await Promise.resolve(7);
                                 """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var result = engine.LoadModule("/mods/value.js");
        var module = await result.ToTask();

        Assert.That(result.Realm, Is.SameAs(engine.MainRealm));
        Assert.That(module.GetExport("value").Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Realm_LoadWorkerScript_UsesExplicitReferrer()
    {
        var workerLoader = new TrackingWorkerScriptLoader("worker-source");
        using var engine = JsRuntime.CreateBuilder()
            .UseWorkerScriptSourceLoader(workerLoader)
            .Build();
        var realm = engine.MainRealm;

        var text = realm.LoadWorkerScript("./worker.js", "/mods/owner.js");

        Assert.That(text, Is.EqualTo("worker-source"));
        Assert.That(workerLoader.LastPath, Is.EqualTo("./worker.js"));
        Assert.That(workerLoader.LastResolveReferrer, Is.EqualTo("/mods/owner.js"));
    }

    [Test]
    public void Realm_LoadModule_InfersActiveModuleReferrer_WhenNotExplicitlyProvided()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/owner.js"] = """
                                 export const ok = __loadRelativeModuleValue__();
                                 """,
            ["/mods/dep.js"] = """export const value = 9;"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();
        var realm = engine.MainRealm;
        realm.Global["__loadRelativeModuleValue__"] = JsValue.FromObject(new JsHostFunction(
            realm,
            "__loadRelativeModuleValue__",
            0,
            static (in info) =>
            {
                _ = info.Realm.LoadModule("./dep.js");
                return JsValue.FromInt32(9);
            },
            false));

        var module = realm.LoadModule("/mods/owner.js");

        Assert.That(module.IsCompleted, Is.True);
        Assert.That(loader.ResolveCalls.Any(c =>
            c.Specifier == "./dep.js" && c.Referrer == "/mods/owner.js"), Is.True);
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
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);
            return source;
        }
    }

    private sealed class TrackingWorkerScriptLoader(string source) : IWorkerScriptSourceLoader
    {
        public string? LastPath { get; private set; }
        public string? LastReferrer { get; private set; }
        public string? LastResolveReferrer { get; private set; }

        public string ResolveScript(string path, string? referrer = null)
        {
            LastPath = path;
            LastResolveReferrer = referrer;
            return path;
        }

        public string LoadScript(string path, string? referrer = null)
        {
            LastPath = path;
            LastReferrer = referrer;
            return source;
        }
    }
}
