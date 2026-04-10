using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleGraphStateTests
{
    [Test]
    public void EvaluateModule_SetsGraphState_ToEvaluated()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = "export const a = 1;"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var worker = engine.CreateWorkerAgent();

        _ = worker.EvaluateModule(worker.MainRealm, "/mods/a.js");

        Assert.That(worker.ModuleGraph.TryGet("/mods/a.js", out var node), Is.True);
        Assert.That(node.State, Is.EqualTo(ModuleEvalState.Evaluated));
    }

    [Test]
    public void EvaluateModule_OnRuntimeFailure_SetsGraphState_ToFailed()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/bad.js"] = "export const x = (function(){ throw 1; })();"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var worker = engine.CreateWorkerAgent();

        Assert.Throws<JsRuntimeException>(() => _ = worker.EvaluateModule(worker.MainRealm, "/mods/bad.js"));

        Assert.That(worker.ModuleGraph.TryGet("/mods/bad.js", out var node), Is.True);
        Assert.That(node.State, Is.EqualTo(ModuleEvalState.Failed));
    }

    [Test]
    public void EvaluateModule_OnRepeatedRuntimeFailure_Rethrows_Original_Error()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/bad.js"] = "throw 'foo';"
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var worker = engine.CreateWorkerAgent();

        var first = Assert.Throws<JsRuntimeException>(() =>
            _ = worker.EvaluateModule(worker.MainRealm, "/mods/bad.js"));
        var second =
            Assert.Throws<JsRuntimeException>(() => _ = worker.EvaluateModule(worker.MainRealm, "/mods/bad.js"));

        Assert.That(first!.ThrownValue!.Value.AsString(), Is.EqualTo("foo"));
        Assert.That(second!.ThrownValue!.Value.AsString(), Is.EqualTo("foo"));
        Assert.That(worker.ModuleGraph.TryGet("/mods/bad.js", out var node), Is.True);
        Assert.That(node.State, Is.EqualTo(ModuleEvalState.Failed));
    }

    [Test]
    public void EvaluateModule_Cycle_SetsAllNodes_ToEvaluated()
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
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var worker = engine.CreateWorkerAgent();

        _ = worker.EvaluateModule(worker.MainRealm, "/mods/a.js");

        Assert.That(worker.ModuleGraph.TryGet("/mods/a.js", out var aNode), Is.True);
        Assert.That(worker.ModuleGraph.TryGet("/mods/b.js", out var bNode), Is.True);
        Assert.That(aNode.State, Is.EqualTo(ModuleEvalState.Evaluated));
        Assert.That(bNode.State, Is.EqualTo(ModuleEvalState.Evaluated));
    }

    [Test]
    public void LinkModule_RecursivelyCreatesDependencyNodes_WithoutEvaluation()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/a.js"] = """
                             import "./b.js";
                             export const a = 1;
                             """,
            ["/mods/b.js"] = """
                             export const b = 2;
                             """
        });
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var worker = engine.CreateWorkerAgent();

        var resolved = worker.Modules.Link(worker.MainRealm, "/mods/a.js");

        Assert.That(resolved, Is.EqualTo("/mods/a.js"));
        Assert.That(worker.ModuleGraph.TryGet("/mods/a.js", out var aNode), Is.True);
        Assert.That(worker.ModuleGraph.TryGet("/mods/b.js", out var bNode), Is.True);
        Assert.That(aNode.LinkPlan, Is.Not.Null);
        Assert.That(bNode.LinkPlan, Is.Not.Null);
        Assert.That(aNode.State, Is.EqualTo(ModuleEvalState.Uninitialized));
        Assert.That(bNode.State, Is.EqualTo(ModuleEvalState.Uninitialized));
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
