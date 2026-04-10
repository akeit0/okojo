using Okojo.Runtime;

namespace Okojo.Tests;

public class ReflectTests
{
    [Test]
    public void Reflect_GlobalObject_AndCoreMethods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;
                                ok = ok && typeof Reflect === "object";
                                ok = ok && Object.getPrototypeOf(Reflect) === Object.prototype;
                                ok = ok && Object.prototype.toString.call(Reflect) === "[object Reflect]";

                                var sum = Reflect.apply(function(a, b) { return this.base + a + b; }, { base: 1 }, [2, 3]);
                                ok = ok && sum === 6;

                                function C(v) { this.v = v; }
                                var c = Reflect.construct(C, [7]);
                                ok = ok && c.v === 7;

                                var o = {};
                                ok = ok && Reflect.defineProperty(o, "x", { value: 10, writable: true, configurable: true, enumerable: true }) === true;
                                ok = ok && Reflect.get(o, "x") === 10;
                                ok = ok && Reflect.has(o, "x") === true;
                                ok = ok && Reflect.set(o, "x", 11) === true;
                                ok = ok && o.x === 11;
                                var d = Reflect.getOwnPropertyDescriptor(o, "x");
                                ok = ok && d && d.value === 11 && d.writable === true && d.enumerable === true && d.configurable === true;
                                ok = ok && Reflect.deleteProperty(o, "x") === true;
                                ok = ok && Reflect.has(o, "x") === false;

                                var p = {};
                                ok = ok && Reflect.getPrototypeOf(p) === Object.prototype;
                                ok = ok && Reflect.setPrototypeOf(p, null) === true;
                                ok = ok && Reflect.getPrototypeOf(p) === null;

                                var e = {};
                                ok = ok && Reflect.isExtensible(e) === true;
                                ok = ok && Reflect.preventExtensions(e) === true;
                                ok = ok && Reflect.isExtensible(e) === false;
                                ok = ok && Reflect.defineProperty(e, "q", { value: 1 }) === false;

                                var sym = Symbol("k");
                                var k = { a: 1 };
                                k[0] = 2;
                                k[sym] = 3;
                                var keys = Reflect.ownKeys(k);
                                ok = ok && keys.length === 3;
                                ok = ok && keys[0] === "0";
                                ok = ok && keys[1] === "a";
                                ok = ok && keys[2] === sym;

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Reflect_Set_UsesReceiver_ForAccessorSetter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var proto = { set x(v) { this.y = v; } };
                                var receiver = {};
                                var ok = Reflect.set(proto, "x", 42, receiver);
                                ok === true && receiver.y === 42 && proto.y === undefined;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectSet_WithProxyReceiver_DefinesOnReceiverWithoutRecursing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = [];
                                var defineCount = 0;
                                var receiverTarget = {};
                                var receiver = new Proxy(receiverTarget, {
                                  defineProperty: function(t, key, descriptor) {
                                    defineCount++;
                                    return Reflect.defineProperty(t, key, descriptor);
                                  }
                                });

                                var ok = Reflect.set(target, "foo", 1, receiver);
                                ok === true &&
                                target.foo === undefined &&
                                receiverTarget.foo === 1 &&
                                defineCount === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Apply_AndReflectConstruct_ConsumeGenericArrayLikeArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                function join3(a, b, c) { return String(a) + "|" + String(b) + "|" + String(c); }
                                ok = ok && join3.apply(null, { 0: "A", 2: "C", length: 3 }) === "A|undefined|C";
                                ok = ok && Reflect.apply(join3, null, { 0: "X", 1: "Y", length: 2 }) === "X|Y|undefined";

                                function C(a, b, c) {
                                  this.a = a;
                                  this.b = b;
                                  this.c = c;
                                }

                                var constructed = Reflect.construct(C, { 0: "L", 2: "N", length: 3 });
                                ok = ok && constructed.a === "L" && constructed.b === undefined && constructed.c === "N";

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectOwnKeys_ProxyOwnKeys_Invariants_Are_Enforced()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = {};
                                Object.defineProperty(target, "fixed", {
                                  value: 1,
                                  configurable: false,
                                  enumerable: true,
                                  writable: true
                                });

                                var okMissingFixed = false;
                                try {
                                  Reflect.ownKeys(new Proxy(target, { ownKeys: function() { return []; } }));
                                } catch (e) {
                                  okMissingFixed = e && e.name === "TypeError";
                                }

                                var okSparseUndefined = false;
                                try {
                                  Reflect.ownKeys(new Proxy({}, { ownKeys: function() { return [, "a"]; } }));
                                } catch (e) {
                                  okSparseUndefined = e && e.name === "TypeError";
                                }

                                okMissingFixed && okSparseUndefined;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectOwnKeys_Preserves_Registered_Symbol_Identity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var sym = Symbol.for("z");
                                var obj = { [sym]: 1 };
                                var keys = Reflect.ownKeys(obj);
                                keys.length === 1 && keys[0] === sym;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectApply_And_Construct_Reject_NonObject_ArgumentLists_And_Propagate_Length_Getters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;
                                function f() {}

                                try { Reflect.apply(f, null, 1); ok = false; } catch (e) { ok = ok && e.name === "TypeError"; }
                                try { Reflect.apply(f, null, Symbol()); ok = false; } catch (e) { ok = ok && e.name === "TypeError"; }
                                try { Reflect.construct(f, 1); ok = false; } catch (e) { ok = ok && e.name === "TypeError"; }

                                try {
                                  Reflect.apply(f, null, { get length() { throw new Error("apply-length"); } });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "apply-length";
                                }

                                try {
                                  Reflect.construct(f, { get length() { throw new Error("construct-length"); } });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "construct-length";
                                }

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectHas_And_DefineProperty_Propagate_PropertyKey_Conversion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;
                                var key = {
                                  toString: function() {
                                    throw new Error("key-boom");
                                  }
                                };

                                try {
                                  Reflect.has({}, key);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "key-boom";
                                }

                                try {
                                  Reflect.defineProperty({}, key, {});
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "key-boom";
                                }

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectSet_Rejects_PrimitiveReceivers_ForDataDescriptors_And_DoesNotOverwrite_AccessorReceivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var o1 = { p: 42 };
                                var receiver = "receiver is a string";
                                ok = ok && Reflect.set(o1, "p", 43, receiver) === false;
                                ok = ok && o1.p === 42;
                                ok = ok && receiver.hasOwnProperty("p") === false;

                                var accessorReceiver = {};
                                var fn = function() {};
                                Object.defineProperty(accessorReceiver, "p", {
                                  set: fn
                                });

                                var emptyTarget = {};
                                ok = ok && Reflect.set(emptyTarget, "p", 42, accessorReceiver) === false;
                                ok = ok && Object.getOwnPropertyDescriptor(accessorReceiver, "p").set === fn;
                                ok = ok && emptyTarget.hasOwnProperty("p") === false;

                                var dataTarget = { p: 43 };
                                ok = ok && Reflect.set(dataTarget, "p", 42, accessorReceiver) === false;
                                ok = ok && Object.getOwnPropertyDescriptor(accessorReceiver, "p").set === fn;
                                ok = ok && dataTarget.p === 43;

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ReflectPreventExtensions_Rejects_VariableLength_TypedArrays_While_ObjectMethods_Throw()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var rab = new ArrayBuffer(4, { maxByteLength: 8 });
                                var rabTracking = new Uint8Array(rab);
                                ok = ok && Reflect.preventExtensions(rabTracking) === false;
                                ok = ok && Object.isExtensible(rabTracking) === true;
                                try {
                                  Object.preventExtensions(rabTracking);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var gsab = new SharedArrayBuffer(4, { maxByteLength: 8 });
                                var gsabFixed = new Uint8Array(gsab, 0, 4);
                                ok = ok && Reflect.preventExtensions(gsabFixed) === true;
                                ok = ok && Object.isExtensible(gsabFixed) === false;

                                var gsabTracking = new Uint8Array(gsab);
                                ok = ok && Reflect.preventExtensions(gsabTracking) === false;
                                ok = ok && Object.isExtensible(gsabTracking) === true;
                                try {
                                  Object.freeze(gsabTracking);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
