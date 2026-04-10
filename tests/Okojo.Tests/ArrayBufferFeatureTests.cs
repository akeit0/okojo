using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayBufferFeatureTests
{
    [Test]
    public void ArrayBuffer_Global_Constructor_Is_Installed_And_Has_NodeLike_Global_Descriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const d = Object.getOwnPropertyDescriptor(globalThis, "ArrayBuffer");
            typeof ArrayBuffer === "function" &&
            d.writable === true &&
            d.enumerable === false &&
            d.configurable === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Called_Without_New_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ok = false;
            try { ArrayBuffer(8); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Constructs_With_ByteLength_And_Brand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ab = new ArrayBuffer(8);
            Object.getPrototypeOf(ab) === ArrayBuffer.prototype &&
            ab.byteLength === 8 &&
            Object.prototype.toString.call(ab) === "[object ArrayBuffer]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Length_Constructor_Argument_Follows_NodeLike_Baseline()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               let negativeRangeError = false;
                               try { new ArrayBuffer(-1); } catch (e) { negativeRangeError = e && e.name === "RangeError"; }
                               negativeRangeError;
                               """).IsTrue, Is.True);
        Assert.That(realm.Eval("new ArrayBuffer().byteLength === 0;").IsTrue, Is.True);
        Assert.That(realm.Eval("new ArrayBuffer(undefined).byteLength === 0;").IsTrue, Is.True);
        Assert.That(realm.Eval("new ArrayBuffer(1.9).byteLength === 1;").IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_IsView_Distinguishes_TypedArray_From_Buffer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            ArrayBuffer.isView(new Uint8Array(2)) === true &&
            ArrayBuffer.isView(new ArrayBuffer(2)) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Can_Be_Constructed_Resizable_And_Resized()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(3, { maxByteLength: 5 });
            const ta = new Uint8Array(rab);
            ta[0] = 1;
            ta[1] = 2;
            ta[2] = 3;
            rab.resize(1);
            rab.byteLength === 1 &&
            ta.length === 1 &&
            ta[0] === 1 &&
            ta[1] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Resizable_And_MaxByteLength_Accessors_Are_Exposed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const fixed = new ArrayBuffer(3);
            const resizable = new ArrayBuffer(3, { maxByteLength: 5 });
            fixed.resizable === false &&
            fixed.maxByteLength === 3 &&
            resizable.resizable === true &&
            resizable.maxByteLength === 5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Constructor_Uses_NewTarget_Prototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            class MyArrayBuffer extends ArrayBuffer {}
            const derived = new MyArrayBuffer(4);
            const viaReflect = Reflect.construct(ArrayBuffer, [8], Object);
            Object.getPrototypeOf(derived) === MyArrayBuffer.prototype &&
            Object.getPrototypeOf(viaReflect) === Object.prototype;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Detached_Species_Slice_And_Transfer_Are_Exposed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            class MyArrayBuffer extends ArrayBuffer {}
            const source = new MyArrayBuffer(4);
            new Uint8Array(source).set([1, 2, 3, 4]);

            const sliced = source.slice(1, 3);
            const moved = source.transferToFixedLength();

            ArrayBuffer[Symbol.species] === ArrayBuffer &&
            sliced instanceof MyArrayBuffer &&
            sliced.byteLength === 2 &&
            new Uint8Array(sliced)[0] === 2 &&
            new Uint8Array(sliced)[1] === 3 &&
            source.detached === true &&
            source.byteLength === 0 &&
            moved.detached === false &&
            moved.resizable === false &&
            moved.maxByteLength === 4 &&
            moved.byteLength === 4 &&
            new Uint8Array(moved)[0] === 1 &&
            new Uint8Array(moved)[3] === 4;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Transfer_Preserves_Resizable_Source_Shape()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const source = new ArrayBuffer(4, { maxByteLength: 8 });
            new Uint8Array(source).set([9, 8, 7, 6]);
            const moved = source.transfer();
            source.detached === true &&
            moved.resizable === true &&
            moved.maxByteLength === 8 &&
            moved.byteLength === 4 &&
            new Uint8Array(moved)[0] === 9 &&
            new Uint8Array(moved)[3] === 6;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_MaxByteLength_RangeError_Happens_Before_NewTarget_Prototype_Lookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function Test262Error() {}
            let getterCalled = false;
            let newTarget = Object.defineProperty(function(){}.bind(null), "prototype", {
              get() {
                getterCalled = true;
                throw new Test262Error();
              }
            });

            let ok = false;
            try {
              Reflect.construct(ArrayBuffer, [10, { maxByteLength: 0 }], newTarget);
            } catch (e) {
              ok = e instanceof RangeError && getterCalled === false;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_DataAllocation_Happens_After_NewTarget_Prototype_Lookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function DummyError() {}

            var newTarget = function() {}.bind(null);
            Object.defineProperty(newTarget, "prototype", {
              get: function() {
                throw new DummyError();
              }
            });

            var ok = false;
            try {
              Reflect.construct(ArrayBuffer, [7 * 1125899906842624], newTarget);
            } catch (e) {
              ok = e instanceof DummyError;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_Prototype_Methods_Reject_SharedArrayBuffer_Receivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = true;
                                var sab = new SharedArrayBuffer(4);
                                var byteLength = Object.getOwnPropertyDescriptor(ArrayBuffer.prototype, "byteLength");
                                var maxByteLength = Object.getOwnPropertyDescriptor(ArrayBuffer.prototype, "maxByteLength");
                                var resizable = Object.getOwnPropertyDescriptor(ArrayBuffer.prototype, "resizable");
                                var detached = Object.getOwnPropertyDescriptor(ArrayBuffer.prototype, "detached");

                                function expectTypeError(fn) {
                                  try {
                                    fn();
                                    return false;
                                  } catch (e) {
                                    return e && e.name === "TypeError";
                                  }
                                }

                                ok = ok && expectTypeError(function() { byteLength.get.call(sab); });
                                ok = ok && expectTypeError(function() { maxByteLength.get.call(sab); });
                                ok = ok && expectTypeError(function() { resizable.get.call(sab); });
                                ok = ok && expectTypeError(function() { detached.get.call(sab); });
                                ok = ok && expectTypeError(function() { ArrayBuffer.prototype.slice.call(sab, 0); });

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayBuffer_TransferToImmutable_Returns_ArrayBuffer_With_Normal_Methods()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ab = (new ArrayBuffer(4)).transferToImmutable();
            typeof ab === "object" &&
            Object.getPrototypeOf(ab) === ArrayBuffer.prototype &&
            typeof ab.resize === "function" &&
            typeof ab.transfer === "function" &&
            typeof ab.transferToFixedLength === "function";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
