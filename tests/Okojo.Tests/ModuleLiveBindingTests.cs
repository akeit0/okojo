using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleLiveBindingTests
{
    [Test]
    public void EvaluateModule_ExportedLocalBinding_IsLive()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/live.js"] = """
                                export let x = 1;
                                export function inc() { x = x + 1; }
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/live.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("x", out var before), Is.True);
        Assert.That(before.Int32Value, Is.EqualTo(1));

        Assert.That(nsObj.TryGetProperty("inc", out var incValue), Is.True);
        Assert.That(incValue.TryGetObject(out var incObj), Is.True);
        Assert.That(incObj, Is.InstanceOf<JsFunction>());
        _ = realm.InvokeFunction((JsFunction)incObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(nsObj.TryGetProperty("x", out var after), Is.True);
        Assert.That(after.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_ExportFromBinding_IsLive()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export let x = 1;
                               export function inc() { x = x + 1; }
                               """,
            ["/mods/main.js"] = """export { x, inc } from "./dep.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("inc", out var incValue), Is.True);
        Assert.That(incValue.TryGetObject(out var incObj), Is.True);
        Assert.That(incObj, Is.InstanceOf<JsFunction>());
        _ = realm.InvokeFunction((JsFunction)incObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(nsObj.TryGetProperty("x", out var xValue), Is.True);
        Assert.That(xValue.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_ExportStarBinding_IsLive()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/dep.js"] = """
                               export let x = 1;
                               export function inc() { x = x + 1; }
                               """,
            ["/mods/main.js"] = """export * from "./dep.js";"""
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);

        Assert.That(nsObj!.TryGetProperty("inc", out var incValue), Is.True);
        Assert.That(incValue.TryGetObject(out var incObj), Is.True);
        Assert.That(incObj, Is.InstanceOf<JsFunction>());
        _ = realm.InvokeFunction((JsFunction)incObj!, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(nsObj.TryGetProperty("x", out var xValue), Is.True);
        Assert.That(xValue.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void EvaluateModule_NamespaceObject_GetOwnPropertyDescriptor_On_Uninitialized_Export_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as ns from "./main.js";
                                Object.getOwnPropertyDescriptor(ns, "local1");
                                export let local1 = 23;
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() => engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.ReferenceError));
    }

    [Test]
    public void EvaluateModule_NamespaceObject_Freeze_With_Exported_Bindings_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as ns from "./main.js";
                                export var local1;
                                Object.freeze(ns);
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ex = Assert.Throws<JsRuntimeException>(() => engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void EvaluateModule_NamespaceObject_IsFrozen_Remains_False_After_Freeze_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as ns from "./main.js";
                                try { Object.freeze(ns); } catch {}
                                export var local1;
                                export default Object.isFrozen(ns);
                                """
        });

        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("default", out var frozenValue), Is.True);
        Assert.That(frozenValue.IsFalse, Is.True);
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
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

        public string LoadSource(string resolvedId)
        {
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);
            return source;
        }
    }
}
