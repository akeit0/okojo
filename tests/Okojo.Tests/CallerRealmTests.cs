using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class CallerRealmTests
{
    [Test]
    public void CrossRealmHostCallReportsTheCallerRealm()
    {
        using var runtime = JsRuntime.Create();
        var targetWindow = new JsWindowProxy(runtime.MainRealm);
        var target = runtime.CreateRealm(options => options.GlobalThisObject = targetWindow);
        targetWindow.SetTarget(target);

        var callerWindow = new JsWindowProxy(runtime.MainRealm);
        var caller = runtime.CreateRealm(options => options.GlobalThisObject = callerWindow);
        callerWindow.SetTarget(caller);

        JsRealm? observed = null;
        var host = new JsHostFunction(
            target,
            (in CallInfo info) =>
            {
                observed = info.CallerRealm;
                return JsValue.Undefined;
            },
            "probe",
            0
        );
        target.Global["probe"] = JsValue.FromObject(host);
        caller.Global["target"] = JsValue.FromObject(targetWindow);

        caller.Evaluate("target.probe()");
        Assert.That(observed, Is.SameAs(caller));
    }

    [Test]
    public void SameRealmHostCallHasNoCallerRealm()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var realm = runtime.CreateRealm(options => options.GlobalThisObject = window);
        window.SetTarget(realm);

        JsRealm? observed = null;
        var host = new JsHostFunction(
            realm,
            (in CallInfo info) =>
            {
                observed = info.CallerRealm;
                return JsValue.Undefined;
            },
            "probe",
            0
        );
        realm.Global["probe"] = JsValue.FromObject(host);

        realm.Evaluate("globalThis.probe()");
        Assert.That(observed, Is.Null);
    }
}
