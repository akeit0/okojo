using Okojo.Runtime;

namespace Okojo.Tests;

public class WorkerModuleLoadingTests
{
    [Test]
    public void CreateWorker_WithModuleEntry_LoadsAndExecutesEntrySource()
    {
        var loader = new CountingModuleLoader("""
                                              onmessage = function (e) { postMessage("entry:" + e.data); };
                                              """);
        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           globalThis.w = createWorker("entry.js");
                           w.postMessage("ping");
                           w.pump();
                           """);

        mainRealm.PumpJobs();
        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("entry:ping"));
        Assert.That(loader.LoadSourceCallCount, Is.EqualTo(1));
    }

    [Test]
    public void WorkerHandle_LoadModule_UsesPerAgentModuleCache()
    {
        var loader = new CountingModuleLoader("globalThis.hit = (globalThis.hit || 0) + 1;");
        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.w = createWorker();
                           w.loadModule("mod.js");
                           w.loadModule("mod.js");
                           """);

        Assert.That(loader.LoadSourceCallCount, Is.EqualTo(1));
        var worker = engine.Agents.First(a => a.Kind == JsAgentKind.Worker);
        Assert.That(worker.CachedModuleCount, Is.EqualTo(1));
        Assert.That(worker.CachedModuleRecordCount, Is.EqualTo(1));
    }

    [Test]
    public void WorkerModuleCache_IsolatedPerAgent()
    {
        var loader = new CountingModuleLoader("globalThis.k = 1;");
        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.w1 = createWorker();
                           globalThis.w2 = createWorker();
                           w1.loadModule("shared.js");
                           w2.loadModule("shared.js");
                           """);

        Assert.That(loader.LoadSourceCallCount, Is.EqualTo(2));
        var workers = engine.Agents.Where(a => a.Kind == JsAgentKind.Worker).ToArray();
        Assert.That(workers.Length, Is.EqualTo(2));
        Assert.That(workers[0].CachedModuleCount, Is.EqualTo(1));
        Assert.That(workers[1].CachedModuleCount, Is.EqualTo(1));
        Assert.That(workers[0].CachedModuleRecordCount, Is.EqualTo(1));
        Assert.That(workers[1].CachedModuleRecordCount, Is.EqualTo(1));
    }

    [Test]
    public void WorkerModule_ImportRelative_UsesReferrerAwareResolve()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import { value } from "./dep.js";
                                onmessage = function (e) { postMessage(value + ":" + e.data); };
                                """,
            ["/mods/dep.js"] = """export const value = "dep-ok";"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = mainRealm.Eval("""
                           globalThis.recv = "";
                           onmessage = function (e) { recv = e.data; };
                           globalThis.w = createWorker("/mods/main.js");
                           w.postMessage("ping");
                           w.pump();
                           """);

        mainRealm.PumpJobs();
        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("dep-ok:ping"));
        Assert.That(loader.LoadSourceCallCount, Is.EqualTo(2));
        Assert.That(loader.ResolveCalls.Any(c =>
            c.Specifier == "./dep.js" && c.Referrer == "/mods/main.js"), Is.True);
    }

    [Test]
    public void CreateWorker_ModuleEntry_RelativeToActiveModuleReferrer_UsesOwnerModuleUrl()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/owner.js"] = """
                                 globalThis.recv = "";
                                 onmessage = function (e) { recv = e.data; };
                                 globalThis.w = createWorker("./worker-entry.js");
                                 """,
            ["/mods/worker-entry.js"] = """
                                        onmessage = function (e) { postMessage("entry:" + e.data); };
                                        """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;

        _ = engine.MainAgent.EvaluateModule(mainRealm, "/mods/owner.js");
        _ = mainRealm.Eval("""
                           w.postMessage("ping");
                           w.pump();
                           recv;
                           """);
        mainRealm.PumpJobs();

        Assert.That(mainRealm.Global["recv"].AsString(), Is.EqualTo("entry:ping"));
        Assert.That(loader.ResolveCalls.Any(c =>
            c.Specifier == "./worker-entry.js" && c.Referrer == "/mods/owner.js"), Is.True);
    }

    [Test]
    public void WorkerModule_ExportFromAndExportStar_AreAvailable()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export { a as aa } from "./dep.js";
                                export * from "./dep2.js";
                                """,
            ["/mods/dep.js"] = """export const a = 10;""",
            ["/mods/dep2.js"] = """export const b = 20;"""
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var result = mainRealm.Eval("""
                                    var w = createWorker();
                                    var ns = w.loadModule("/mods/main.js");
                                    ns.aa + ns.b;
                                    """);

        Assert.That(result.NumberValue, Is.EqualTo(30d));
    }

    [Test]
    public void WorkerModule_Cycle_DoesNotThrow_ForSideEffectImports()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """
                             import "./b.js";
                             export const a = 1;
                             """,
            ["/mods/b.js"] = """
                             import "./a.js";
                             export const b = 2;
                             """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var result = mainRealm.Eval("""
                                    var w = createWorker();
                                    var ns = w.loadModule("/mods/a.js");
                                    ns.a;
                                    """);

        Assert.That(result.NumberValue, Is.EqualTo(1d));
    }

    [Test]
    public void WorkerModule_ExportFunction_Works_WithSnapshotValueSemantics()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/live.js"] = """
                                export let x = 1;
                                export function inc() { x = x + 1; }
                                """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var result = mainRealm.Eval("""
                                    var w = createWorker();
                                    var ns = w.loadModule("/mods/live.js");
                                    var isFn = typeof ns.inc === "function";
                                    ns.inc();
                                    isFn && ns.x === 1;
                                    """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void WorkerModule_TopLevelAwait_IsSupported()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/tla.js"] = """
                               export const value = await Promise.resolve(7);
                               """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .UseWorkerGlobals()
            .Build();
        var mainRealm = engine.MainRealm;
        var result = mainRealm.Eval("""
                                    var w = createWorker();
                                    var ns = w.loadModule("/mods/tla.js");
                                    ns.value;
                                    """);

        Assert.That(result.IsInt32, Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(7));
    }

    private sealed class CountingModuleLoader(string source) : IModuleSourceLoader
    {
        public int ResolveCallCount { get; private set; }
        public int LoadSourceCallCount { get; private set; }

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            ResolveCallCount++;
            return "resolved:" + specifier;
        }

        public string LoadSource(string resolvedId)
        {
            LoadSourceCallCount++;
            return source;
        }
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public readonly List<(string Specifier, string? Referrer)> ResolveCalls = [];
        public int LoadSourceCallCount { get; private set; }

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
            LoadSourceCallCount++;
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);
            return source;
        }
    }
}
