using Okojo.Runtime;

namespace Okojo.Tests;

public class DisposeTests
{
    [Test]
    public void EngineDispose_TerminatesAllAgents()
    {
        var engine = JsRuntime.Create();
        var worker = engine.CreateWorkerAgent();

        Assert.That(engine.MainAgent.IsTerminated, Is.False);
        Assert.That(worker.IsTerminated, Is.False);

        engine.Dispose();

        Assert.That(engine.MainAgent.IsTerminated, Is.True);
        Assert.That(worker.IsTerminated, Is.True);
    }

    [Test]
    public void DisposedRuntime_Rejects_New_Execution_And_Load_Requests()
    {
        var engine = JsRuntime.Create();
        engine.Dispose();

        Assert.That(engine.IsDisposed, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(() => engine.Execute("1 + 1"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => engine.Evaluate("1 + 1"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => engine.LoadModule("/mods/a.js"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => engine.LoadWorkerScript("/workers/a.js"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _ = engine.MainRealm, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _ = engine.DefaultRealm, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _ = engine.Agents, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _ = engine.Realms, Throws.TypeOf<ObjectDisposedException>());
        });
    }
}
