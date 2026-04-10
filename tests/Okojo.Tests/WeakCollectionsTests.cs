using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class WeakCollectionsTests
{
    [Test]
    public void WeakCollection_Global_Surface_Exists()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   [
                                                                     typeof WeakMap,
                                                                     typeof WeakSet,
                                                                     typeof WeakRef,
                                                                     WeakMap.prototype[Symbol.toStringTag],
                                                                     WeakSet.prototype[Symbol.toStringTag],
                                                                     WeakRef.prototype[Symbol.toStringTag]
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|function|function|WeakMap|WeakSet|WeakRef"));
    }

    [Test]
    public void WeakMap_Basic_Object_Key_Methods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const key = {};
                                                                   const wm = new WeakMap();
                                                                   wm.set(key, 42);
                                                                   [wm.has(key), wm.get(key), wm.delete(key), wm.has(key)].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|42|true|false"));
    }

    [Test]
    public void WeakSet_Basic_Object_Value_Methods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const key = {};
                                                                   const ws = new WeakSet();
                                                                   ws.add(key);
                                                                   [ws.has(key), ws.delete(key), ws.has(key)].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|false"));
    }

    [Test]
    public void WeakRef_Deref_Returns_Target()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const target = {};
                                                                   const ref = new WeakRef(target);
                                                                   ref.deref() === target;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void WeakRef_Uses_NewTarget_Prototype_And_Rejects_Registered_Symbols()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let result = [];
                                                                   const wr = Reflect.construct(WeakRef, [{}], Object);
                                                                   result.push(Object.getPrototypeOf(wr) === Object.prototype);
                                                                   result.push(Symbol.keyFor(Symbol.for("x")) === "x");
                                                                   try { new WeakRef(Symbol.for("x")); result.push("no"); } catch (e) { result.push(e.name); }
                                                                   result.join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|TypeError"));
    }

    [Test]
    public void FinalizationRegistry_Global_Minimal_Surface_Exists()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const registry = new FinalizationRegistry(function() {});
                                                                   [
                                                                     typeof FinalizationRegistry,
                                                                     FinalizationRegistry.prototype[Symbol.toStringTag],
                                                                     Object.getPrototypeOf(registry) === FinalizationRegistry.prototype
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|FinalizationRegistry|true"));
    }

    [Test]
    public void FinalizationRegistry_Register_And_Unregister_Validate_WeaklyHeld_Inputs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const registry = new FinalizationRegistry(function() {});
                                                                   const target = {};
                                                                   let out = [];
                                                                   try { registry.register(Symbol.for("x")); out.push("no"); } catch (e) { out.push(e.name); }
                                                                   try { registry.register(target, undefined, Symbol.for("x")); out.push("no"); } catch (e) { out.push(e.name); }
                                                                   try { registry.unregister(Symbol.for("x")); out.push("no"); } catch (e) { out.push(e.name); }
                                                                   out.join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError|TypeError|TypeError"));
    }

    [Test]
    public void FinalizationRegistry_Register_Rejects_Same_Target_And_HeldValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const registry = new FinalizationRegistry(function() {});
                                                                   const target = {};
                                                                   let out = [];
                                                                   try { registry.register(target, target); out.push("no"); } catch (e) { out.push(e.name); }
                                                                   const sym = Symbol("x");
                                                                   try { registry.register(sym, sym); out.push("no"); } catch (e) { out.push(e.name); }
                                                                   out.join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError|TypeError"));
    }

    [Test]
    public void FinalizationRegistry_Unregister_Removes_Object_And_Symbol_Tokens()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const registry = new FinalizationRegistry(function() {});
                                                                   const t1 = {};
                                                                   const t2 = {};
                                                                   const token1 = {};
                                                                   const token2 = Symbol("u");
                                                                   registry.register(t1, 1, token1);
                                                                   registry.register(t1, 2, token1);
                                                                   registry.register(t2, 3, token2);
                                                                   [
                                                                     registry.unregister(token1),
                                                                     registry.unregister(token1),
                                                                     registry.unregister(token2),
                                                                     registry.unregister(token2)
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|false|true|false"));
    }

    [Test]
    public void WeakRef_Adds_Target_To_KeptObjects_During_Execution_And_Clears_Afterward()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var external = new JsPlainObject(realm);
        realm.Global["external"] = JsValue.FromObject(external);
        realm.Global["__isKeptAlive__"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj))
                return JsValue.False;
            return innerRealm.Agent.IsKeptAlive(obj) ? JsValue.True : JsValue.False;
        }, "__isKeptAlive__", 1));

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const refObj = new WeakRef(external);
                                                                   [
                                                                     __isKeptAlive__(external),
                                                                     refObj.deref() === external,
                                                                     __isKeptAlive__(external)
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|true"));
        Assert.That(realm.Agent.IsKeptAlive(external), Is.False);
    }

    [Test]
    public void WeakRef_Target_Remains_Kept_Alive_Through_Microtasks_Of_Current_Turn()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var external = new JsPlainObject(realm);
        realm.Global["external"] = JsValue.FromObject(external);
        realm.Global["__isKeptAlive__"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj))
                return JsValue.False;
            return innerRealm.Agent.IsKeptAlive(obj) ? JsValue.True : JsValue.False;
        }, "__isKeptAlive__", 1));

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.__out = [];
                                                                   const refObj = new WeakRef(external);
                                                                   Promise.resolve().then(() => {
                                                                     __out.push(__isKeptAlive__(external));
                                                                     __out.push(refObj.deref() === external);
                                                                   });
                                                                   __out.push(__isKeptAlive__(external));
                                                                   "scheduled";
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Global["__out"].TryGetObject(out var outObj), Is.True);
        Assert.That(outObj!.TryGetElement(0, out var first), Is.True);
        Assert.That(outObj.TryGetElement(1, out var second), Is.True);
        Assert.That(outObj.TryGetElement(2, out var third), Is.True);
        Assert.That(first.IsTrue, Is.True);
        Assert.That(second.IsTrue, Is.True);
        Assert.That(third.IsTrue, Is.True);
        Assert.That(realm.Agent.IsKeptAlive(external), Is.False);
    }

    [Test]
    public void Symbol_SameValue_Distinguishes_Different_Symbol_Instances()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const a = Symbol("x");
                                                                   const b = Symbol("x");
                                                                   [
                                                                     Object.is(a, b),
                                                                     Object.is(Symbol.hasInstance, Symbol.hasInstance),
                                                                     Object.is(a, a)
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("false|true|true"));
    }

    [Test]
    public void Agent_Can_Explicitly_Collect_Weak_Target_And_Run_Finalization_Cleanup_Job()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.target = {};
                                                                   globalThis.out = [];
                                                                   globalThis.refObj = new WeakRef(target);
                                                                   globalThis.registry = new FinalizationRegistry(v => out.push(v));
                                                                   registry.register(target, "held", target);
                                                                   "ready";
                                                                   """));

        realm.Execute(script);

        var target = realm.Global["target"];
        Assert.That(realm.Agent.NotifyWeakTargetCollected(target), Is.True);
        realm.PumpJobs();

        var refObj = realm.Global["refObj"].AsObject() as JsWeakRefObject;
        Assert.That(refObj, Is.Not.Null);
        Assert.That(refObj!.Deref().IsUndefined, Is.True);

        Assert.That(realm.Global["out"].TryGetObject(out var outObj), Is.True);
        Assert.That(outObj!.TryGetElement(0, out var held), Is.True);
        Assert.That(held.AsString(), Is.EqualTo("held"));
    }

    [Test]
    public void Agent_Does_Not_Collect_Target_While_It_Is_Kept_Alive()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var target = new JsPlainObject(realm);
        var weakRef = new JsWeakRefObject(realm, target, realm.WeakRefPrototype);

        realm.Agent.AddToKeptObjects(target);

        Assert.That(realm.Agent.NotifyWeakTargetCollected(JsValue.FromObject(target)), Is.False);
        Assert.That(weakRef.Deref().TryGetObject(out var sameTarget), Is.True);
        Assert.That(sameTarget, Is.SameAs(target));

        realm.Agent.ClearKeptObjects();
        Assert.That(realm.Agent.NotifyWeakTargetCollected(JsValue.FromObject(target)), Is.True);
        Assert.That(weakRef.Deref().IsUndefined, Is.True);
    }

    [Test]
    public void FinalizationRegistry_Cleanup_Callback_Throw_Is_Reported_To_Host_Hook()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var observed = JsValue.Undefined;
        realm.FinalizationRegistryCleanupError += value => observed = value;

        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.target = {};
                                                                   globalThis.registry = new FinalizationRegistry(function() {
                                                                     throw new Error("cleanup boom");
                                                                   });
                                                                   registry.register(target, "held", target);
                                                                   "ready";
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Agent.NotifyWeakTargetCollected(realm.Global["target"]), Is.True);
        realm.PumpJobs();

        Assert.That(observed.TryGetObject(out var errorObj), Is.True);
        Assert.That(errorObj!.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("message"), out var message, out _),
            Is.True);
        Assert.That(message.AsString(), Is.EqualTo("cleanup boom"));
    }

    [Test]
    public void Agent_Explicit_Collection_Removes_WeakMap_And_WeakSet_Object_Entries()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.target = {};
                                                                   globalThis.wm = new WeakMap();
                                                                   globalThis.ws = new WeakSet();
                                                                   wm.set(target, 123);
                                                                   ws.add(target);
                                                                   [wm.has(target), ws.has(target)].join("|");
                                                                   """));

        realm.Execute(script);

        var target = realm.Global["target"];
        Assert.That(realm.Agent.NotifyWeakTargetCollected(target), Is.True);
        var weakMap = (JsWeakMapObject)realm.Global["wm"].AsObject();
        var weakSet = (JsWeakSetObject)realm.Global["ws"].AsObject();
        var targetObject = target.AsObject();
        Assert.That(weakMap.HasKey(targetObject), Is.False);
        Assert.That(weakMap.TryGetValue(targetObject, out var mapped), Is.False);
        Assert.That(mapped.IsUndefined, Is.True);
        Assert.That(weakSet.HasValue(targetObject), Is.False);
    }

    [Test]
    public void Agent_Explicit_Collection_Removes_WeakMap_And_WeakSet_Symbol_Entries()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.target = Symbol("wk");
                                                                   globalThis.wm = new WeakMap();
                                                                   globalThis.ws = new WeakSet();
                                                                   wm.set(target, 123);
                                                                   ws.add(target);
                                                                   [wm.has(target), ws.has(target)].join("|");
                                                                   """));

        realm.Execute(script);

        var target = realm.Global["target"];
        Assert.That(realm.Agent.NotifyWeakTargetCollected(target), Is.True);
        var weakMap = (JsWeakMapObject)realm.Global["wm"].AsObject();
        var weakSet = (JsWeakSetObject)realm.Global["ws"].AsObject();
        var targetSymbol = target.AsSymbol();
        Assert.That(weakMap.HasKey(targetSymbol), Is.False);
        Assert.That(weakMap.TryGetValue(targetSymbol, out var mapped), Is.False);
        Assert.That(mapped.IsUndefined, Is.True);
        Assert.That(weakSet.HasValue(targetSymbol), Is.False);
    }

    [Test]
    public void WeakMap_And_WeakSet_Use_CrossRealm_NewTarget_Fallback_Prototype()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const other = globalThis.__createRealmForTest__();
                                                                   const MapTarget = new other.Function();
                                                                   MapTarget.prototype = null;
                                                                   const SetTarget = new other.Function();
                                                                   SetTarget.prototype = null;
                                                                   const map = Reflect.construct(WeakMap, [], MapTarget);
                                                                   const set = Reflect.construct(WeakSet, [], SetTarget);
                                                                   [
                                                                     Object.getPrototypeOf(map) === other.WeakMap.prototype,
                                                                     Object.getPrototypeOf(set) === other.WeakSet.prototype
                                                                   ].join("|");
                                                                   """));

        realm.Global["__createRealmForTest__"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var otherRealm = innerRealm.Agent.CreateRealm();
            return JsValue.FromObject(otherRealm.GlobalObject);
        }, "__createRealmForTest__", 0));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true"));
    }

    [Test]
    public void WeakCollections_Support_Symbol_Keys_And_Targets()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const sym = Symbol("x");
                                                                   const wm = new WeakMap();
                                                                   wm.set(sym, 7);
                                                                   const ws = new WeakSet();
                                                                   ws.add(sym);
                                                                   const wr = new WeakRef(sym);
                                                                   [
                                                                     wm.get(sym),
                                                                     wm.has(sym),
                                                                     ws.has(sym),
                                                                     wr.deref() === sym
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("7|true|true|true"));
    }

    [Test]
    public void WeakMap_And_WeakSet_Reject_NonObject_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let out = "";
                                                                   try { new WeakMap().set(1, 2); } catch (e) { out += e.name; }
                                                                   try { new WeakSet().add(1); } catch (e) { out += "|" + e.name; }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError|TypeError"));
    }

    [Test]
    [Ignore("GC-driven weak target collection is nondeterministic; local smoke test only.")]
    public void WeakRef_Object_Target_May_Clear_After_GC_Collect()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var weakRef = CreateWeakRefWithCollectableObjectTarget(realm);

        realm.Agent.ClearKeptObjects();

        for (var i = 0; i < 8 && !weakRef.Deref().IsUndefined; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.That(weakRef.Deref().IsUndefined, Is.True,
            "WeakRef target should clear after forced CLR collection in this smoke test.");
    }

    private static JsWeakRefObject CreateWeakRefWithCollectableObjectTarget(JsRealm realm)
    {
        var target = new JsPlainObject(realm);
        return new(realm, target, realm.WeakRefPrototype);
    }
}
