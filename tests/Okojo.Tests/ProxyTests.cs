using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ProxyTests
{
    [Test]
    public void Proxy_GlobalExists_AndHasNoPrototypeProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                typeof Proxy === "function" && ("prototype" in Proxy) === false;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_CallWithoutNew_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                try { Proxy({}, {}); } catch (e) { ok = e && e.name === "TypeError"; }
                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Constructor_RequiresObjectTargetAndHandler()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok1 = false, ok2 = false;
                                try { new Proxy(1, {}); } catch (e) { ok1 = e && e.name === "TypeError"; }
                                try { new Proxy({}, 1); } catch (e) { ok2 = e && e.name === "TypeError"; }
                                ok1 && ok2;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_ForwardsBasicPropertyOperations()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = { x: 1 };
                                var p = new Proxy(target, {});
                                var a = (p.x === 1);
                                p.y = 2;
                                var b = (target.y === 2);
                                var c = ("x" in p);
                                var d = delete p.x;
                                var e = !("x" in target);
                                a && b && c && d && e;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ProxyRevocable_BasicSemantics_AndIdempotentRevoke()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var r = Proxy.revocable({ x: 1 }, {});
                                var shape = typeof r === "object" && typeof r.proxy === "object" && typeof r.revoke === "function";
                                var before = r.proxy.x === 1;
                                r.revoke();
                                r.revoke();
                                var threw = false;
                                try { r.proxy.x; } catch (e) { threw = e && e.name === "TypeError"; }
                                shape && before && threw;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Revoked_Proxy_Function_Throws_TypeError_From_Proxy_Realm()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        realm.Global["__createRealmForTest__"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var otherRealm = info.Realm.Agent.CreateRealm();
            return JsValue.FromObject(otherRealm.GlobalObject);
        }, "__createRealmForTest__", 0));

        var result = realm.Eval("""
                                var other = __createRealmForTest__();
                                var F = other.Function(
                                  "var proxyObj = Proxy.revocable(function() {}, {});" +
                                  "var proxy = proxyObj.proxy;" +
                                  "proxyObj.revoke();" +
                                  "return function() { return proxy(); };"
                                )();
                                try {
                                  F();
                                  false;
                                } catch (e) {
                                  e.constructor === other.TypeError;
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_OfCallableTarget_IsCallable_ButNotNecessarilyConstructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var p = new Proxy(eval, {});
                                var ok1 = (typeof p === "function");
                                var ok2 = false;
                                try { new p(); } catch (e) { ok2 = e && e.name === "TypeError"; }
                                ok1 && ok2;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Call_UsesApplyTrap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var log = [];
                                function target(a, b) { log.push("target"); return a + b; }
                                var p = new Proxy(target, {
                                  apply: function(t, thisArg, args) {
                                    log.push("apply");
                                    return t.call({ ignored: true }, args[0] * 2, args[1] * 3);
                                  }
                                });
                                p(2, 3) === 13 && log.length === 2 && log[0] === "apply" && log[1] === "target";
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Construct_UsesConstructTrap_AndRequiresObjectResult()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var called = false;
                                function Target(v) { this.v = v; }
                                var P = new Proxy(Target, {
                                  construct: function(t, args, newTarget) {
                                    called = (t === Target) && args[0] === 7 && newTarget === P;
                                    return { v: args[0] + 1 };
                                  }
                                });
                                var a = new P(7);
                                var threw = false;
                                var Bad = new Proxy(Target, {
                                  construct: function() { return 1; }
                                });
                                try { new Bad(1); } catch (e) { threw = e && e.name === "TypeError"; }
                                called && a.v === 8 && threw;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayIsArray_UnwrapsProxy_AndThrowsOnRevokedProxy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var p = new Proxy([], {});
                                var pp = new Proxy(p, {});
                                var a = Array.isArray(p) === true && Array.isArray(pp) === true;
                                var r = Proxy.revocable([], {});
                                r.revoke();
                                var threw = false;
                                try { Array.isArray(r.proxy); } catch (e) { threw = e && e.name === "TypeError"; }
                                a && threw;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectFreeze_ProxyPreventExtensionsTrapThrow_IsPropagated()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                var p = new Proxy({}, {
                                  preventExtensions: function() {
                                    throw new Error("boom");
                                  }
                                });

                                try { Object.freeze(p); } catch (e) { ok = e && e.message === "boom"; }
                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectFreeze_ProxyWithoutOwnKeys_UsesTargetOwnPropertyKeyOrder()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = {};
                                var sym = Symbol();
                                target[sym] = 1;
                                target.foo = 2;
                                target[0] = 3;

                                var seen = [];
                                var proxy = new Proxy(target, {
                                  getOwnPropertyDescriptor: function(t, key) {
                                    seen.push(key);
                                    return Reflect.getOwnPropertyDescriptor(t, key);
                                  }
                                });

                                Object.freeze(proxy);
                                seen.length === 3 && seen[0] === "0" && seen[1] === "foo" && seen[2] === sym;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_AccessorPair_WithComputedKeyBefore_DoesNotThrow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var sym = Symbol();
                                var o = {
                                  [sym]: 1,
                                  get foo() { return 1; },
                                  set foo(v) {}
                                };
                                var d = Reflect.getOwnPropertyDescriptor(o, "foo");
                                d && typeof d.get === "function" && typeof d.set === "function";
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectFreeze_ProxyDefinePropertyTrap_ReceivesPartialDescriptors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var sym = Symbol();
                                var seenDescriptors = {};
                                var proxy = new Proxy({
                                  [sym]: 1,
                                  get foo() {},
                                  set foo(_v) {},
                                }, {
                                  defineProperty: function(target, key, descriptor) {
                                    seenDescriptors[key] = descriptor;
                                    return Reflect.defineProperty(target, key, descriptor);
                                  },
                                });

                                Object.freeze(proxy);
                                seenDescriptors[sym] &&
                                seenDescriptors[sym].value === undefined &&
                                seenDescriptors[sym].writable === false &&
                                seenDescriptors[sym].enumerable === undefined &&
                                seenDescriptors[sym].configurable === false &&
                                seenDescriptors.foo &&
                                seenDescriptors.foo.get === undefined &&
                                seenDescriptors.foo.set === undefined &&
                                seenDescriptors.foo.enumerable === undefined &&
                                seenDescriptors.foo.configurable === false;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeHasOwnProperty_ThroughProxy_Uses_OwnPropertyDescriptor_Behavior()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = { a: 1 };
                                var proxy = new Proxy(target, {});
                                Object.prototype.hasOwnProperty.call(proxy, "a") === true &&
                                Object.prototype.hasOwnProperty.call(proxy, "b") === false;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_OwnKeys_Rejects_Invalid_Entries_And_Duplicates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var okInvalid = false;
                                try {
                                  Reflect.ownKeys(new Proxy({}, { ownKeys: function() { return [undefined]; } }));
                                } catch (e) {
                                  okInvalid = e && e.name === "TypeError";
                                }

                                var sym = Symbol("x");
                                var okDuplicate = false;
                                try {
                                  Reflect.ownKeys(new Proxy({}, { ownKeys: function() { return [sym, sym]; } }));
                                } catch (e) {
                                  okDuplicate = e && e.name === "TypeError";
                                }

                                okInvalid && okDuplicate;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_OwnKeys_NonExtensible_Target_Cannot_Add_Or_Drop_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var target = { a: 1 };
                                Object.preventExtensions(target);

                                var okMissing = false;
                                try {
                                  Reflect.ownKeys(new Proxy(target, { ownKeys: function() { return []; } }));
                                } catch (e) {
                                  okMissing = e && e.name === "TypeError";
                                }

                                var okExtra = false;
                                try {
                                  Reflect.ownKeys(new Proxy(target, { ownKeys: function() { return ["a", "b"]; } }));
                                } catch (e) {
                                  okExtra = e && e.name === "TypeError";
                                }

                                okMissing && okExtra;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Nested_Target_Fallback_Preserves_Receiver_And_Own_Descriptor_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var target = {
                                  get attr() {
                                    return this;
                                  }
                                };
                                var getProxy = new Proxy(target, { get: null });
                                var getParent = Object.create(getProxy);
                                ok = ok && getProxy.attr === getProxy;
                                ok = ok && getParent.attr === getParent;

                                var nestedGetTarget = new Proxy(/(?:)/i, {});
                                var nestedGetProxy = new Proxy(nestedGetTarget, {});
                                ok = ok && Object.create(nestedGetProxy).lastIndex === 0;
                                ok = ok && nestedGetProxy[Symbol.match] === RegExp.prototype[Symbol.match];

                                var nestedFunctionTarget = new Proxy(function(_arg) {}, {});
                                var nestedFunctionProxy = new Proxy(nestedFunctionTarget, {});
                                ok = ok && Object.create(nestedFunctionProxy).length === 1;

                                var arrayTarget = new Proxy([42], {});
                                var arrayProxy = new Proxy(arrayTarget, {
                                  getOwnPropertyDescriptor: undefined
                                });
                                var d0 = Reflect.getOwnPropertyDescriptor(arrayProxy, "0");
                                var dLength = Reflect.getOwnPropertyDescriptor(arrayProxy, "length");
                                ok = ok && d0.value === 42 && d0.enumerable === true && d0.configurable === true && d0.writable === true;
                                ok = ok && dLength.value === 1 && dLength.enumerable === false && dLength.configurable === false;

                                var hasTarget = new Proxy([1, 2], {});
                                var hasProxy = new Proxy(hasTarget, {});
                                ok = ok && ("length" in Object.create(hasProxy));
                                ok = ok && ("1" in hasProxy);
                                ok = ok && !("2" in hasProxy);

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Invariants_For_Has_GetPrototypeOf_And_GetOwnPropertyDescriptor_Are_Enforced()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var hasTarget = {};
                                Object.defineProperty(hasTarget, "attr", {
                                  configurable: true,
                                  value: 1
                                });
                                Object.preventExtensions(hasTarget);
                                try {
                                  "attr" in new Proxy(hasTarget, {
                                    has: function() { return false; }
                                  });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var protoBase = { foo: 1 };
                                var protoTarget = Object.create(protoBase);
                                Object.preventExtensions(protoTarget);
                                try {
                                  Object.getPrototypeOf(new Proxy(protoTarget, {
                                    getPrototypeOf: function() { return {}; }
                                  }));
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var descTarget = { bar: 1 };
                                try {
                                  Object.getOwnPropertyDescriptor(new Proxy(descTarget, {
                                    getOwnPropertyDescriptor: function(_target, prop) {
                                      var tmp = {};
                                      Object.defineProperty(tmp, "bar", {
                                        configurable: false,
                                        enumerable: true,
                                        value: 1
                                      });
                                      return Object.getOwnPropertyDescriptor(tmp, prop);
                                    }
                                  }), "bar");
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Missing_Traps_Forward_Numeric_Has_And_Writable_Special_Own_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var observedProp = null;
                                var proto = [14];
                                var target = Object.create(proto);
                                var proxy = new Proxy(target, {
                                  has: function(_target, prop) {
                                    observedProp = prop;
                                    return false;
                                  }
                                });
                                var array = [];
                                Object.setPrototypeOf(array, proxy);
                                ok = ok && (1 in array) === false;
                                ok = ok && observedProp === "1";

                                var functionTarget = new Proxy(function() {}, {});
                                var functionProxy = new Proxy(functionTarget, {});
                                functionProxy.prototype = 123;
                                ok = ok && Object.getOwnPropertyDescriptor(functionProxy, "prototype").value === 123;

                                var regexpTarget = new Proxy(/(?:)/i, {});
                                var regexpProxy = new Proxy(regexpTarget, {});
                                regexpProxy.lastIndex = 7;
                                ok = ok && Object.getOwnPropertyDescriptor(regexpProxy, "lastIndex").value === 7;

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_DefineProperty_And_DeleteProperty_Invariants_Are_Enforced()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var deleteTarget = {};
                                Object.defineProperty(deleteTarget, "attr", {
                                  configurable: false,
                                  value: 1
                                });
                                try {
                                  delete new Proxy(deleteTarget, {
                                    deleteProperty: function() { return true; }
                                  }).attr;
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var nonExtensibleTarget = {};
                                Object.preventExtensions(nonExtensibleTarget);
                                try {
                                  Object.defineProperty(new Proxy(nonExtensibleTarget, {
                                    defineProperty: function() { return true; }
                                  }), "foo", {});
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var configurableTarget = {};
                                Object.defineProperty(configurableTarget, "foo", {
                                  value: 1,
                                  configurable: true
                                });
                                try {
                                  Object.defineProperty(new Proxy(configurableTarget, {
                                    defineProperty: function() { return true; }
                                  }), "foo", {
                                    value: 1,
                                    configurable: false
                                  });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var writableTarget = {};
                                try {
                                  Object.defineProperty(new Proxy(writableTarget, {
                                    defineProperty: function(t, prop) {
                                      Object.defineProperty(t, prop, {
                                        configurable: false,
                                        writable: true
                                      });
                                      return true;
                                    }
                                  }), "prop", {
                                    configurable: false,
                                    writable: false
                                  });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var reflectWritableTarget = new Proxy({}, {
                                  defineProperty: function(t, prop) {
                                    Object.defineProperty(t, prop, {
                                      configurable: false,
                                      writable: true
                                    });
                                    return true;
                                  }
                                });
                                try {
                                  Reflect.defineProperty(reflectWritableTarget, "prop", {
                                    writable: false
                                  });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Missing_Set_Trap_Rechecks_Receiver_Own_Descriptor_On_Every_Call()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var getOwnPropertyKeys = [];
                                var definePropertyKeys = [];
                                var p = new Proxy({ foo: 1 }, {
                                  getOwnPropertyDescriptor: function(target, key) {
                                    getOwnPropertyKeys.push(key);
                                    return Reflect.getOwnPropertyDescriptor(target, key);
                                  },
                                  defineProperty: function(target, key, desc) {
                                    definePropertyKeys.push(key);
                                    return Reflect.defineProperty(target, key, desc);
                                  }
                                });

                                p.foo = 2;
                                p.foo = 2;
                                p.foo = 2;

                                ok = ok && getOwnPropertyKeys.join(",") === "foo,foo,foo";
                                ok = ok && definePropertyKeys.join(",") === "foo,foo,foo";

                                getOwnPropertyKeys = [];
                                definePropertyKeys = [];

                                p[22] = false;
                                p[22] = false;
                                p[22] = false;

                                ok = ok && getOwnPropertyKeys.join(",") === "22,22,22";
                                ok = ok && definePropertyKeys.join(",") === "22,22,22";

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Get_Invariants_Are_Enforced()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var target = {};
                                Object.defineProperty(target, "attr", {
                                  configurable: false,
                                  writable: false,
                                  value: 1
                                });
                                try {
                                  (new Proxy(target, {
                                    get: function() { return 2; }
                                  })).attr;
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                var accessorTarget = {};
                                Object.defineProperty(accessorTarget, "attr", {
                                  configurable: false,
                                  get: undefined
                                });
                                try {
                                  (new Proxy(accessorTarget, {
                                    get: function() { return 2; }
                                  })).attr;
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                ok;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Proxy_Apply_And_Construct_Use_Caller_Realm_Argument_Array()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherEval"] = otherRealm.Global["eval"];
        realm.Global["OtherArray"] = JsValue.FromObject(otherRealm.ArrayConstructor);
        realm.Global["OtherArrayPrototype"] = JsValue.FromObject(otherRealm.ArrayPrototype);

        var result = realm.Eval("""
                                var f = OtherEval('new Proxy(function() {}, { apply: function(_, __, args) { return args; } })');
                                var c = OtherEval('new Proxy(function() {}, { construct: function(_, args) { return args; } })');
                                var applied = f();
                                var constructed = new c();
                                [
                                  applied.constructor === Array,
                                  Object.getPrototypeOf(applied) === Array.prototype,
                                  applied.constructor === OtherArray,
                                  Object.getPrototypeOf(applied) === OtherArrayPrototype,
                                  constructed.constructor === Array,
                                  Object.getPrototypeOf(constructed) === Array.prototype,
                                  constructed.constructor === OtherArray,
                                  Object.getPrototypeOf(constructed) === OtherArrayPrototype
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true|false|false|true|true|false|false"));
    }

    [Test]
    public void Proxy_GetFunctionRealm_Uses_Target_Function_Realm_For_NewTarget_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var realm1 = realm.Agent.CreateRealm();
        var realm2 = realm.Agent.CreateRealm();
        var realm3 = realm.Agent.CreateRealm();
        realm.Global["Realm1Function"] = JsValue.FromObject(realm1.FunctionConstructor);
        realm.Global["Realm1Array"] = JsValue.FromObject(realm1.ArrayConstructor);
        realm.Global["Realm1ArrayPrototype"] = JsValue.FromObject(realm1.ArrayPrototype);
        realm.Global["Realm2Proxy"] = JsValue.FromObject(realm2.ProxyConstructor);
        realm.Global["Realm3Array"] = JsValue.FromObject(realm3.ArrayConstructor);

        var result = realm.Eval("""
                                var newTarget = new Realm1Function();
                                newTarget.prototype = false;
                                var newTargetProxy = new Realm2Proxy(newTarget, {});
                                var array = Reflect.construct(Realm3Array, [], newTargetProxy);
                                [
                                  array instanceof Realm1Array,
                                  Object.getPrototypeOf(array) === Realm1ArrayPrototype
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true"));
    }
}
