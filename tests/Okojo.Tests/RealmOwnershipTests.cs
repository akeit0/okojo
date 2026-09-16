using System.Runtime.CompilerServices;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class RealmOwnershipTests
{
    [Test]
    public void ReleaseRemovesRegistryOwnershipWithoutReusingIds()
    {
        using var runtime = JsRuntime.Create();
        var main = runtime.MainRealm;
        var previousId = main.Id;
        for (var index = 0; index < 100; index++)
        {
            var child = runtime.CreateRealm();
            Assert.That(child.Id, Is.GreaterThan(previousId));
            previousId = child.Id;
            Assert.That(runtime.Realms, Has.Count.EqualTo(2));
            Assert.That(runtime.ReleaseRealm(child), Is.True);
            Assert.That(runtime.ReleaseRealm(child), Is.False);
            Assert.That(runtime.Realms, Is.EqualTo(new[] { main }));
            Assert.That(runtime.MainRealm, Is.SameAs(main));
        }
    }

    [Test]
    public void ReleasePreservesReferencesAndQueuedPromises()
    {
        using var runtime = JsRuntime.Create();
        var parent = runtime.MainRealm;
        var child = runtime.CreateRealm();
        parent.Global["saved"] = child.Evaluate("globalThis.value = 42; () => value");
        parent.Global["childPromise"] = child.Evaluate("Promise.resolve(43)");
        parent.Execute(
            "globalThis.result = 0; childPromise.then(value => { result = value; });",
            pumpJobsAfterRun: false
        );

        Assert.That(runtime.ReleaseRealm(child), Is.True);
        parent.PumpJobs();
        Assert.That(parent.Evaluate("saved()").NumberValue, Is.EqualTo(42));
        Assert.That(parent.Global["result"].NumberValue, Is.EqualTo(43));
        Assert.That(runtime.MainAgent.IsTerminated, Is.False);
    }

    [Test]
    [NonParallelizable]
    public void ReleasedRealmCanBeCollectedWhileRuntimeRemainsAlive()
    {
        using var runtime = JsRuntime.Create();
        var weak = CreateReleasedRealm(runtime);
        for (var attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.That(weak.IsAlive, Is.False);
        Assert.That(runtime.MainRealm.Evaluate("1 + 2").NumberValue, Is.EqualTo(3));
        GC.KeepAlive(runtime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateReleasedRealm(JsRuntime runtime)
    {
        var child = runtime.CreateRealm();
        child.Execute("globalThis.document = { text: 'old document' };");
        runtime.ReleaseRealm(child);
        return new WeakReference(child);
    }

    [Test]
    public void ReentrantAndFailedInitializationDoNotReuseRealmIds()
    {
        using var runtime = JsRuntime.Create();
        JsRealm? nested = null;
        var outer = runtime.CreateRealm(options =>
            options.Initialize = _ => nested = runtime.CreateRealm()
        );
        Assert.That(nested, Is.Not.Null);
        Assert.That(nested!.Id, Is.GreaterThan(outer.Id));
        Assert.That(runtime.ReleaseRealm(outer), Is.True);
        Assert.That(runtime.ReleaseRealm(nested), Is.True);
        var failedId = -1;
        Assert.Throws<InvalidOperationException>(() =>
            runtime.CreateRealm(options =>
                options.Initialize = realm =>
                {
                    failedId = realm.Id;
                    throw new InvalidOperationException("initialization failed");
                }
            )
        );
        var replacement = runtime.CreateRealm();
        Assert.That(replacement.Id, Is.GreaterThan(failedId));
        Assert.That(runtime.Realms, Has.Count.EqualTo(2));
    }

    [Test]
    public void ReleaseRejectsMainNullAndForeignRealms()
    {
        using var runtime = JsRuntime.Create();
        using var other = JsRuntime.Create();
        Assert.Throws<ArgumentNullException>(() => runtime.ReleaseRealm(null!));
        Assert.Throws<ArgumentException>(() => runtime.ReleaseRealm(runtime.MainRealm));
        Assert.Throws<ArgumentException>(() => runtime.ReleaseRealm(other.MainRealm));
        Assert.Throws<ArgumentException>(() =>
            runtime.ReleaseRealm(runtime.CreateWorkerAgent().MainRealm)
        );
        Assert.That(runtime.Realms, Has.Count.EqualTo(1));
    }

    [Test]
    public void ReleaseRejectsDisposedRuntime()
    {
        var runtime = JsRuntime.Create();
        var child = runtime.CreateRealm();
        runtime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runtime.ReleaseRealm(child));
    }
}
