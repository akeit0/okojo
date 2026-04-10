using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ObjectConstructorProgressTests
{
    [TestCase("assign")]
    [TestCase("create")]
    [TestCase("defineProperties")]
    [TestCase("defineProperty")]
    [TestCase("entries")]
    [TestCase("freeze")]
    [TestCase("fromEntries")]
    [TestCase("getOwnPropertyDescriptor")]
    [TestCase("getOwnPropertyDescriptors")]
    [TestCase("getOwnPropertyNames")]
    [TestCase("getOwnPropertySymbols")]
    [TestCase("getPrototypeOf")]
    [TestCase("groupBy")]
    [TestCase("hasOwn")]
    [TestCase("is")]
    [TestCase("isExtensible")]
    [TestCase("isFrozen")]
    [TestCase("isSealed")]
    [TestCase("keys")]
    [TestCase("preventExtensions")]
    [TestCase("seal")]
    [TestCase("setPrototypeOf")]
    [TestCase("values")]
    public void ObjectMethod_Exists(string methodName)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["name"] = JsValue.FromString(methodName);
        var result = realm.Eval("typeof Object[name] === 'function';");
        Assert.That(result.IsTrue, Is.True, $"Object.{methodName} should exist as a function.");
    }

    [Test]
    public void ObjectMethod_BasicBehaviorMatrix()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var checks = [];
            function expectNotSupported(fn) {
              try { fn(); return false; } catch (e) { return true; }
            }

            checks.push((function(){ var o = {a:1}; var r = Object.assign(o, {b:2}); return r === o && o.b === 2; })());
            checks.push(Object.getPrototypeOf(Object.create(null)) === null);
            checks.push((function(){ var o={}; Object.defineProperty(o, "x", { value: 1 }); return o.x === 1; })());
            checks.push((function(){ var o={}; Object.defineProperties(o, { a: { value: 1 }, b: { value: 2 } }); return o.a === 1 && o.b === 2; })());
            checks.push((function(){ var e = Object.entries({a:1}); return e.length === 1 && e[0][0] === "a" && e[0][1] === 1; })());
            checks.push(Object.isFrozen(Object.freeze({a:1})) === true);
            checks.push((function(){ var o = Object.fromEntries([["a",1],["b",2]]); return o.a === 1 && o.b === 2; })());
            checks.push(Object.getOwnPropertyDescriptor({a:1}, "a").value === 1);
            checks.push((function(){ var d = Object.getOwnPropertyDescriptors({a:1}); return d.a && d.a.value === 1; })());
            checks.push(Object.getOwnPropertyNames({a:1})[0] === "a");
            checks.push((function(){ return Object.getOwnPropertySymbols({}).length === 0; })());
            checks.push(Object.getPrototypeOf({}) === Object.prototype);
            checks.push((function(){
              var g = Object.groupBy([1,2,3], function(x){ return x % 2 ? "odd" : "even"; });
              return Object.getPrototypeOf(g) === null &&
                     Object.keys(g).join("|") === "odd|even" &&
                     g.odd.length === 2 && g.odd[0] === 1 && g.odd[1] === 3 &&
                     g.even.length === 1 && g.even[0] === 2;
            })());
            checks.push(Object.hasOwn({a:1}, "a") === true && Object.hasOwn({a:1}, "b") === false);
            checks.push(Object.is(NaN, NaN) === true);
            checks.push(Object.isExtensible({}) === true);
            checks.push(Object.isFrozen(Object.freeze({})) === true);
            checks.push(Object.isSealed(Object.seal({})) === true);
            checks.push(Object.keys({a:1})[0] === "a");
            checks.push(Object.isExtensible(Object.preventExtensions({})) === false);
            checks.push(Object.isSealed(Object.seal({a:1})) === true);
            checks.push((function(){ var o={}; var p={x:1}; Object.setPrototypeOf(o,p); return o.x === 1; })());
            checks.push((function(){ var v = Object.values({a:1}); return v.length === 1 && v[0] === 1; })());
            var ok = true;
            for (var i = 0; i < checks.length; i++) {
              if (!checks[i]) { ok = false; break; }
            }
            ok;
            """);

        Assert.That(result.IsTrue, Is.True, "Object progress behavior matrix should pass.");
    }

    [Test]
    public void ObjectAssign_ThrowsTypeError_WhenTargetWriteFails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var ok = false;
            try { Object.assign("a", [1]); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectAssign_ThrowsWhenInvokedAsConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var ok = false;
            try { new Object.assign({}, { a: 1 }); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectKeys_Uses_Proxy_OwnKeys_And_Enumerable_Descriptor_Traps()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var log = [];
            var target = {};
            var proxy = new Proxy(target, {
              ownKeys: function() {
                log.push("ownKeys");
                return ["z", "1", Symbol("skip")];
              },
              getOwnPropertyDescriptor: function(_, key) {
                log.push("gopd:" + String(key));
                if (key === "z" || key === "1") {
                  return { configurable: true, enumerable: true, value: 1 };
                }
                if (typeof key === "symbol") {
                  return { configurable: true, enumerable: true, value: 1 };
                }
              }
            });
            var keys = Object.keys(proxy);
            keys.length === 2 &&
            keys[0] === "z" &&
            keys[1] === "1" &&
            log.join(",") === "ownKeys,gopd:z,gopd:1";
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectAssign_CopiesStringSourceIndices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var out = Object.assign({}, "12");
            out[0] === "1" && out[1] === "2";
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectAssign_ToArray_TargetLengthPropertyIsApplied()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var target = [1,2,3];
            Object.assign(target, { length: 1 });
            target.length === 1 && target[0] === 1 && target[1] === undefined;
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectCreate_WithPropertyDescriptors_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var p = { base: 1 };
            var o = Object.create(p, {
              x: { value: 7, enumerable: true, writable: false, configurable: false }
            });
            var d = Object.getOwnPropertyDescriptor(o, "x");
            Object.getPrototypeOf(o) === p &&
            o.x === 7 &&
            d.value === 7 &&
            d.enumerable === true &&
            d.writable === false &&
            d.configurable === false;
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectSetPrototypeOf_ThrowsOnCycleAndNonExtensible()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var ok1 = false;
            var ok2 = false;
            try { Object.setPrototypeOf(Object.prototype, Array.prototype); } catch (e) { ok1 = e && e.name === "TypeError"; }
            var o = {};
            Object.preventExtensions(o);
            try { Object.setPrototypeOf(o, null); } catch (e) { ok2 = e && e.name === "TypeError"; }
            ok1 && ok2;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectSetPrototypeOf_PropertyDescriptor_IsNonEnumerable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var d = Object.getOwnPropertyDescriptor(Object, "setPrototypeOf");
            d.writable === true && d.enumerable === false && d.configurable === true;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectSetPrototypeOf_PrimitiveTarget_ReturnsPrimitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            Object.setPrototypeOf(1, null) === 1;
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertyDescriptors_ProxyObservableOrder_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var log = [];
            var target = { a: 1, b: 2 };
            var p = new Proxy(target, {
              ownKeys: function(t) { log.push("ownKeys"); return ["a","b"]; },
              getOwnPropertyDescriptor: function(t, k) { log.push("gopd:" + k); return Reflect.getOwnPropertyDescriptor(t, k); }
            });
            var d = Object.getOwnPropertyDescriptors(p);
            d.a.value === 1 &&
            d.b.value === 2 &&
            log.join("|") === "ownKeys|gopd:a|gopd:b";
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertyDescriptors_Proxy_Filters_Undefined_Descriptor_Without_Mutating_OwnKeys_Result()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var key = "a";
            var ownKeys = [key];
            var proxy = new Proxy({}, {
              getOwnPropertyDescriptor: function() {},
              ownKeys: function() { return ownKeys; }
            });

            var keys = Reflect.ownKeys(proxy);
            var descriptor = Object.getOwnPropertyDescriptor(proxy, key);
            var descriptors = Object.getOwnPropertyDescriptors(proxy);

            keys !== ownKeys &&
            Array.isArray(keys) &&
            keys.length === ownKeys.length &&
            keys[0] === ownKeys[0] &&
            descriptor === undefined &&
            !(key in descriptors);
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectLiteral_InferredName_Function_Does_Not_Shadow_Outer_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var ownKeys = ["a"];
            var fn = { ownKeys: function() { return ownKeys; } }.ownKeys;
            var returned = fn();

            returned === ownKeys &&
            Array.isArray(returned) &&
            returned.length === 1 &&
            returned[0] === "a" &&
            fn.name === "ownKeys";
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertyDescriptors_Proxy_Filters_Undefined_Descriptor_With_AllowProxyTraps_Helper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function allowProxyTraps(overrides) {
              function throwTest262Error(msg) {
                return function () { throw new Error(msg); };
              }
              if (!overrides) { overrides = {}; }
              return {
                getPrototypeOf: overrides.getPrototypeOf || throwTest262Error('[[GetPrototypeOf]] trap called'),
                setPrototypeOf: overrides.setPrototypeOf || throwTest262Error('[[SetPrototypeOf]] trap called'),
                isExtensible: overrides.isExtensible || throwTest262Error('[[IsExtensible]] trap called'),
                preventExtensions: overrides.preventExtensions || throwTest262Error('[[PreventExtensions]] trap called'),
                getOwnPropertyDescriptor: overrides.getOwnPropertyDescriptor || throwTest262Error('[[GetOwnProperty]] trap called'),
                has: overrides.has || throwTest262Error('[[HasProperty]] trap called'),
                get: overrides.get || throwTest262Error('[[Get]] trap called'),
                set: overrides.set || throwTest262Error('[[Set]] trap called'),
                deleteProperty: overrides.deleteProperty || throwTest262Error('[[Delete]] trap called'),
                defineProperty: overrides.defineProperty || throwTest262Error('[[DefineOwnProperty]] trap called'),
                enumerate: throwTest262Error('[[Enumerate]] trap called: this trap has been removed'),
                ownKeys: overrides.ownKeys || throwTest262Error('[[OwnPropertyKeys]] trap called'),
                apply: overrides.apply || throwTest262Error('[[Call]] trap called'),
                construct: overrides.construct || throwTest262Error('[[Construct]] trap called')
              };
            }

            var key = "a";
            var ownKeys = [key];
            var badProxyHandlers = allowProxyTraps({
              getOwnPropertyDescriptor: function() {},
              ownKeys: function() {
                return ownKeys;
              }
            });
            var proxy = new Proxy({}, badProxyHandlers);

            var keys = Reflect.ownKeys(proxy);
            var descriptor = Object.getOwnPropertyDescriptor(proxy, key);
            var descriptors = Object.getOwnPropertyDescriptors(proxy);

            keys !== ownKeys &&
            Array.isArray(keys) &&
            keys.length === ownKeys.length &&
            keys[0] === ownKeys[0] &&
            descriptor === undefined &&
            !(key in descriptors);
            """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPropertyKey_Coercion_Honors_Symbols_And_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var sym = Symbol();
            var obj = {};
            obj[sym] = 1;
            var log = [];
            var wrapper = {};
            wrapper[Symbol.toPrimitive] = function(hint) {
              log.push(hint);
              return sym;
            };
            var keyOrder = {
              get toString() {
                log.push("toString");
                throw new Error("boom");
              },
              get valueOf() {
                log.push("valueOf");
                throw new Error("wrong-order");
              }
            };
            var orderOk = false;
            try { Object.prototype.hasOwnProperty.call(null, keyOrder); } catch (e) { orderOk = e && e.message === "boom"; }
            Object.hasOwn(obj, wrapper) === true &&
            obj.propertyIsEnumerable(wrapper) === true &&
            orderOk &&
            log.join("|") === "toString|string|string";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPreventExtensions_Returns_Primitive_Argument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            Object.preventExtensions(undefined) === undefined &&
            Object.preventExtensions(0) === 0 &&
            Object.preventExtensions("x") === "x";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeIsPrototypeOf_Returns_False_For_Primitive_Argument_Before_This_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            Object.prototype.isPrototypeOf.call(undefined, undefined) === false &&
            Object.prototype.isPrototypeOf.call(undefined, Symbol("x")) === false &&
            Object.prototype.isPrototypeOf.call(undefined, 3.14) === false;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectFromEntries_Closes_Iterator_When_Key_Coercion_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            let closed = 0;
            let iteratorCalled = false;
            function DummyError() {}
            const iterable = {
              [Symbol.iterator]() {
                let advanced = false;
                iteratorCalled = true;
                return {
                  next() {
                    if (advanced) throw new Error("advanced twice");
                    advanced = true;
                    return {
                      done: false,
                      value: {
                        0: { toString() { throw new DummyError(); } }
                      }
                    };
                  },
                  return() { closed++; return {}; }
                };
              }
            };
            let threw = false;
            try { Object.fromEntries(iterable); } catch (e) { threw = e instanceof DummyError; }
            [iteratorCalled,threw, closed]
            """);

        Assert.That(result.IsObject, Is.True);
        var array = (JsArray)result.AsObject();
        Assert.That(array.Length, Is.EqualTo(3));
        Assert.That(array[0].IsBool, Is.True);
        Assert.That(array[0].IsTrue, Is.True);
        Assert.That(array[1].IsBool, Is.True);
        Assert.That(array[1].IsTrue, Is.True);
        Assert.That(array[2].Tag, Is.EqualTo(Tag.JsTagInt));
        Assert.That(array[2].NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void ObjectPrototypeToString_Uses_Overridden_SymbolToStringTag_For_Boxed_Primitives()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            Boolean.prototype[Symbol.toStringTag] = "test262";
            Number.prototype[Symbol.toStringTag] = "test262";
            String.prototype[Symbol.toStringTag] = "test262";
            Object.defineProperty(Symbol.prototype, Symbol.toStringTag, { value: "test262" });
            Object.prototype.toString.call(true) === "[object test262]" &&
            Object.prototype.toString.call(0) === "[object test262]" &&
            Object.prototype.toString.call("") === "[object test262]" &&
            Object.prototype.toString.call(Symbol.prototype) === "[object test262]";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeToString_Falls_Back_To_Object_For_NonString_BigInt_And_Symbol_Tags()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            delete Symbol.prototype[Symbol.toStringTag];
            Object.defineProperty(BigInt.prototype, Symbol.toStringTag, { value: 86 });
            Object.prototype.toString.call(BigInt(0)) === "[object Object]" &&
            Object.prototype.toString.call(Object(BigInt(0))) === "[object Object]" &&
            Object.prototype.toString.call(Symbol("desc")) === "[object Object]";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeIsPrototypeOf_Uses_Proxy_Prototype_Chain()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var proto = {};
            var target = Object.create(proto);
            var proxy = new Proxy(target, {});
            Object.prototype.isPrototypeOf.call(proto, proxy) === true;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectKeys_And_GetOwnPropertyNames_Are_Not_Constructors_And_Lengths_Are_Spec_Shaped()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var d = Object.getOwnPropertyDescriptor(Object, "getOwnPropertyDescriptor");
            var keysThrows = false;
            var namesThrows = false;
            try { new Object.keys({}); } catch (e) { keysThrows = e && e.name === "TypeError"; }
            try { new Object.getOwnPropertyNames({}); } catch (e) { namesThrows = e && e.name === "TypeError"; }
            keysThrows && namesThrows &&
            d.value.length === 2 && d.writable === true && d.enumerable === false && d.configurable === true;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void GlobalThis_And_RegExp_Descriptor_Shapes_Are_Spec_Aligned()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var globalDesc = Object.getOwnPropertyDescriptor(globalThis, "globalThis");
            var ctorProtoDesc = Object.getOwnPropertyDescriptor(RegExp, "prototype");
            var sourceDesc = Object.getOwnPropertyDescriptor(RegExp.prototype, "source");
            var globalFlagDesc = Object.getOwnPropertyDescriptor(RegExp.prototype, "global");
            var ignoreCaseDesc = Object.getOwnPropertyDescriptor(RegExp.prototype, "ignoreCase");
            var multilineDesc = Object.getOwnPropertyDescriptor(RegExp.prototype, "multiline");
            globalDesc.writable === true &&
            globalDesc.enumerable === false &&
            globalDesc.configurable === true &&
            ctorProtoDesc.value === RegExp.prototype &&
            ctorProtoDesc.writable === false &&
            ctorProtoDesc.enumerable === false &&
            ctorProtoDesc.configurable === false &&
            typeof sourceDesc.get === "function" &&
            sourceDesc.set === undefined &&
            sourceDesc.enumerable === false &&
            sourceDesc.configurable === true &&
            typeof globalFlagDesc.get === "function" &&
            globalFlagDesc.set === undefined &&
            globalFlagDesc.enumerable === false &&
            globalFlagDesc.configurable === true &&
            typeof ignoreCaseDesc.get === "function" &&
            ignoreCaseDesc.set === undefined &&
            ignoreCaseDesc.enumerable === false &&
            ignoreCaseDesc.configurable === true &&
            typeof multilineDesc.get === "function" &&
            multilineDesc.set === undefined &&
            multilineDesc.enumerable === false &&
            multilineDesc.configurable === true;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_SetPrototypeOf_ObjectPrototype_IsImmutable_And_FromEntries_Preserves_Evaluation_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var immutableOk = false;
            try {
              Object.setPrototypeOf(Object.prototype, Object.create(null));
            } catch (e) {
              immutableOk = e instanceof TypeError;
            }
            immutableOk = immutableOk && Reflect.setPrototypeOf(Object.prototype, Object.create(null)) === false;

            var effects = [];
            var iterable = {
              [Symbol.iterator]() {
                effects.push("get Symbol.iterator");
                var count = 0;
                return {
                  next() {
                    effects.push("next " + count);
                    if (count++ === 0) {
                      return {
                        done: false,
                        value: {
                          get 0() {
                            effects.push('access property "0"');
                            return { toString() { effects.push("toString key"); return "k"; } };
                          },
                          get 1() {
                            effects.push('access property "1"');
                            return "v";
                          }
                        }
                      };
                    }
                    return { done: true };
                  }
                };
              }
            };
            Object.fromEntries(iterable);

            immutableOk && effects.join("|") === 'get Symbol.iterator|next 0|access property "0"|access property "1"|toString key|next 1';
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Object_IsSealed_And_IsFrozen_Proxy_Path_Uses_Proxy_OwnKey_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var target = {};
            var sym = Symbol();
            target[sym] = 1;
            target.foo = 2;
            target[0] = 3;
            Object.seal(target);

            var sealedKeys = [];
            var frozenKeys = [];
            var sealedProxy = new Proxy(target, {
              getOwnPropertyDescriptor(t, key) {
                sealedKeys.push(key);
                return Reflect.getOwnPropertyDescriptor(t, key);
              }
            });
            var frozenProxy = new Proxy(target, {
              getOwnPropertyDescriptor(t, key) {
                frozenKeys.push(key);
                return Reflect.getOwnPropertyDescriptor(t, key);
              }
            });

            Object.isSealed(sealedProxy);
            Object.isFrozen(frozenProxy);

            sealedKeys.length === 3 &&
            sealedKeys[0] === "0" &&
            sealedKeys[1] === "foo" &&
            sealedKeys[2] === sym &&
            frozenKeys.length === 3 &&
            frozenKeys[0] === "0" &&
            frozenKeys[1] === "foo" &&
            frozenKeys[2] === sym;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeHasOwnProperty_Does_Not_Depend_On_Public_GetOwnPropertyDescriptor_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var saved = Object.getOwnPropertyDescriptor;
            var ok;
            delete Object.getOwnPropertyDescriptor;
            try {
              ok =
                Object.prototype.hasOwnProperty.call(Object, "defineProperty") === true &&
                Object.prototype.propertyIsEnumerable.call(Object, "defineProperty") === false;
            } finally {
              Object.getOwnPropertyDescriptor = saved;
            }
            ok;
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectGetOwnPropertyDescriptors_Preserves_Key_Order_After_Redefine()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var obj = {};
            var symA = Symbol("a");
            var symB = Symbol("b");
            obj[symA] = 1;
            obj[symB] = 2;
            Object.defineProperty(obj, symA, { configurable: false });
            var objKeys = Reflect.ownKeys(Object.getOwnPropertyDescriptors(obj));

            var re = /(?:)/g;
            re.a = 1;
            Object.defineProperty(re, "lastIndex", { value: 2 });
            var reKeys = Reflect.ownKeys(Object.getOwnPropertyDescriptors(re));

            objKeys.length === 2 &&
            objKeys[0] === symA &&
            objKeys[1] === symB &&
            reKeys.length === 2 &&
            reKeys[0] === "lastIndex" &&
            reKeys[1] === "a";
            """);

        Assert.That(result.IsTrue, Is.True);
    }
}
