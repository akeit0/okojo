using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SetFeatureTests
{
    [Test]
    public void Set_Global_Constructor_And_Species_Are_Installed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const d = Object.getOwnPropertyDescriptor(globalThis, "Set");
            const sd = Object.getOwnPropertyDescriptor(Set, Symbol.species);
            typeof Set === "function" &&
            d.writable === true &&
            d.enumerable === false &&
            d.configurable === true &&
            typeof sd.get === "function" &&
            sd.enumerable === false &&
            sd.configurable === true &&
            Set[Symbol.species] === Set;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Call_Without_New_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ok = false;
            try { Set(); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Constructs_And_Uses_SameValueZero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const s = new Set([1, 2, 2, NaN, -0, 0]);
            Object.getPrototypeOf(s) === Set.prototype &&
            s.size === 4 &&
            s.has(2) &&
            s.has(NaN) &&
            s.has(-0) &&
            s.has(0);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Add_Delete_Clear_And_Has_Basic_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const s = new Set([1, 2]);
            const addResult = s.add(3);
            const hasAfterAdd = s.has(3);
            const deleteResult = s.delete(2);
            const hasAfterDelete = s.has(2);
            s.clear();
            addResult === s &&
            hasAfterAdd === true &&
            deleteResult === true &&
            hasAfterDelete === false &&
            s.size === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Iterator_Methods_Alias_Values_And_Preserve_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const s = new Set([3, 1, 2]);
            const v = s.values();
            const k = s.keys();
            const e = s.entries();
            const firstValue = v.next();
            const firstKey = k.next();
            const firstEntry = e.next();

            Set.prototype[Symbol.iterator] === Set.prototype.values &&
            Set.prototype.keys === Set.prototype.values &&
            v[Symbol.iterator]() === v &&
            firstValue.value === 3 &&
            firstKey.value === 3 &&
            firstEntry.value[0] === 3 &&
            firstEntry.value[1] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_ForOf_Preserves_Undefined_Distinct_From_Null()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const s = new Set();
            const obj = {};
            s.add(0);
            s.add('a');
            s.add(true);
            s.add(false);
            s.add(null);
            s.add(undefined);
            s.add(NaN);
            s.add(obj);
            const seen = [];
            for (var value of s) {
              seen.push(value === null ? "null" : Number.isNaN(value) ? "number:NaN" : value === obj ? "object" : typeof value + ":" + String(value));
            }
            seen.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("number:0|string:a|boolean:true|boolean:false|null|undefined:undefined|number:NaN|object"));
    }

    [Test]
    public void Set_ForEach_Uses_Value_Value_Set_Callback_Shape()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const s = new Set(["a", "b"]);
            let log = "";
            s.forEach(function (value, key, set) {
              log += value + ":" + key + ":" + (set === s) + "|";
            });
            log === "a:a:true|b:b:true|";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Operation_Methods_Return_Correct_Results()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = new Set([1, 2, 3]);
            const b = new Set([2, 4]);
            const diff = a.difference(b);
            const inter = a.intersection(b);
            const sym = a.symmetricDifference(b);
            const uni = a.union(b);

            Array.from(diff.values()).join(",") === "1,3" &&
            Array.from(inter.values()).join(",") === "2" &&
            Array.from(sym.values()).join(",") === "1,3,4" &&
            Array.from(uni.values()).join(",") === "1,2,3,4";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Relation_Methods_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = new Set([1, 2]);
            const b = new Set([1, 2, 3]);
            const c = new Set([4, 5]);
            a.isSubsetOf(b) &&
            b.isSupersetOf(a) &&
            a.isDisjointFrom(c) &&
            a.isDisjointFrom(b) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Constructor_Calls_Instance_Add_For_Each_Iterable_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const setAdd = Set.prototype.add;
            let counter = 0;
            Set.prototype.add = function(value) {
              counter++;
              return setAdd.call(this, value);
            };

            const set = new Set([1, 2]);
            Set.prototype.add = setAdd;

            counter === 2 && set.has(1) && set.has(2);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Set_Union_Uses_SetRecord_Order_And_Keys_Not_Has()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let observedOrder = [];

                                function observableIterator() {
                                  let values = ["a", "b", "c"];
                                  let index = 0;
                                  return {
                                    get next() {
                                      observedOrder.push("getting next");
                                      return function () {
                                        observedOrder.push("calling next");
                                        return {
                                          get done() {
                                            observedOrder.push("getting done");
                                            return index >= values.length;
                                          },
                                          get value() {
                                            observedOrder.push("getting value");
                                            return values[index++];
                                          },
                                        };
                                      };
                                    },
                                  };
                                }

                                class MySetLike {
                                  get size() {
                                    observedOrder.push("getting size");
                                    return {
                                      valueOf() {
                                        observedOrder.push("ToNumber(size)");
                                        return 2;
                                      }
                                    };
                                  }
                                  get has() {
                                    observedOrder.push("getting has");
                                    return function () {
                                      throw new Error("union should not call has");
                                    };
                                  }
                                  get keys() {
                                    observedOrder.push("getting keys");
                                    return function () {
                                      observedOrder.push("calling keys");
                                      return observableIterator();
                                    };
                                  }
                                }

                                const combined = new Set(["a", "d"]).union(new MySetLike());
                                Array.from(combined).join(",") === "a,d,b,c" &&
                                observedOrder.join("|") === "getting size|ToNumber(size)|getting has|getting keys|calling keys|getting next|calling next|getting done|getting value|calling next|getting done|getting value|calling next|getting done|getting value|calling next|getting done";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_SymmetricDifference_And_Difference_Do_Not_Use_Other_Has_When_Keys_Suffice()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const baseSet = new Set(["a", "b", "c", "d", "e"]);
                                let symOk = false;

                                const sym = new Set([1, 2]).symmetricDifference({
                                  size: 2,
                                  has() { throw new Error("symmetricDifference should not call has"); },
                                  keys: function* () { yield 2; yield 3; }
                                });
                                symOk = Array.from(sym).join(",") === "1,3";

                                const other = {
                                  size: 3,
                                  get has() {
                                    baseSet.add("q");
                                    return function () {
                                      throw new Error("difference should not call has");
                                    };
                                  },
                                  keys() {
                                    let index = 0;
                                    let values = ["x", "b", "b"];
                                    return {
                                      next() {
                                        if (index === 0) {
                                          baseSet.delete("b");
                                          baseSet.delete("c");
                                          baseSet.add("b");
                                          baseSet.add("d");
                                        }
                                        return { done: index >= values.length, value: values[index++] };
                                      }
                                    };
                                  }
                                };

                                const diff = baseSet.difference(other);
                                symOk &&
                                Array.from(diff).join(",") === "a,c,d,e,q" &&
                                Array.from(baseSet).join(",") === "a,d,e,q,b";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_GetSetRecord_Rejects_Invalid_Size_And_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const s = new Set([1, 2]);
                                let sizeTypeError = false;
                                let keysTypeError = false;
                                try { s.isDisjointFrom({ size: undefined, has() {}, keys() { return [][Symbol.iterator](); } }); } catch (e) { sizeTypeError = e && e.name === "TypeError"; }
                                try { s.union({ size: 0, has() {}, keys: undefined }); } catch (e) { keysTypeError = e && e.name === "TypeError"; }
                                sizeTypeError && keysTypeError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_Iterator_Remains_Done_After_Exhaustion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const set = new Set([1, 2]);
                                const iterator = set.values();
                                iterator.next();
                                set.add(3);
                                iterator.next();
                                iterator.next();
                                const exhausted = iterator.next();
                                set.add(4);
                                const repeated = iterator.next();
                                exhausted.done === true &&
                                exhausted.value === undefined &&
                                repeated.done === true &&
                                repeated.value === undefined;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_Difference_Uses_Has_When_Receiver_Not_Larger()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const diff = new Set([1, 2]).difference({
                                  size: 2,
                                  has(v) { return v === 2; },
                                  keys() { throw new Error("difference should not read keys when this.size <= other.size"); }
                                });
                                Array.from(diff).join(",") === "1";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_SymmetricDifference_Uses_Receiver_Membership_For_SetLike_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const baseSet = new Set(["a", "b", "c", "d", "e"]);
                                const combined = baseSet.symmetricDifference({
                                  size: 4,
                                  get has() {
                                    baseSet.add("q");
                                    return function () { throw new Error("should not call has"); };
                                  },
                                  keys() {
                                    let index = 0;
                                    const values = ["x", "b", "c", "c"];
                                    return {
                                      next() {
                                        if (index === 0) {
                                          baseSet.delete("b");
                                          baseSet.delete("c");
                                          baseSet.add("b");
                                          baseSet.add("d");
                                        }
                                        return { done: index >= values.length, value: values[index++] };
                                      }
                                    };
                                  }
                                });
                                Array.from(combined).join(",") === "a,c,d,e,q,x";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_Canonicalizes_Negative_Zero_In_Stored_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const union = new Set([1]).union({
                                  size: 1,
                                  has() { throw new Error("union should not call has"); },
                                  keys() { return [-0].values(); }
                                });
                                const intersection = new Set([-0]).intersection({
                                  size: 1,
                                  has() { return true; },
                                  keys() { throw new Error("intersection should not call keys when this.size <= other.size"); }
                                });
                                Object.is(Array.from(union)[1], 0) &&
                                Object.is(Array.from(intersection)[0], 0);
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_Constructor_Closes_Iterator_When_Add_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let closed = 0;
                                const iterable = {
                                  [Symbol.iterator]() {
                                    return {
                                      next() { return { value: 1, done: false }; },
                                      return() { closed++; return {}; }
                                    };
                                  }
                                };
                                const originalAdd = Set.prototype.add;
                                Set.prototype.add = function () { throw new Error("boom"); };
                                try { new Set(iterable); } catch {}
                                Set.prototype.add = originalAdd;
                                closed === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Set_Construct_Uses_NewTarget_Realm_Prototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunction"] = JsValue.FromObject(otherRealm.FunctionConstructor);
        realm.Global["OtherSetPrototype"] = JsValue.FromObject(otherRealm.SetPrototype);

        var result = realm.Eval("""
                                const C = new OtherFunction();
                                C.prototype = null;
                                const instance = Reflect.construct(Set, [], C);
                                Object.getPrototypeOf(instance) === OtherSetPrototype;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
