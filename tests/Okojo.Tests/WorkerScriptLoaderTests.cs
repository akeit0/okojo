using Okojo.Runtime;

namespace Okojo.Tests;

public class WorkerScriptLoaderTests
{
    [Test]
    public void LoadWorkerScript_UsesCustomLoader()
    {
        var loader = new StubLoader("custom-script");
        var engine = JsRuntime.CreateBuilder().UseWorkerScriptSourceLoader(loader).Build();

        var text = engine.LoadWorkerScript("worker.js");

        Assert.That(text, Is.EqualTo("custom-script"));
        Assert.That(loader.LastPath, Is.EqualTo("worker.js"));
        Assert.That(loader.LastReferrer, Is.Null);
    }

    [Test]
    public void LoadWorkerScript_DefaultFileLoader_ReadsFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "console.log('worker');");
            var engine = JsRuntime.Create();

            var text = engine.LoadWorkerScript(path);

            Assert.That(text, Is.EqualTo("console.log('worker');"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Test]
    public void LoadWorkerScript_DefaultFileLoader_ResolvesRelativeToReferrerDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OkojoWorkerScriptLoaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var ownerPath = Path.Combine(tempDir, "owner.js");
        var workerPath = Path.Combine(tempDir, "worker.js");

        try
        {
            File.WriteAllText(ownerPath, "// owner");
            File.WriteAllText(workerPath, "console.log('relative-worker');");
            var engine = JsRuntime.Create();

            var text = engine.LoadWorkerScript("worker.js", ownerPath);

            Assert.That(text, Is.EqualTo("console.log('relative-worker');"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private sealed class StubLoader(string content) : IWorkerScriptSourceLoader
    {
        public string? LastPath { get; private set; }
        public string? LastReferrer { get; private set; }

        public string LoadScript(string path, string? referrer = null)
        {
            LastPath = path;
            LastReferrer = referrer;
            return content;
        }
    }
}
