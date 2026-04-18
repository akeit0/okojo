using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class MapFeatureTests
{
    [Test]
    public void Map_Global_Constructor_Is_Installed_And_Has_NodeLike_Global_Descriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const d = Object.getOwnPropertyDescriptor(globalThis, "Map");
            typeof Map === "function" &&
            d.writable === true &&
            d.enumerable === false &&
            d.configurable === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Called_Without_New_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let ok = false;
            try { Map(); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Constructs_From_Iterable_And_Uses_MapPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const m = new Map([[1, "a"], [2, "b"]]);
            Object.getPrototypeOf(m) === Map.prototype &&
            Object.getPrototypeOf(Map.prototype) === Object.prototype &&
            m.get(1) === "a" &&
            m.get(2) === "b" &&
            m.size === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Uses_SameValueZero_For_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const m = new Map([[NaN, "n"], [-0, "m"], [0, "z"]]);
            m.get(NaN) === "n" &&
            m.get(-0) === "z" &&
            m.get(0) === "z" &&
            m.size === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Set_Delete_Clear_And_Has_Basic_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const m = new Map([[1, "a"], [1, "b"]]);
            const setResult = m.set(2, "c");
            const hasAfterSet = m.has(2);
            const deleteResult = m.delete(2);
            const hasAfterDelete = m.has(2);
            const firstValue = m.get(1);
            m.clear();
            setResult === m &&
            firstValue === "b" &&
            hasAfterSet === true &&
            deleteResult === true &&
            hasAfterDelete === false &&
            m.size === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void MapGroupBy_Calls_Callback_With_Value_And_Index_And_Returns_Map()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const arr = [-0, 0, 1, 2, 3];
            let calls = 0;

            const grouped = Map.groupBy(arr, function (n, i) {
              calls++;
              if (n !== arr[i]) return "bad";
              if (arguments.length !== 2) return "bad";
              return null;
            });

            grouped instanceof Map &&
            calls === 5 &&
            grouped.size === 1 &&
            grouped.get(null).length === 5 &&
            grouped.get(null)[0] === -0 &&
            grouped.get(null)[4] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void MapPrototype_SymbolIterator_Aliases_Entries_And_Is_Not_Constructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const m = new Map([[1, "a"]]);
            let threw = false;
            try { new m[Symbol.iterator](); } catch (e) { threw = e && e.name === "TypeError"; }

            const iter = m.entries();
            const first = iter.next();
            const second = iter.next();

            Map.prototype[Symbol.iterator] === Map.prototype.entries &&
            threw &&
            iter[Symbol.iterator]() === iter &&
            first.done === false &&
            first.value[0] === 1 &&
            first.value[1] === "a" &&
            second.done === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void MapPrototype_ForEach_And_GetOrInsert_Methods_Are_Not_Constructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function isConstructor(f) {
              try {
                Reflect.construct(function(){}, [], f);
              } catch (e) {
                return false;
              }
              return true;
            }

            typeof Map.prototype.forEach === "function" &&
            typeof Map.prototype.getOrInsert === "function" &&
            typeof Map.prototype.getOrInsertComputed === "function" &&
            isConstructor(Map.prototype.forEach) === false &&
            isConstructor(Map.prototype.getOrInsert) === false &&
            isConstructor(Map.prototype.getOrInsertComputed) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void MapIterator_ToString_Falls_Back_To_Iterator_Brand_When_Own_Tag_Is_Removed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const map = new Map([[1, "a"]]);
            const it = map.keys();
            const proto = Object.getPrototypeOf(it);

            Object.prototype.toString.call(it) === "[object Map Iterator]" &&
            delete proto[Symbol.toStringTag] &&
            Object.prototype.toString.call(it) === "[object Iterator]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Constructor_Calls_Instance_Set_For_Each_Iterable_Entry()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const mapSet = Map.prototype.set;
            let counter = 0;
            Map.prototype.set = function(k, v) {
              counter++;
              return mapSet.call(this, k, v);
            };

            const map = new Map([["foo", 1], ["bar", 2]]);
            Map.prototype.set = mapSet;

            counter === 2 && map.get("foo") === 1 && map.get("bar") === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_Constructor_Throws_When_Getting_Set_Method_Fails()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            Object.defineProperty(Map.prototype, "set", {
              get: function() { throw new Error("boom"); }
            });

            let threw = false;
            try { new Map([]); } catch (e) { threw = e && e.message === "boom"; }
            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Map_SymbolSpecies_Returns_This_And_CrossRealm_NewTarget_Falls_Back_To_MapPrototype()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunction"] = JsValue.FromObject(otherRealm.FunctionConstructor);
        realm.Global["OtherMapPrototype"] = JsValue.FromObject(otherRealm.MapPrototype);

        var result = realm.Eval("""
                                var thisVal = {};
                                var accessor = Object.getOwnPropertyDescriptor(Map, Symbol.species).get;
                                var C = new OtherFunction();
                                C.prototype = null;
                                var map = Reflect.construct(Map, [], C);

                                accessor.call(thisVal) === thisVal &&
                                Object.getPrototypeOf(map) === OtherMapPrototype;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Map_Constructor_Closes_Iterator_On_Abrupt_Entry_Processing_And_Exhausted_Iterator_Stays_Done()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;

                                var count = 0;
                                var iterable = {};
                                iterable[Symbol.iterator] = function() {
                                  return {
                                    next: function() {
                                      return {
                                        value: 1,
                                        done: false
                                      };
                                    },
                                    return: function() {
                                      count += 1;
                                    }
                                  };
                                };
                                try {
                                  new Map(iterable);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.name === "TypeError";
                                }
                                ok = ok && count === 1;

                                count = 0;
                                iterable = {};
                                iterable[Symbol.iterator] = function() {
                                  return {
                                    next: function() {
                                      return {
                                        value: [],
                                        done: false
                                      };
                                    },
                                    return: function() {
                                      count += 1;
                                    }
                                  };
                                };
                                var mapSet = Map.prototype.set;
                                Map.prototype.set = function() {
                                  throw new Error("set-boom");
                                };
                                try {
                                  new Map(iterable);
                                  ok = false;
                                } catch (e) {
                                  ok = ok && e.message === "set-boom";
                                } finally {
                                  Map.prototype.set = mapSet;
                                }
                                ok = ok && count === 1;

                                var map = new Map();
                                map.set(1, 11);
                                var iterator = map[Symbol.iterator]();
                                iterator.next();
                                iterator.next();
                                var exhausted = iterator.next();
                                map.set(2, 22);
                                var repeated = iterator.next();

                                ok = ok &&
                                  exhausted.done === true &&
                                  exhausted.value === undefined &&
                                  repeated.done === true &&
                                  repeated.value === undefined;

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
