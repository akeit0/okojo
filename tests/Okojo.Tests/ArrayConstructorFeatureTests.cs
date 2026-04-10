using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayConstructorFeatureTests
{
    [Test]
    public void ArrayFrom_Uses_Custom_Constructors_And_Closes_Iterator_On_Map_Throw()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        realm.Eval("""
                   var firstOk = true;
                   var thisVal, args;
                   var callCount = 0;
                   var C = function() {
                     thisVal = this;
                     args = arguments;
                     callCount += 1;
                   };
                   var items = {};
                   items[Symbol.iterator] = function() {
                     return {
                       next: function() {
                         return { done: true };
                       }
                     };
                   };

                   var result = Array.from.call(C, items);
                   firstOk = (result instanceof C) &&
                     (result.constructor === C) &&
                     (callCount === 1) &&
                     (thisVal === result) &&
                     (args.length === 0);

                   var closeCount = 0;
                   var mapFn = function() {
                     throw new Error("boom");
                   };
                   var iterItems = {};
                   iterItems[Symbol.iterator] = function() {
                     return {
                       return: function() {
                         closeCount += 1;
                       },
                       next: function() {
                         return {
                           done: false,
                           value: 1
                         };
                       }
                     };
                   };

                   try {
                     Array.from(iterItems, mapFn);
                     firstOk = false;
                   } catch (e) {
                     firstOk = firstOk && e.message === "boom";
                   }

                   globalThis.firstOk = firstOk;
                   globalThis.closeOk = closeCount === 1;

                   var ctorBoom = false;
                   var ThrowingCtor = function() {
                     throw new Error("ctor-boom");
                   };
                   var throwingItems = {};
                   throwingItems[Symbol.iterator] = function() {
                     throw new Error("iterator-boom");
                   };
                   try {
                     Array.from.call(ThrowingCtor, throwingItems);
                   } catch (e) {
                     ctorBoom = e.message === "ctor-boom";
                   }

                   globalThis.ctorOk = ctorBoom;
                   firstOk && closeCount === 1 && ctorBoom;
                   """);

        Assert.That(realm.Global["firstOk"].IsTrue, Is.True);
        Assert.That(realm.Global["closeOk"].IsTrue, Is.True);
        Assert.That(realm.Global["ctorOk"].IsTrue, Is.True);
    }

    [Test]
    public void ArrayFrom_Uses_Custom_Constructor_For_Iterables_And_Ctor_Realm_Prototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunction"] = JsValue.FromObject(otherRealm.FunctionConstructor);
        realm.Global["OtherObjectPrototype"] = JsValue.FromObject(otherRealm.ObjectPrototype);

        var result = realm.Eval("""
                                var ok = true;

                                ok = ok && (Array.from.call(Object, []).constructor === Object);

                                var C = new OtherFunction();
                                C.prototype = null;
                                var a = Array.from.call(C, []);
                                ok = ok && (Object.getPrototypeOf(a) === OtherObjectPrototype);

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayFrom_Sets_Result_Length_With_Ordinary_Set_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var poisonedPrototypeLength = function() {};
                                Object.defineProperty(poisonedPrototypeLength.prototype, 'length', {
                                  set: function(_) {
                                    throw new Error("length-boom");
                                  }
                                });

                                var items = {};
                                items[Symbol.iterator] = function() {
                                  return {
                                    next: function() {
                                      return {
                                        done: true
                                      };
                                    }
                                  };
                                };

                                try {
                                  Array.from.call(poisonedPrototypeLength, items);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "length-boom";
                                }

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLength_DefineAndSet_Coerce_Length_Twice_Before_Writable_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var array = [1, 2];
                                var valueOfCalls = 0;
                                var length = {
                                  valueOf: function() {
                                    valueOfCalls += 1;
                                    if (valueOfCalls !== 1) {
                                      Object.defineProperty(array, "length", { writable: false });
                                    }
                                    return array.length;
                                  }
                                };

                                try {
                                  Object.defineProperty(array, "length", { value: length, writable: true });
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }
                                ok = ok && valueOfCalls === 2;

                                array = [1, 2, 3];
                                var hints = [];
                                length = {};
                                length[Symbol.toPrimitive] = function(hint) {
                                  hints.push(hint);
                                  Object.defineProperty(array, "length", { writable: false });
                                  return 0;
                                };

                                try {
                                  (function() {
                                    "use strict";
                                    array.length = length;
                                  })();
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }

                                ok = ok && hints.join(",") === "number,number";
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayIsArray_Is_Not_Constructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                try {
                                  new Array.isArray([]);
                                } catch (e) {
                                  ok = e && e.name === "TypeError";
                                }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
