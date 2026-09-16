using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class WindowAccessPolicyTests
{
    private sealed class Policy(JsRealm fullAccess, JsValue denied) : IWindowAccessPolicy
    {
        public WindowAccessDecision CheckMemberAccess(JsRealm accessingRealm, string? member)
        {
            if (ReferenceEquals(accessingRealm, fullAccess))
                return WindowAccessDecision.Allow;
            return member is "postMessage" or "closed" or "length"
                ? WindowAccessDecision.Allow
                : WindowAccessDecision.Deny(denied);
        }
    }

    [Test]
    public void DeniedRealmIsRestrictedToAllowedMembers()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var allowed = runtime.CreateRealm(options => options.GlobalThisObject = window);
        window.SetTarget(allowed);
        allowed.Evaluate(
            "globalThis.secret = 7; globalThis.closed = false; globalThis.postMessage = function () {};"
        );

        var deniedWindow = new JsWindowProxy(runtime.MainRealm);
        var denied = runtime.CreateRealm(options => options.GlobalThisObject = deniedWindow);
        deniedWindow.SetTarget(denied);
        var thrown = denied.Evaluate("new Error('blocked')");
        window.SetAccessPolicy(new Policy(allowed, thrown));
        denied.Global["target"] = JsValue.FromObject(window);

        // The target's own realm stays unrestricted.
        Assert.That(allowed.Evaluate("globalThis.secret").NumberValue, Is.EqualTo(7));

        // Allowed cross-origin members resolve.
        Assert.That(denied.Evaluate("target.closed").IsFalse, Is.True);
        Assert.That(denied.Evaluate("typeof target.postMessage").AsString(), Is.EqualTo("function"));

        // Every other named member throws the host's value.
        Assert.That(
            denied
                .Evaluate(
                    "(function(){ try { target.secret; return 'value'; } catch (e) { return e.message; } })()"
                )
                .AsString(),
            Is.EqualTo("blocked")
        );
        Assert.That(
            denied
                .Evaluate(
                    "(function(){ try { 'secret' in target; return 'value'; } catch (e) { return e.message; } })()"
                )
                .AsString(),
            Is.EqualTo("blocked")
        );
        Assert.That(
            denied
                .Evaluate(
                    "(function(){ try { Object.keys(target); return 'value'; } catch (e) { return e.message; } })()"
                )
                .AsString(),
            Is.EqualTo("blocked")
        );
        Assert.That(
            denied
                .Evaluate(
                    "(function(){ try { Object.getOwnPropertyDescriptor(target, 'secret'); return 'value'; } catch (e) { return e.message; } })()"
                )
                .AsString(),
            Is.EqualTo("blocked")
        );
    }

    [Test]
    public void NullPolicyKeepsUnrestrictedAccess()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var first = runtime.CreateRealm(options => options.GlobalThisObject = window);
        window.SetTarget(first);
        first.Evaluate("globalThis.secret = 3;");

        var secondWindow = new JsWindowProxy(runtime.MainRealm);
        var second = runtime.CreateRealm(options => options.GlobalThisObject = secondWindow);
        secondWindow.SetTarget(second);
        second.Global["target"] = JsValue.FromObject(window);
        window.SetAccessPolicy(null);

        Assert.That(second.Evaluate("target.secret").NumberValue, Is.EqualTo(3));
    }
}
