using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleSampleCrashReproTests
{
    [Test]
    public void EvaluateModule_NamespaceCall_DefaultReExportedClosure_DoesNotCrash()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as shop from "./index.js";
                                export function runDemo() { return shop.getRunsDefault(); }
                                """,
            ["/mods/index.js"] = """
                                 export { default as getRunsDefault } from "./metrics.js";
                                 export { bumpRuns } from "./metrics.js";
                                 """,
            ["/mods/metrics.js"] = """
                                   export let runCount = 0;
                                   export function bumpRuns() { runCount += 1; }
                                   export default function getRuns() { return runCount; }
                                   """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("runDemo", out var runDemoValue), Is.True);
        Assert.That(runDemoValue.TryGetObject(out var runDemoObj), Is.True);
        Assert.That(runDemoObj, Is.InstanceOf<JsFunction>());

        var result = realm.Call((JsFunction)runDemoObj!, JsValue.Undefined);
        Assert.That(result.IsInt32, Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void EvaluateModule_NamespaceCall_ReExportedClosure_CapturesNonExportedModuleLocal()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as shop from "./index.js";
                                export function runDemo() {
                                    shop.addItem("tea");
                                    return shop.getSummary();
                                }
                                """,
            ["/mods/index.js"] = """
                                 export { addItem, getSummary } from "./cart.js";
                                 """,
            ["/mods/cart.js"] = """
                                const lines = [];
                                export function addItem(name) { lines.push(name); }
                                export function getSummary() { return lines.join(", "); }
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");

        Assert.That(ns.TryGetObject(out var nsObj), Is.True);
        Assert.That(nsObj, Is.Not.Null);
        Assert.That(nsObj!.TryGetProperty("runDemo", out var runDemoValue), Is.True);
        Assert.That(runDemoValue.TryGetObject(out var runDemoObj), Is.True);
        Assert.That(runDemoObj, Is.InstanceOf<JsFunction>());

        var result = realm.Call((JsFunction)runDemoObj!, JsValue.Undefined);
        Assert.That(result.IsString, Is.True);
        Assert.That(result.AsString(), Is.EqualTo("tea"));
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, string> modules = modules;

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("./", StringComparison.Ordinal))
            {
                var basePath = referrer is null
                    ? "/"
                    : referrer.Replace('\\', '/');
                var slash = basePath.LastIndexOf('/');
                var dir = slash >= 0 ? basePath[..(slash + 1)] : "/";
                return Normalize(dir + specifier[2..]);
            }

            return Normalize(specifier);
        }

        public string LoadSource(string resolvedId)
        {
            if (modules.TryGetValue(Normalize(resolvedId), out var source))
                return source;
            throw new InvalidOperationException("Module not found: " + resolvedId);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
