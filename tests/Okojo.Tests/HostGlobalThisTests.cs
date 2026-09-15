using System.Runtime.CompilerServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Tests;

public class HostGlobalThisTests
{
    private static JsRealm Attach(JsRuntime runtime, JsWindowProxy window)
    {
        var realm = runtime.CreateRealm(options => options.GlobalThisObject = window);
        window.SetTarget(realm);
        return realm;
    }

    private sealed class Trace : IDebuggerSession
    {
        public readonly List<string> Instructions = [];

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            if (checkpoint.CurrentFrameInfo.FunctionName == "accessor")
                Instructions.Add($"{checkpoint.ProgramCounter}: {checkpoint.CurrentOpcode}");
        }
    }

    private sealed class ModuleLoader(string source) : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer) => specifier;

        public string LoadSource(string resolvedId) => source;
    }

    [TestCase(false), TestCase(true)]
    public void ModuleEntryThisIsUndefined(bool asyncModule)
    {
        using var runtime = JsRuntime
            .CreateBuilder()
            .UseModuleSourceLoader(
                new ModuleLoader(
                    "globalThis.before = this === undefined; "
                        + (asyncModule ? "await 0; " : "")
                        + "globalThis.after = this === undefined; export const moduleThis = this;"
                )
            )
            .Build();
        var window = new JsWindowProxy(runtime.MainRealm);
        var realm = Attach(runtime, window);
        realm.Import("https://example.test/module.js");
        Assert.That(realm.Evaluate("before && after && this === globalThis").IsTrue, Is.True);
    }

    [Test]
    public void GlobalAccessorUsesReflectReceiverWithVmTrace()
    {
        var trace = new Trace();
        using var runtime = JsRuntime
            .CreateBuilder()
            .UseAgent(agent =>
            {
                agent.DebuggerSession = trace;
                agent.SetCheckInterval(1);
            })
            .Build();
        var result = runtime.MainRealm.Evaluate(
            "Object.defineProperty(globalThis,'access',{get:function accessor(){return this.marker;}}); Reflect.get(globalThis,'access',{marker:42})"
        );
        Assert.That(result.NumberValue, Is.EqualTo(42));
        TestContext.Out.WriteLine(string.Join("\n", trace.Instructions));
        Assert.That(trace.Instructions.Count, Is.GreaterThan(0));
    }

    [Test]
    public void WarmPropertySitesAndNestedProxiesFollowReplacement()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var first = Attach(runtime, window);
        first.Execute("globalThis.value = 1;");
        runtime.MainRealm.Global["w"] = window;
        runtime.MainRealm.Execute(
            "globalThis.p = new Proxy(w,Object.create(null)); function read(){return p.value + w.value;} for(let i=0;i<1000;i++){read();} Object.prototype.get = () => 999;"
        );
        var next = Attach(runtime, window);
        next.Execute("globalThis.value = 2;");
        Assert.That(runtime.MainRealm.Evaluate("read()").NumberValue, Is.EqualTo(4));
        Assert.That(
            runtime
                .MainRealm.Evaluate("Object.getPrototypeOf(w) === Object.getPrototypeOf(p)")
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void ScriptEvalFunctionsAndHostCallbacksUseConfiguredThis()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var realm = Attach(runtime, window);
        realm.Execute(
            "globalThis.scriptThis = this; function sloppy(){return this;} function strict(){'use strict';return this;}"
        );
        Assert.That(
            realm
                .Evaluate(
                    "scriptThis === globalThis && sloppy() === globalThis && strict() === undefined && (0,eval)('this') === globalThis && Function('return this')() === globalThis"
                )
                .IsTrue,
            Is.True
        );
        var callback = (JsFunction)
            realm
                .Evaluate("(function(){'use strict'; globalThis.callbackThis = this;})")
                .AsObject();
        realm.QueueMicrotask(callback);
        realm.PumpJobs();
        Assert.That(realm.Evaluate("callbackThis === globalThis").IsTrue, Is.True);
        Assert.That(realm.Evaluate("this").AsObject(), Is.SameAs(window));
    }

    [Test]
    public void NavigationPreservesReferencesButNotGlobalBindings()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var old = Attach(runtime, window);
        old.Execute(
            "var value = 1; let lexical = 10; globalThis.document = {}; function saved(){ return [value, lexical, this.value, globalThis.value]; }"
        );
        runtime.MainRealm.Global["w"] = window;
        runtime.MainRealm.Global["saved"] = old.Global["saved"];
        runtime.MainRealm.Global["oldDocument"] = old.Global["document"];
        var next = Attach(runtime, window);
        next.Execute("var value = 2; globalThis.document = {};");
        runtime.ReleaseRealm(old);
        Assert.That(
            runtime.MainRealm.Evaluate("JSON.stringify(saved())").AsString(),
            Is.EqualTo("[1,10,2,2]")
        );
        Assert.That(
            runtime
                .MainRealm.Evaluate(
                    "w.document !== oldDocument && w.value === 2 && w === w.globalThis"
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void DescriptorsKeysSymbolsAndReceiversFollowTarget()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var first = Attach(runtime, window);
        var caller = runtime.MainRealm;
        caller.Global["w"] = window;
        first.Execute("var declared = 1;");
        caller.Execute(
            "Object.defineProperty(w,'fixed',{value:1}); w[7] = 7; globalThis.key = Symbol(); w[key] = 9; Object.defineProperty(w,'access',{get(){return this.marker;}, configurable:true});"
        );
        Assert.That(
            caller
                .Evaluate(
                    "JSON.stringify([Object.getOwnPropertyDescriptor(w,'fixed').configurable, Reflect.ownKeys(w).includes('declared'), Reflect.ownKeys(w).includes(key), w[7], Reflect.get(w,'access',{marker:42}), Reflect.set(w,'fixed',2)])"
                )
                .AsString(),
            Is.EqualTo("[false,true,true,7,42,false]")
        );
        Attach(runtime, window);
        caller.Execute("Object.defineProperty(w,'fixed',{value:2}); w[7] = 8; w[key] = 10;");
        Assert.That(
            caller
                .Evaluate(
                    "w.fixed === 2 && w[7] === 8 && w[key] === 10 && !('declared' in w) && !Reflect.ownKeys(w).includes('declared') && Object.getOwnPropertyDescriptor(new Proxy(w,{}),'fixed').value === 2"
                )
                .IsTrue,
            Is.True
        );
        caller.Execute("w.temporary = 1; delete w.temporary; Reflect.set(w,'other',3,{marker:0});");
        Assert.That(caller.Evaluate("!('temporary' in w) && !('other' in w)").IsTrue, Is.True);
    }

    [Test]
    public void WindowCannotBeFrozenOrHaveItsPrototypeChanged()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        Attach(runtime, window);
        var caller = runtime.MainRealm;
        caller.Global["w"] = window;
        Assert.That(
            caller
                .Evaluate(
                    "Object.isExtensible(w) && !Reflect.preventExtensions(w) && !Reflect.setPrototypeOf(w,{}) && Reflect.setPrototypeOf(w,Object.getPrototypeOf(w))"
                )
                .IsTrue,
            Is.True
        );
        Assert.That(
            caller
                .Evaluate(
                    "(() => { try { Object.freeze(w); return false; } catch(e) { return e instanceof TypeError; } })()"
                )
                .IsTrue,
            Is.True
        );
    }

    [Test]
    public void DetachmentDeniesAccessAndCanBeReattached()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        Attach(runtime, window).Execute("globalThis.value = 1;");
        runtime.MainRealm.Global["w"] = window;
        window.SetTarget(null);
        Assert.That(window.TargetRealm, Is.Null);
        foreach (
            var operation in new[]
            {
                "w.value",
                "w.value = 1",
                "'value' in w",
                "Object.keys(w)",
                "Object.getOwnPropertyDescriptor(w,'value')",
                "Object.getPrototypeOf(w)",
            }
        )
            Assert.Throws<JsRuntimeException>(() => runtime.MainRealm.Evaluate(operation));
        Attach(runtime, window).Execute("globalThis.value = 2;");
        Assert.That(runtime.MainRealm.Evaluate("w.value").NumberValue, Is.EqualTo(2));
    }

    [Test]
    public void RejectsForeignAgentObjects()
    {
        using var runtime = JsRuntime.Create();
        using var other = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        Assert.Throws<ArgumentException>(() => window.SetTarget(other.MainRealm));
        Assert.Throws<ArgumentException>(() =>
            other.CreateRealm(options => options.GlobalThisObject = window)
        );
    }

    [Test, NonParallelizable]
    public void RetargetingDoesNotRootThePreviousDocument()
    {
        using var runtime = JsRuntime.Create();
        var window = new JsWindowProxy(runtime.MainRealm);
        var weak = Replace(runtime, window);
        for (var i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.That(weak.IsAlive, Is.False);
        GC.KeepAlive(window);
        GC.KeepAlive(runtime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Replace(JsRuntime runtime, JsWindowProxy window)
    {
        var old = Attach(runtime, window);
        old.Execute("globalThis.document = {};");
        runtime.ReleaseRealm(old);
        Attach(runtime, window);
        return new WeakReference(old);
    }
}
