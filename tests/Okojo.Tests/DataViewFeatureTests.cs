using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DataViewFeatureTests
{
    [Test]
    public void DataView_Global_Constructor_Is_Installed_And_IsView_Sees_It()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const d = Object.getOwnPropertyDescriptor(globalThis, "DataView");
            const view = new DataView(new ArrayBuffer(4));

            typeof DataView === "function" &&
            d.writable === true &&
            d.enumerable === false &&
            d.configurable === true &&
            Object.getPrototypeOf(view) === DataView.prototype &&
            Object.prototype.toString.call(view) === "[object DataView]" &&
            ArrayBuffer.isView(view) === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Accessors_And_GetSet_Methods_Use_Requested_Endianness()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(16);
            const view = new DataView(buffer, 4, 8);
            view.setUint16(0, 0x1234, true);
            view.setUint32(2, 0x01020304, false);
            view.setFloat16(6, 1.5, true);

            view.buffer === buffer &&
            view.byteOffset === 4 &&
            view.byteLength === 8 &&
            view.getUint16(0, true) === 0x1234 &&
            view.getUint16(0, false) === 0x3412 &&
            view.getUint32(2, false) === 0x01020304 &&
            view.getUint32(2, true) === 0x04030201 &&
            view.getFloat16(6, true) === 1.5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Supports_BigInt_Accessors_And_RangeErrors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const view = new DataView(new ArrayBuffer(8));
            view.setBigUint64(0, 18446744073709551615n, true);

            let rangeError = false;
            try { view.getUint32(6, true); } catch (e) { rangeError = e && e.name === "RangeError"; }

            view.getBigUint64(0, true) === 18446744073709551615n &&
            rangeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Get_Throws_TypeError_For_Detached_Buffer_Before_Range_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer, 0);
            buffer.transfer();

            let typeError = false;
            try { view.getUint8(100); } catch (e) { typeError = e && e.name === "TypeError"; }
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Set_Converts_Value_Before_Final_Detached_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(8);
            const view = new DataView(buffer, 0);
            const poisoned = { valueOf() { throw new Error("poison"); } };
            buffer.transfer();

            let sawPoison = false;
            try { view.setUint16(0, poisoned); } catch (e) { sawPoison = e && e.message === "poison"; }
            sawPoison;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Set_Converts_Value_Before_Final_Range_Check()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const view = new DataView(new ArrayBuffer(8), 0);
            const poisoned = { valueOf() { throw new Error("poison"); } };

            let sawPoison = false;
            try { view.setFloat64(100, poisoned); } catch (e) { sawPoison = e && e.message === "poison"; }
            sawPoison;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Methods_Throw_TypeError_When_Fixed_View_Becomes_Out_Of_Bounds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(24, { maxByteLength: 32 });
            const view = new DataView(buffer, 0, 16);

            buffer.resize(8);

            let typeError = false;
            try { view.setUint32(0, 1); } catch (e) { typeError = e && e.name === "TypeError"; }
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Constructor_Length_And_Auto_Length_Tracking_Follow_Resizable_Buffer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(4, { maxByteLength: 5 });
            const view = new DataView(buffer, 1);
            buffer.resize(5);
            const grew = view.byteLength === 4;
            buffer.resize(3);
            const shrank = view.byteLength === 2;
            DataView.length === 1 && grew && shrank;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Getters_Throw_For_Detached_And_Out_Of_Bounds_Views()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const detachedBuffer = new ArrayBuffer(8);
            const detachedView = new DataView(detachedBuffer, 1, 2);
            detachedBuffer.transfer();

            let detachedByteOffsetTypeError = false;
            let detachedByteLengthTypeError = false;
            try { detachedView.byteOffset; } catch (e) { detachedByteOffsetTypeError = e && e.name === "TypeError"; }
            try { detachedView.byteLength; } catch (e) { detachedByteLengthTypeError = e && e.name === "TypeError"; }

            const rab = new ArrayBuffer(4, { maxByteLength: 5 });
            const fixedView = new DataView(rab, 1, 2);
            rab.resize(2);

            let outOfBoundsByteOffsetTypeError = false;
            let outOfBoundsByteLengthTypeError = false;
            try { fixedView.byteOffset; } catch (e) { outOfBoundsByteOffsetTypeError = e && e.name === "TypeError"; }
            try { fixedView.byteLength; } catch (e) { outOfBoundsByteLengthTypeError = e && e.name === "TypeError"; }

            detachedByteOffsetTypeError &&
            detachedByteLengthTypeError &&
            outOfBoundsByteOffsetTypeError &&
            outOfBoundsByteLengthTypeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DataView_Validates_ByteOffset_Before_NewTarget_Prototype_Access()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const newTarget = Object.defineProperty(function() {}.bind(), "prototype", {
              get() { throw new Error("prototype accessed"); }
            });

            let sawRangeError = false;
            try {
              Reflect.construct(DataView, [new ArrayBuffer(0), 10], newTarget);
            } catch (e) {
              sawRangeError = e && e.name === "RangeError";
            }

            sawRangeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
