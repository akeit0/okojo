using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ExecutionContextModelTests
{
    [Test]
    public void Execute_EndsWithEmptyExecutionContextStack()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;

        _ = realm.Eval("""
                       function f(x) { return x + 1; }
                       f(10);
                       """);

        Assert.That(engine.MainAgent.GetExecutionContextDepth(realm), Is.EqualTo(0));
    }

    [Test]
    public void HostCall_ShowsRunningExecutionContextOnTop()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var topKind = CallFrameKind.ScriptFrame;
        var observedDepth = 0;

        realm.Global["probe"] = JsValue.FromObject(new JsHostFunction(realm, (in info) =>
        {
            var innerRealm = info.Realm;
            var snapshot = innerRealm.Agent.GetExecutionContextsSnapshot(innerRealm);
            observedDepth = snapshot.Count;
            topKind = snapshot[0].FrameKind;
            return JsValue.FromInt32(snapshot.Count);
        }, "probe", 0));

        _ = realm.Eval("""
                       function f() { return probe(); }
                       f();
                       """);

        Assert.That(observedDepth, Is.GreaterThanOrEqualTo(2));
        Assert.That(topKind, Is.EqualTo(CallFrameKind.HostExitFrame));
        Assert.That(engine.MainAgent.GetExecutionContextDepth(realm), Is.EqualTo(0));
    }
}
