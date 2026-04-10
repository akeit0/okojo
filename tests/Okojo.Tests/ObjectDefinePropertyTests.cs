using Okojo.Runtime;

namespace Okojo.Tests;

public class ObjectDefinePropertyTests
{
    [Test]
    public void ObjectPrototype_PropertyIsEnumerable_ArrayIndexKey_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var a = [];
                                Object.defineProperty(a, "0", { value: 1, enumerable: true, writable: true, configurable: true });
                                Object.prototype.propertyIsEnumerable.call(a, "0") === true &&
                                Object.prototype.propertyIsEnumerable.call(a, 0) === true &&
                                Object.prototype.propertyIsEnumerable.call(a, { toString: function(){ return "0"; } }) === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperty_ObjectKeyToIndex_UsesElementDescriptorPath()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var a = [];
                                var key = { toString: function() { return "1"; } };
                                Object.defineProperty(a, key, { value: 9, enumerable: false, writable: true, configurable: true });
                                var d = Object.getOwnPropertyDescriptor(a, key);
                                a[1] === 9 && d.value === 9 && d.enumerable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperty_DataDescriptor_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = {};
                                Object.defineProperty(o, "x", {
                                  value: 7,
                                  writable: false,
                                  enumerable: false,
                                  configurable: false
                                });
                                o.x = 9;
                                delete o.x;
                                var d = Object.getOwnPropertyDescriptor(o, "x");
                                d.value === 7 &&
                                d.writable === false &&
                                d.enumerable === false &&
                                d.configurable === false &&
                                o.x === 7;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperty_AccessorDescriptor_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var state = 0;
                                var o = {};
                                Object.defineProperty(o, "x", {
                                  get: function() { return state; },
                                  set: function(v) { state = v; },
                                  enumerable: true,
                                  configurable: true
                                });
                                o.x = 5;
                                var d = Object.getOwnPropertyDescriptor(o, "x");
                                o.x === 5 &&
                                typeof d.get === "function" &&
                                typeof d.set === "function" &&
                                d.enumerable === true &&
                                d.configurable === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_BasicDescriptors_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = {};
                                Object.defineProperties(o, {
                                  a: { value: 1, enumerable: true },
                                  b: { value: 2, writable: false }
                                });
                                var da = Object.getOwnPropertyDescriptor(o, "a");
                                var db = Object.getOwnPropertyDescriptor(o, "b");
                                Object.keys(o).join(",") === "a" &&
                                da.value === 1 && da.enumerable === true &&
                                db.value === 2 && db.writable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_NonConfigurableProperty_CannotBecomeConfigurable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var o = {};
                                Object.defineProperty(o, "prop", { value: 11, configurable: false });
                                var threw = false;
                                try {
                                  Object.defineProperties(o, { prop: { value: 12, configurable: true } });
                                } catch (e) {
                                  threw = e && e.name === "TypeError";
                                }
                                threw;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_RepeatedGetterMaterialization_On_Different_Receivers_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const styles = {};

                                styles.green = {
                                  get() {
                                    const builder = () => 'ok';
                                    Object.setPrototypeOf(builder, proto);
                                    Object.defineProperty(this, 'green', { value: builder });
                                    return builder;
                                  }
                                };

                                const proto = Object.defineProperties(() => {}, { ...styles });

                                function create() {
                                  const chalk = () => 'base';
                                  Object.setPrototypeOf(chalk, proto);
                                  return chalk;
                                }

                                const left = create();
                                const right = create();
                                `${typeof left.green}|${typeof right.green}`;
                                """);

        Assert.That(result.IsString, Is.True);
        Assert.That(result.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void ObjectDefineProperties_InlineTemplateTypeof_After_PrecomputedTypeof_StaysFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const styles = {};

                                styles.green = {
                                  get() {
                                    const builder = () => 'ok';
                                    Object.setPrototypeOf(builder, proto);
                                    Object.defineProperty(this, 'green', { value: builder });
                                    return builder;
                                  }
                                };

                                const proto = Object.defineProperties(() => {}, { ...styles });

                                function create() {
                                  const chalk = () => 'base';
                                  Object.setPrototypeOf(chalk, proto);
                                  return chalk;
                                }

                                const left = create();
                                const first = typeof left.green;
                                const second = typeof left.green;
                                `${first}|${second}|${typeof left.green}`;
                                """);

        Assert.That(result.IsString, Is.True);
        Assert.That(result.AsString(), Is.EqualTo("function|function|function"));
    }

    [Test]
    public void ObjectDefineProperty_ConvertsDataToAccessor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var state = 1;
                                var o = { x: 3 };
                                Object.defineProperty(o, "x", {
                                  get: function() { return state; },
                                  set: function(v) { state = v; },
                                  enumerable: true,
                                  configurable: true
                                });
                                o.x = 9;
                                var d = Object.getOwnPropertyDescriptor(o, "x");
                                o.x === 9 &&
                                typeof d.get === "function" &&
                                typeof d.set === "function" &&
                                d.writable === undefined &&
                                d.enumerable === true;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperty_ConvertsAccessorToData()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var state = 5;
                                var o = {
                                  get x() { return state; },
                                  set x(v) { state = v; }
                                };
                                Object.defineProperty(o, "x", {
                                  value: 42,
                                  writable: true,
                                  enumerable: false,
                                  configurable: true
                                });
                                o.x = 7;
                                var d = Object.getOwnPropertyDescriptor(o, "x");
                                o.x === 7 &&
                                d.value === 7 &&
                                d.writable === true &&
                                d.get === undefined &&
                                d.set === undefined;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_ShrinkBlockedByNonConfigurableElement()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, 1];
                                Object.defineProperty(arr, "1", { value: 1, configurable: false });
                                var threw = false;
                                try { Object.defineProperties(arr, { length: { value: 1 } }); } catch (e) { threw = e && e.name === "TypeError"; }
                                var d = Object.getOwnPropertyDescriptor(arr, "length");
                                threw && d.value === 2 && d.writable === true && d.enumerable === false && d.configurable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_DescriptorWithoutValue_PreservesValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [];
                                Object.defineProperties(arr, {
                                  length: {
                                    writable: true,
                                    enumerable: false,
                                    configurable: false
                                  }
                                });
                                arr.length = 2;
                                var d = Object.getOwnPropertyDescriptor(arr, "length");
                                d.value === 2 && d.writable === true && d.enumerable === false && d.configurable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_StringExponential_IsAccepted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [];
                                Object.defineProperties(arr, { length: { value: "2E3" } });
                                arr.length === 2000;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_NonPrimitiveObjectValue_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var toStringAccessed = false;
                                var valueOfAccessed = false;
                                var threwType = false;
                                try {
                                  Object.defineProperties([], {
                                    length: {
                                      value: {
                                        toString: function(){ toStringAccessed = true; return {}; },
                                        valueOf: function(){ valueOfAccessed = true; return {}; }
                                      }
                                    }
                                  });
                                } catch (e) {
                                  threwType = e && e.name === "TypeError";
                                }
                                threwType && toStringAccessed && valueOfAccessed;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_SameValue_DoesNotMaterializeElision()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, , 2];
                                Object.defineProperties(arr, { length: { value: 3 } });
                                arr.length === 3 &&
                                arr[0] === 0 &&
                                arr.hasOwnProperty("1") === false &&
                                arr[2] === 2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_ShrinkStopsAtFirstNonConfigurable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, 1, 2];
                                Object.defineProperty(arr, "1", { configurable: false });
                                Object.defineProperty(arr, "2", { configurable: true });
                                var threw = false;
                                try { Object.defineProperties(arr, { length: { value: 1 } }); } catch (e) { threw = e && e.name === "TypeError"; }
                                threw && arr.length === 2 && arr.hasOwnProperty("2") === false && arr[1] === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_ShrinkBlockedByNonConfigurable_KeepsElement()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, 1];
                                Object.defineProperty(arr, "1", { configurable: false });
                                var threw = false;
                                try { Object.defineProperties(arr, { length: { value: 1 } }); } catch (e) { threw = e && e.name === "TypeError"; }
                                threw && arr.length === 2 && arr.hasOwnProperty("1") && arr[1] === 1;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_ShrinkWithWritableFalse_MakesLengthReadOnly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, 1];
                                Object.defineProperties(arr, { length: { value: 1, writable: false } });
                                var d = Object.getOwnPropertyDescriptor(arr, "length");
                                var before = arr.length;
                                arr.length = 5;
                                d.writable === false && before === 1 && arr.length === 1;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperties_ArrayLength_ShrinkFailureWithWritableFalse_StillMakesReadOnly()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var arr = [0, 1, 2];
                                Object.defineProperty(arr, "1", { configurable: false });
                                var threw = false;
                                try {
                                  Object.defineProperties(arr, { length: { value: 0, writable: false } });
                                } catch (e) {
                                  threw = e && e.name === "TypeError";
                                }
                                var d = Object.getOwnPropertyDescriptor(arr, "length");
                                var before = arr.length;
                                arr.length = 7;
                                threw && d.writable === false && before === 2 && arr.length === 2;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectDefineProperty_LargeNumberKey_UsesJsNumberToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var obj = {};
                                Object.defineProperty(obj, 100000000000000000000, {});
                                obj.hasOwnProperty("100000000000000000000");
                                """);
        Assert.That(result.IsTrue, Is.True);
    }
}
