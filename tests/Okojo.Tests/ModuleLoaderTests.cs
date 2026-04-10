using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleLoaderTests
{
    [Test]
    public void LoadWorkerScript_UsesModuleLoaderByDefault()
    {
        var moduleLoader = new StubModuleLoader("module-source");
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(moduleLoader).Build();

        var text = engine.LoadWorkerScript("worker-entry.js");

        Assert.That(text, Is.EqualTo("module-source"));
        Assert.That(moduleLoader.LastResolveSpecifier, Is.EqualTo("worker-entry.js"));
        Assert.That(moduleLoader.LastLoadResolvedId, Is.EqualTo("resolved:worker-entry.js"));
    }

    [Test]
    public void WorkerScriptLoader_OverridesModuleLoader()
    {
        var moduleLoader = new StubModuleLoader("module-source");
        var workerLoader = new StubWorkerLoader("worker-source");
        var engine = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(moduleLoader)
            .UseWorkerScriptSourceLoader(workerLoader)
            .Build();

        var text = engine.LoadWorkerScript("worker-entry.js");

        Assert.That(text, Is.EqualTo("worker-source"));
        Assert.That(workerLoader.LastPath, Is.EqualTo("worker-entry.js"));
        Assert.That(workerLoader.LastReferrer, Is.Null);
    }

    [Test]
    public void LoadWorkerScript_ForwardsReferrerToModuleLoaderFallback()
    {
        var moduleLoader = new StubModuleLoader("module-source");
        var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(moduleLoader).Build();

        var text = engine.LoadWorkerScript("./worker-entry.js", "/mods/owner.js");

        Assert.That(text, Is.EqualTo("module-source"));
        Assert.That(moduleLoader.LastResolveSpecifier, Is.EqualTo("./worker-entry.js"));
        Assert.That(moduleLoader.LastResolveReferrer, Is.EqualTo("/mods/owner.js"));
    }

    private sealed class StubModuleLoader(string source) : IModuleSourceLoader
    {
        public string? LastResolveSpecifier { get; private set; }
        public string? LastResolveReferrer { get; private set; }
        public string? LastLoadResolvedId { get; private set; }

        public string ResolveSpecifier(string specifier, string? referrer)
        {
            LastResolveSpecifier = specifier;
            LastResolveReferrer = referrer;
            return $"resolved:{specifier}";
        }

        public string LoadSource(string resolvedId)
        {
            LastLoadResolvedId = resolvedId;
            return source;
        }
    }

    private sealed class StubWorkerLoader(string source) : IWorkerScriptSourceLoader
    {
        public string? LastPath { get; private set; }
        public string? LastReferrer { get; private set; }

        public string LoadScript(string path, string? referrer = null)
        {
            LastPath = path;
            LastReferrer = referrer;
            return source;
        }
    }
}
