using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleApiRefineTests
{
    [Test]
    public void Modules_Resolve_WhenResolverThrows_WrapsAsModuleResolveFailed()
    {
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(new ThrowingResolveLoader()).Build();
        var ex = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.Resolve("/mods/a.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_RESOLVE_FAILED"));
    }

    [Test]
    public void Modules_Link_WhenLoadThrows_WrapsAsModuleLoadFailed()
    {
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(new ThrowingLoadLoader()).Build();
        var ex = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.Link("/mods/a.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LOAD_FAILED"));
    }

    [Test]
    public void Modules_Link_WhenParseThrows_WrapsAsModuleParseFailed()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export { ;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var ex = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.Link("/mods/a.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_PARSE_FAILED"));
    }

    [Test]
    public void Modules_EvaluateNamespace_WhenLinkFails_WrapsAsModuleLinkFailed()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = "export const ok = 1;",
            ["/mods/a.js"] = "import { missing } from './dep.js'; export const y = missing;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var ex = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.EvaluateNamespace("/mods/a.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("MODULE_LINK_FAILED"));
    }

    [Test]
    public void Modules_TryGetCachedNamespace_ReturnsFalse_WhenNotCached_ThenTrueAfterEvaluate()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export const a = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var modules = engine.MainAgent.Modules;

        Assert.That(modules.TryGetCachedNamespace("/mods/a.js", out var miss), Is.False);
        Assert.That(miss.IsUndefined, Is.True);

        _ = modules.Evaluate("/mods/a.js");

        Assert.That(modules.TryGetCachedNamespace("/mods/a.js", out var ns), Is.True);
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj!.TryGetProperty("a", out var a), Is.True);
        Assert.That(a.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Modules_GetState_Tracks_Link_And_Evaluate()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export const a = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var modules = engine.MainAgent.Modules;

        var before = modules.GetState("/mods/a.js");
        Assert.That(before.Exists, Is.False);
        Assert.That(before.HasLinkPlan, Is.False);

        _ = modules.Link("/mods/a.js");
        var linked = modules.GetState("/mods/a.js");
        Assert.That(linked.Exists, Is.True);
        Assert.That(linked.HasLinkPlan, Is.True);
        Assert.That(linked.State, Is.EqualTo(JsAgent.JsAgentModuleApi.ModuleStateKind.Uninitialized));

        _ = modules.Evaluate("/mods/a.js");
        var evaluated = modules.GetState("/mods/a.js");
        Assert.That(evaluated.Exists, Is.True);
        Assert.That(evaluated.State, Is.EqualTo(JsAgent.JsAgentModuleApi.ModuleStateKind.Evaluated));
    }

    [Test]
    public void Modules_Invalidate_RemovesCachedModule_AndForcesReload()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export const a = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var modules = engine.MainAgent.Modules;

        _ = modules.Evaluate("/mods/a.js");
        Assert.That(loader.LoadCount("/mods/a.js"), Is.EqualTo(1));

        Assert.That(modules.Invalidate("/mods/a.js"), Is.True);
        Assert.That(modules.TryGetCachedNamespace("/mods/a.js", out _), Is.False);
        Assert.That(modules.GetState("/mods/a.js").Exists, Is.False);

        _ = modules.Evaluate("/mods/a.js");
        Assert.That(loader.LoadCount("/mods/a.js"), Is.EqualTo(2));
    }

    [Test]
    public void Modules_Invalidate_WithImportersScope_RemovesImporterClosure_AndForcesReload()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/c.js"] = "export const value = 1;",
            ["/mods/b.js"] = "import { value } from './c.js'; export const middle = value + 1;",
            ["/mods/a.js"] = "import { middle } from './b.js'; export const top = middle + 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var modules = engine.MainAgent.Modules;

        _ = modules.Evaluate("/mods/a.js");
        Assert.That(loader.LoadCount("/mods/a.js"), Is.EqualTo(1));
        Assert.That(loader.LoadCount("/mods/b.js"), Is.EqualTo(1));
        Assert.That(loader.LoadCount("/mods/c.js"), Is.EqualTo(1));

        var result = modules.Invalidate(
            "/mods/c.js",
            JsAgent.JsAgentModuleApi.ModuleInvalidationScope.Importers);

        Assert.That(result.ResolvedIds, Is.EqualTo(new[] { "/mods/a.js", "/mods/b.js", "/mods/c.js" }));
        Assert.That(modules.GetState("/mods/a.js").Exists, Is.False);
        Assert.That(modules.GetState("/mods/b.js").Exists, Is.False);
        Assert.That(modules.GetState("/mods/c.js").Exists, Is.False);

        _ = modules.Evaluate("/mods/a.js");
        Assert.That(loader.LoadCount("/mods/a.js"), Is.EqualTo(2));
        Assert.That(loader.LoadCount("/mods/b.js"), Is.EqualTo(2));
        Assert.That(loader.LoadCount("/mods/c.js"), Is.EqualTo(2));
    }

    [Test]
    public void Modules_Clear_RemovesAllCachedModules_AndForcesReload()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export const a = 1;",
            ["/mods/b.js"] = "export const b = 2;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var modules = engine.MainAgent.Modules;

        _ = modules.Evaluate("/mods/a.js");
        _ = modules.Evaluate("/mods/b.js");
        Assert.That(modules.GetState("/mods/a.js").Exists, Is.True);
        Assert.That(modules.GetState("/mods/b.js").Exists, Is.True);

        modules.Clear();

        Assert.That(modules.GetState("/mods/a.js").Exists, Is.False);
        Assert.That(modules.GetState("/mods/b.js").Exists, Is.False);

        _ = modules.Evaluate("/mods/a.js");
        _ = modules.Evaluate("/mods/b.js");
        Assert.That(loader.LoadCount("/mods/a.js"), Is.EqualTo(2));
        Assert.That(loader.LoadCount("/mods/b.js"), Is.EqualTo(2));
    }

    [Test]
    public void Modules_GetState_WithIncludeError_ReturnsLastFailureDiagnostic()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "boom(); export const x = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        realm.Global["boom"] = JsValue.FromObject(new JsHostFunction(realm, "boom", 0,
            static (in info) => { throw new InvalidOperationException("boom exploded"); }));

        _ = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.Evaluate("/mods/a.js"));

        var state = engine.MainAgent.Modules.GetState("/mods/a.js", true);
        Assert.That(state.Exists, Is.True);
        Assert.That(state.State, Is.EqualTo(JsAgent.JsAgentModuleApi.ModuleStateKind.Failed));
        Assert.That(state.LastError, Is.Not.Null);
        Assert.That(state.LastError!.Value.DetailCode, Is.EqualTo("MODULE_EXEC_FAILED"));
        Assert.That(state.LastError.Value.ExceptionType, Does.Contain("JsRuntimeException"));
        Assert.That(state.LastError.Value.InnerExceptionType, Does.Contain("InvalidOperationException"));
        Assert.That(state.LastError.Value.Message, Does.Contain("/mods/a.js"));
    }

    [Test]
    public void Modules_GetState_Default_DoesNotIncludeFailureDiagnostic()
    {
        var loader = new CountingModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "boom(); export const x = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        realm.Global["boom"] = JsValue.FromObject(new JsHostFunction(realm, "boom", 0,
            static (in info) => { throw new InvalidOperationException("boom exploded"); }));

        _ = Assert.Throws<JsRuntimeException>(() => _ = engine.MainAgent.Modules.Evaluate("/mods/a.js"));

        var state = engine.MainAgent.Modules.GetState("/mods/a.js");
        Assert.That(state.Exists, Is.True);
        Assert.That(state.State, Is.EqualTo(JsAgent.JsAgentModuleApi.ModuleStateKind.Failed));
        Assert.That(state.LastError, Is.Null);
    }

    private sealed class CountingModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, int> loadCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> modules = modules;

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

        public string LoadSource(string resolvedId)
        {
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);

            loadCounts.TryGetValue(resolvedId, out var count);
            loadCounts[resolvedId] = count + 1;
            return source;
        }

        public int LoadCount(string resolvedId)
        {
            return loadCounts.TryGetValue(resolvedId, out var count) ? count : 0;
        }
    }

    private sealed class ThrowingResolveLoader : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            throw new InvalidOperationException("resolve exploded");
        }

        public string LoadSource(string resolvedId)
        {
            throw new InvalidOperationException("not reached");
        }
    }

    private sealed class ThrowingLoadLoader : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            return specifier;
        }

        public string LoadSource(string resolvedId)
        {
            throw new InvalidOperationException("load exploded");
        }
    }
}
