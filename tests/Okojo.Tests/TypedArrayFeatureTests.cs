using System.Text;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

[Parallelizable(ParallelScope.All)]
public class TypedArrayFeatureTests
{
    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Test]
    public void Uint8Array_Global_Constructor_Is_Installed_And_Has_NodeLike_Global_Descriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const d = Object.getOwnPropertyDescriptor(globalThis, "Uint8Array");
            typeof Uint8Array === "function" &&
            d.writable === true &&
            d.enumerable === false &&
            d.configurable === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Called_Without_New_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let ok = false;
            try { Uint8Array(4); } catch (e) { ok = e && e.name === "TypeError"; }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Constructs_With_TypedArray_Prototype_Chain_And_Brand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array(4);
            Object.getPrototypeOf(ta) === Uint8Array.prototype &&
            Object.getPrototypeOf(Uint8Array.prototype) !== Object.prototype &&
            ta.length === 4 &&
            ta.byteLength === 4 &&
            Uint8Array.BYTES_PER_ELEMENT === 1 &&
            Uint8Array.prototype.BYTES_PER_ELEMENT === 1 &&
            Object.prototype.toString.call(ta) === "[object Uint8Array]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Indexed_Read_Write_Uses_Uint8_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array(3);
            ta[0] = 257;
            ta[1] = -1;
            ta[3] = 99;

            ta[0] === 1 &&
            ta[1] === 255 &&
            ta[2] === 0 &&
            ta[3] === undefined &&
            Object.getOwnPropertyDescriptor(ta, "0").writable === true &&
            Object.getOwnPropertyDescriptor(ta, "0").enumerable === true &&
            delete ta[0] === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Length_Constructor_Argument_Follows_Narrow_Phase1_Rules()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(realm.Eval("""
                               let negativeRangeError = false;
                               try { new Uint8Array(-1); } catch (e) { negativeRangeError = e && e.name === "RangeError"; }
                               negativeRangeError;
                               """).IsTrue, Is.True);
        Assert.That(realm.Eval("new Uint8Array().length === 0;").IsTrue, Is.True);
        Assert.That(realm.Eval("new Uint8Array(undefined).length === 0;").IsTrue, Is.True);
        Assert.That(realm.Eval("new Uint8Array(1.9).length === 1;").IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Can_View_ArrayBuffer_With_ByteOffset_And_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(4);
            const view = new Uint8Array(buffer, 1, 2);
            view[0] = 255;
            view[1] = 7;
            const raw = new Uint8Array(buffer);

            view.buffer === buffer &&
            view.byteOffset === 1 &&
            view.length === 2 &&
            view.byteLength === 2 &&
            raw[1] === 255 &&
            raw[2] === 7;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Buffer_View_Omitted_Length_Uses_Remainder_And_Invalid_Offset_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.That(realm.Eval("""
                               const buffer = new ArrayBuffer(8);
                               const view = new Uint8Array(buffer, 3);
                               view.length === 5 && view.byteOffset === 3 && view.byteLength === 5;
                               """).IsTrue, Is.True);

        Assert.That(realm.Eval("""
                               let rangeError = false;
                               try { new Uint8Array(new ArrayBuffer(4), 10); } catch (e) { rangeError = e && e.name === "RangeError"; }
                               rangeError;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Can_Copy_From_TypedArray_And_ArrayLike_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const source = new Uint8Array([1, 257, -1]);
            const copy = new Uint8Array(source);
            const arrayLike = new Uint8Array({ length: 2, 0: 7, 1: 9 });

            copy !== source &&
            copy.length === 3 &&
            copy[0] === 1 &&
            copy[1] === 1 &&
            copy[2] === 255 &&
            arrayLike.length === 2 &&
            arrayLike[0] === 7 &&
            arrayLike[1] === 9;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Can_Construct_From_Iterable_And_Empty_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const fromSet = new Uint8Array(new Set([4, 5]));
            const empty = new Uint8Array({});

            fromSet.length === 2 &&
            fromSet[0] === 4 &&
            fromSet[1] === 5 &&
            empty.length === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_From_And_Of_Use_Uint8_Conversion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const from = Uint8Array.from([1, 257, -1], x => x);
            const ofResult = Uint8Array.of(1, 257, -1);

            from.length === 3 &&
            from[0] === 1 &&
            from[1] === 1 &&
            from[2] === 255 &&
            ofResult.length === 3 &&
            ofResult[0] === 1 &&
            ofResult[1] === 1 &&
            ofResult[2] === 255;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_From_Rejects_NonCallable_MapFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(realm.Eval("""
                               let typeError = false;
                               try { Uint8Array.from([1], 1); } catch (e) { typeError = e && e.name === "TypeError"; }
                               typeError;
                               """).IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Iterator_Methods_Alias_Values_And_Preserve_Index_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([7, 8]);
            const values = ta.values();
            const keys = ta.keys();
            const entries = ta.entries();
            const valueStep = values.next();
            const keyStep = keys.next();
            const entryStep = entries.next();
            const doneStep = values.next().value === 8 && values.next().done === true;

            Uint8Array.prototype[Symbol.iterator] === Uint8Array.prototype.values &&
            values[Symbol.iterator]() === values &&
            valueStep.done === false &&
            valueStep.value === 7 &&
            keyStep.value === 0 &&
            entryStep.value[0] === 0 &&
            entryStep.value[1] === 7 &&
            doneStep;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Symbol_Keyed_Properties_Do_Not_Overwrite_Indexed_Elements()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([7, 8]);
            const s = Symbol("1");
            ta[s] = 43;
            const values = [];
            ta.forEach(v => values.push(v));
            ta[s] === 43 && values.length === 2 && values[0] === 7 && values[1] === 8;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Reverse_Preserves_Symbol_Keyed_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([7, 8]);
            const s = Symbol("1");
            ta[s] = 1;
            ta.reverse();
            ta[0] === 8 && ta[1] === 7 && ta[s] === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_From_Propagates_Source_Access_Errors_Before_Abstract_Construction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const marker = {};
            const TA = Object.getPrototypeOf(Int8Array);
            const source = {};
            Object.defineProperty(source, Symbol.iterator, {
              get() {
                throw marker;
              }
            });

            try {
              TA.from(source);
              false;
            } catch (e) {
              e === marker;
            }
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Buffer_Argument_Throws_TypeError_When_Detached_During_Offset_Or_Length_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262DetachBufferHarness(realm);
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let offsetTypeError = false;
            let lengthTypeError = false;

            {
              const buffer = new ArrayBuffer(24);
              const byteOffset = { valueOf() { $262.detachArrayBuffer(buffer); return 8; } };
              try { new Float64Array(buffer, byteOffset); } catch (e) { offsetTypeError = e && e.name === "TypeError"; }
            }

            {
              const buffer = new ArrayBuffer(24);
              const length = { valueOf() { $262.detachArrayBuffer(buffer); return 1; } };
              try { new Float64Array(buffer, 0, length); } catch (e) { lengthTypeError = e && e.name === "TypeError"; }
            }

            offsetTypeError && lengthTypeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Constructor_Prefers_Iterator_For_Array_Input()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const values = [0, {
              valueOf() {
                values.length = 0;
                return 100;
              }
            }, 2];

            const ta = new Float64Array(values);
            ta.length === 3 && ta[0] === 0 && ta[1] === 100 && ta[2] === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Constructor_Throws_For_OutOfBounds_Resizable_TypedArray_Source()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(8, { maxByteLength: 16 });
            const source = new Int16Array(rab, 0, 4);
            rab.resize(4);

            try {
              new Int16Array(source);
              false;
            } catch (e) {
              e && e.name === "TypeError";
            }
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Iterator_Has_TypedArrayIterator_Brand_And_Falls_Back_To_Iterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const it = new Uint8Array([1]).values();
            const proto = Object.getPrototypeOf(it);

            Object.prototype.toString.call(it) === "[object Array Iterator]" &&
            delete proto[Symbol.toStringTag] &&
            Object.prototype.toString.call(it) === "[object Iterator]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ReflectSet_Valid_Index_With_Altered_Receiver_Uses_Receiver_Own_Descriptor_Rules()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var valueOfCalls = 0;
            var value = {
              valueOf: function() {
                ++valueOfCalls;
                return 2.3;
              },
            };

            Object.defineProperty(Uint8Array.prototype, 0, {
              get: function() { throw new Error("0 getter should be unreachable!"); },
              set: function(_v) { throw new Error("0 setter should be unreachable!"); },
              configurable: true,
            });

            var target = new Uint8Array([0]);
            var receiver = {};
            var emptyOk = Reflect.set(target, 0, value, receiver);
            var targetUnchanged = target[0] === 0;
            var receiverCreated = receiver[0] === value;

            target = new Uint8Array([0]);
            receiver = {
              get 0() { return 1; },
              set 0(_v) { throw new Error("receiver setter should be unreachable!"); },
            };
            var accessorFail = Reflect.set(target, 0, value, receiver) === false;
            var accessorUnchanged = receiver[0] === 1;

            target = new Uint8Array([0]);
            receiver = Object.defineProperty({}, 0, { value: 1, writable: false, configurable: true });
            var readonlyFail = Reflect.set(target, 0, value, receiver) === false;
            var readonlyUnchanged = receiver[0] === 1;

            delete Uint8Array.prototype[0];

            emptyOk && targetUnchanged && receiverCreated &&
            accessorFail && accessorUnchanged &&
            readonlyFail && readonlyUnchanged &&
            valueOfCalls === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ReflectSet_Valid_Index_With_TypedArray_Receiver_Writes_Receiver_Not_Target()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var target = new Uint8Array([0]);
            var receiver = new Uint8Array([1]);
            var sameLengthOk = Reflect.set(target, 0, new Number(2.3), receiver);
            var targetUnchanged = target[0] === 0;
            var receiverUpdated = receiver[0] === 2;

            target = new Uint8Array([0, 0]);
            receiver = new Uint8Array([1]);
            var shortFail = Reflect.set(target, 1, 255.9, receiver) === false;
            var shortTargetUnchanged = target[1] === 0;
            var noPropertyCreated = !receiver.hasOwnProperty(1);

            sameLengthOk && targetUnchanged && receiverUpdated &&
            shortFail && shortTargetUnchanged && noPropertyCreated;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_At_Uses_Relative_Indexing_And_Rejects_Incompatible_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([5, 6, 7]);
            let typeError = false;
            try { Uint8Array.prototype.at.call({ length: 1, 0: 9 }, 0); } catch (e) { typeError = e && e.name === "TypeError"; }

            ta.at(0) === 5 &&
            ta.at(1.9) === 6 &&
            ta.at(-1) === 7 &&
            ta.at(-4) === undefined &&
            ta.at(9) === undefined &&
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Prototype_ToString_Is_The_ArrayPrototype_ToString_Function()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            TypedArray.prototype.toString === Array.prototype.toString;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Subarray_Returns_Shared_View_With_Relative_Indexing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const source = new Uint8Array([1, 2, 3, 4]);
            const view = source.subarray(1, -1);
            view[0] = 99;

            view !== source &&
            Object.getPrototypeOf(view) === Uint8Array.prototype &&
            view.buffer === source.buffer &&
            view.byteOffset === 1 &&
            view.length === 2 &&
            view[0] === 99 &&
            source[1] === 99 &&
            source.subarray(9).length === 0 &&
            source.subarray(-2)[0] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Set_Copies_ArrayLike_And_Handles_Overlap_And_RangeErrors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const a = new Uint8Array([1, 2, 3, 4]);
            const returned = a.set({ length: 2, 0: 7, 1: 6 }, 1.9);

            const b = new Uint8Array([1, 2, 3, 4]);
            b.set(b.subarray(0, 3), 1);

            let rangeError = false;
            try { new Uint8Array([1, 2, 3]).set([9, 8], 2); } catch (e) { rangeError = e && e.name === "RangeError"; }

            returned === undefined &&
            a[0] === 1 &&
            a[1] === 7 &&
            a[2] === 6 &&
            b[0] === 1 &&
            b[1] === 1 &&
            b[2] === 2 &&
            b[3] === 3 &&
            rangeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_Readonly_Methods_Use_TypedArray_Element_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([1, 2, 1]);
            const withNaN = new Uint8Array([0, 1]);

            ta.includes(1) &&
            ta.includes(1, 1) &&
            ta.includes(1, 3) === false &&
            withNaN.includes(NaN) === false &&
            ta.indexOf(1) === 0 &&
            ta.indexOf(1, 1) === 2 &&
            ta.lastIndexOf(1) === 2 &&
            ta.lastIndexOf(1, -2) === 0 &&
            ta.join("-") === "1-2-1";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ToLocaleString_Calls_Element_ToLocaleString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let calls = [];
            Number.prototype.toLocaleString = function() {
              calls.push(Number(this));
              return "h" + calls.length;
            };

            const ta = new Float64Array([42, 0]);
            ta.toLocaleString() === "h1,h2" &&
            calls.length === 2 &&
            calls[0] === 42 &&
            calls[1] === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Subarray_LengthTracking_View_Omits_Length_Argument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const ta = new Int8Array(rab, 0);
            const sub = ta.subarray(0);
            rab.resize(6);
            sub.length === 6 && sub.byteOffset === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ToReversed_And_ToSorted_Create_SameType_Copies_And_Ignore_Species()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([4, 2, 1, 3]);
            Object.defineProperty(ta, "constructor", {
              get() { throw new Error("should not read constructor"); }
            });

            const reversed = ta.toReversed();
            const sorted = ta.toSorted((a, b) => b - a);

            Object.getPrototypeOf(reversed) === Float64Array.prototype &&
            Object.getPrototypeOf(sorted) === Float64Array.prototype &&
            reversed !== ta &&
            sorted !== ta &&
            reversed[0] === 3 &&
            reversed[3] === 4 &&
            sorted[0] === 4 &&
            sorted[3] === 1 &&
            ta[0] === 4 &&
            typeof Float64Array.prototype.toReversed === "function" &&
            typeof Float64Array.prototype.toSorted === "function";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_With_Creates_SameType_Copy_And_Coerces_Value_Before_Copying()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([0, 1, 2]);
            Object.defineProperty(ta, "constructor", {
              get() { throw new Error("should not read constructor"); }
            });

            const value = {
              valueOf() {
                ta[0] = 3;
                return 4;
              }
            };

            const result = ta.with(1, value);

            Object.getPrototypeOf(result) === Float64Array.prototype &&
            result !== ta &&
            result[0] === 3 &&
            result[1] === 4 &&
            result[2] === 2 &&
            ta[0] === 3 &&
            ta[1] === 1 &&
            (() => { try { ta.with(-4, 7); return false; } catch (e) { return e && e.name === "RangeError"; } })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_With_Validates_Index_After_Value_Coercion_Against_Current_View_State()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(2, { maxByteLength: 5 });
            const ta = new Int8Array(rab);
            ta[0] = 11;
            ta[1] = 22;

            const growValue = {
              valueOf() {
                rab.resize(5);
                return 123;
              }
            };

            const copy = ta.with(4, growValue);

            const rab2 = new ArrayBuffer(4, { maxByteLength: 4 });
            const ta2 = new Int8Array(rab2);
            const shrinkValue = {
              valueOf() {
                rab2.resize(1);
                return 9;
              }
            };

            let rangeError = false;
            try { ta2.with(-1, shrinkValue); } catch (e) { rangeError = e && e.name === "RangeError"; }

            copy.length === 2 &&
            copy[0] === 11 &&
            copy[1] === 22 &&
            ta.length === 5 &&
            rangeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_SetFromBase64_Writes_Into_Target_And_Reports_Read_And_Written()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const target = new Uint8Array([255, 255, 255, 255, 255]);
            const result = target.setFromBase64("Zm9vYmFy");

            result.read === 4 &&
            result.written === 3 &&
            target[0] === 102 &&
            target[1] === 111 &&
            target[2] === 111 &&
            target[3] === 255 &&
            target[4] === 255;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_SetFromBase64_Writes_Valid_Chunks_Before_SyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const target = new Uint8Array([255, 255, 255, 255, 255]);
            let syntaxError = false;
            try {
              target.setFromBase64("MjYyZm.9v");
            } catch (e) {
              syntaxError = e && e.name === "SyntaxError";
            }

            syntaxError &&
            target[0] === 50 &&
            target[1] === 54 &&
            target[2] === 50 &&
            target[3] === 255 &&
            target[4] === 255;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_SetFromHex_Writes_Into_Target_And_Stops_When_Full()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const target = new Uint8Array([255, 255]);
            const result = target.setFromHex("aabbcc");

            result.read === 4 &&
            result.written === 2 &&
            target[0] === 170 &&
            target[1] === 187;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_ToBase64_Respects_Alphabet_OmitPadding_And_Detached_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1);
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Uint8Array([199, 239]);
            let getterCalls = 0;
            const target = new Uint8Array([255]);
            const options = {};
            Object.defineProperty(options, "alphabet", {
              get() {
                getterCalls++;
                detachBuffer(target.buffer);
                return "base64";
              }
            });

            let detachedTypeError = false;
            try {
              target.toBase64(options);
            } catch (e) {
              detachedTypeError = e && e.name === "TypeError";
            }

            sample.toBase64() === "x+8=" &&
            sample.toBase64({ omitPadding: true }) === "x+8" &&
            sample.toBase64({ alphabet: "base64url", omitPadding: true }) === "x-8" &&
            getterCalls === 1 &&
            detachedTypeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_ToHex_Encodes_Lowercase_And_Throws_On_Detached_Buffer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1);
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Uint8Array([0xab, 0xcd]);
            const detached = new Uint8Array(2);
            detachBuffer(detached.buffer);
            let typeError = false;
            try {
              detached.toHex();
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }

            sample.toHex() === "abcd" && typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_SetFromBase64_StopBeforePartial_Allows_Partial_Padded_Tail()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const target = new Uint8Array([255, 255, 255, 255]);
            const result = target.setFromBase64("ZXhhZg=", { lastChunkHandling: "stop-before-partial" });

            result.read === 4 &&
            result.written === 3 &&
            target[0] === 101 &&
            target[1] === 120 &&
            target[2] === 97 &&
            target[3] === 255;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Fill_Throws_When_Buffer_Becomes_OutOfBounds_During_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(16, { maxByteLength: 16 });
            const ta = new Int32Array(rab, 0, 4);
            let threw = false;
            try {
              ta.fill({
                valueOf() {
                  rab.resize(8);
                  return 3;
                }
              }, 1, 2);
            } catch (e) {
              threw = e && e.name === "TypeError";
            }
            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CopyWithin_Uses_PreCoercion_Length_For_LengthTracking_Grow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const ta = new Uint8Array(rab);
            ta[0] = 0;
            ta[1] = 1;
            ta[2] = 2;
            ta[3] = 3;

            const evil = {
              valueOf() {
                rab.resize(6);
                ta[4] = 4;
                ta[5] = 5;
                return 0;
              }
            };

            ta.copyWithin(evil, 2);

            ta.length === 6 &&
            ta[0] === 2 &&
            ta[1] === 3 &&
            ta[2] === 2 &&
            ta[3] === 3 &&
            ta[4] === 4 &&
            ta[5] === 5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CopyWithin_Truncates_To_Current_Length_After_LengthTracking_Shrink()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const ta = new Uint8Array(rab);
            ta[0] = 0;
            ta[1] = 1;
            ta[2] = 2;
            ta[3] = 3;

            ta.copyWithin({
              valueOf() {
                rab.resize(3);
                return 2;
              }
            }, 0);

            ta.length === 3 &&
            ta[0] === 0 &&
            ta[1] === 1 &&
            ta[2] === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void String_Function_Converts_Symbol_Without_Throwing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            typeof String(Symbol.iterator) === "string" &&
            String(Symbol.iterator).indexOf("Symbol.iterator") >= 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Iterator_Next_Throws_TypeError_When_Buffer_Is_Detached_MidIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array(2);
            const iter = ta.keys();
            const first = iter.next();
            let typeError = false;

            detachBuffer(ta.buffer);

            try {
              iter.next();
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }

            first.value === 0 &&
            first.done === false &&
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Keys_ForOf_Throws_TypeError_When_Buffer_Is_Detached_MidIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array(2);
            let count = 0;
            let typeError = false;

            try {
              for (const key of ta.keys()) {
                detachBuffer(ta.buffer);
                count++;
              }
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }

            count === 1 && typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_At_Uses_PreCoercion_Length_But_PostCoercion_View_State()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const fixedLength = new Uint8Array(rab, 0, 4);
            const lengthTracking = new Uint8Array(rab);

            const fixed = fixedLength.at({ valueOf() { rab.resize(2); return 0; } });

            rab.resize(4);
            const tracking = lengthTracking.at({ valueOf() { rab.resize(2); return -1; } });

            String(fixed) + "|" + String(tracking);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined|undefined"));
    }

    [Test]
    public void TypedArray_At_Throws_When_Fixed_View_Is_Already_Out_Of_Bounds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(32, { maxByteLength: 40 });
            const fixed = new Float64Array(rab, 8, 2);
            let typeError = false;

            rab.resize(23);

            try {
              fixed.at(0);
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }

            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CopyWithin_Uses_View_ByteOffset_And_Preserves_Raw_Bytes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const base = new Uint8Array([0, 1, 2, 3]).buffer;
            const ta = new Uint8Array(base, 1);
            ta.copyWithin(2, 0);

            const raw = new Uint8Array(base);

            const buffer = new ArrayBuffer(16);
            const bytes = new Uint8Array(buffer);
            for (let i = 0; i < 8; i++) bytes[i] = i + 1;
            const floats = new Float64Array(buffer);
            floats.copyWithin(1, 0);

            raw.join(",") === "0,1,2,1" &&
            new Uint8Array(buffer, 8, 8).join(",") === "1,2,3,4,5,6,7,8";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CopyWithin_Rechecks_OutOfBounds_After_Argument_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const fixedRab = new ArrayBuffer(4, { maxByteLength: 8 });
            const fixed = new Uint8Array(fixedRab, 0, 4);
            let fixedTypeError = false;
            try {
              fixed.copyWithin({ valueOf() { fixedRab.resize(2); return 2; } }, 0, 1);
            } catch (e) {
              fixedTypeError = e && e.name === "TypeError";
            }

            const trackingRab = new ArrayBuffer(4, { maxByteLength: 8 });
            const tracking = new Uint8Array(trackingRab);
            tracking.set([0, 1, 2, 3]);
            tracking.copyWithin({ valueOf() { trackingRab.resize(3); return 2; } }, 0);

            fixedTypeError &&
            tracking.length === 3 &&
            tracking.join(",") === "0,1,0";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CopyWithin_Treats_Undefined_End_As_Length_And_Uses_ArrayIterator_Prototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([0, 1, 2, 3]);
            ta.copyWithin(0, 1, undefined);

            const arrayIteratorProto = Object.getPrototypeOf([][Symbol.iterator]());
            const iter = ta.entries();

            ta.join(",") === "1,2,3,3" &&
            Object.getPrototypeOf(iter) === arrayIteratorProto;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Every_Passes_Value_Index_And_Receiver_And_Reads_Undefined_After_Shrink()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([42, 43, 44]);
            const seen = [];
            const thisArg = { marker: 1 };

            const allTrue = ta.every(function(value, index, receiver) {
              seen.push(String(value) + ":" + index + ":" + (receiver === ta) + ":" + (this === thisArg));
              return true;
            }, thisArg);

            const rab = new ArrayBuffer(32, { maxByteLength: 32 });
            const fixed = new Float64Array(rab, 0, 4);
            fixed.set([0, 2, 4, 6]);
            const shrunk = [];
            fixed.every(function(value, index) {
              shrunk.push(String(value) + ":" + index);
              if (index === 1) rab.resize(24);
              return true;
            });

            allTrue &&
            seen.join("|") === "42:0:true:true|43:1:true:true|44:2:true:true" &&
            shrunk.join("|") === "0:0|2:1|undefined:2|undefined:3";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Reflect_Set_On_TypedArray_Same_Receiver_Uses_Buffer_Write_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            let ok = true;

            sample.every(function(value, index) {
              if (index > 0) {
                ok = ok && sample[index - 1] === index - 1;
              }
              ok = ok && Reflect.set(sample, index, index);
              return true;
            });

            ok &&
            sample[0] === 0 &&
            sample[1] === 1 &&
            sample[2] === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Fill_Converts_Once_Uses_Initial_Length_And_Writes_Typed_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ints = new Uint8Array(2);
            let n = 1;
            ints.fill({ valueOf() { return n++; } });

            const rab = new ArrayBuffer(1, { maxByteLength: 4 });
            const tracking = new Int8Array(rab);
            tracking.fill({
              valueOf() {
                rab.resize(4);
                return 123;
              }
            });

            ints[0] === 1 &&
            ints[1] === 1 &&
            n === 2 &&
            tracking.length === 4 &&
            tracking[0] === 123 &&
            tracking[1] === 0 &&
            tracking[2] === 0 &&
            tracking[3] === 0 &&
            new Float64Array([0, 0, 0, 0]).fill(1, 0, undefined).join(",") === "1,1,1,1";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Filter_Uses_Internal_Length_Callback_Args_And_Produces_Typed_Result()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            const seen = [];
            const thisArg = { marker: 1 };
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const filtered = sample.filter(function(value, index, receiver) {
              seen.push(String(value) + ":" + index + ":" + (receiver === sample) + ":" + (this === thisArg));
              return index !== 1;
            }, thisArg);

            delete TypedArray.prototype.length;

            filtered instanceof Float64Array &&
            filtered.join(",") === "42,44" &&
            filtered[0] === 42 &&
            filtered[1] === 44 &&
            lengthGets === 0 &&
            seen.join("|") === "42:0:true:true|43:1:true:true|44:2:true:true";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Find_And_FindIndex_Use_Internal_Length_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([1, 2, 3]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            let resizeSeen = [];
            const rab = new ArrayBuffer(32, { maxByteLength: 32 });
            const fixed = new Float64Array(rab, 0, 4);
            fixed.set([0, 2, 4, 6]);

            const found = fixed.find(function(value, index) {
              resizeSeen.push(String(value) + ":" + index);
              if (index === 1) rab.resize(24);
              return value === undefined;
            });

            delete TypedArray.prototype.length;

            sample.find(v => v === 2) === 2 &&
            sample.findIndex(v => v === 3) === 2 &&
            lengthGets === 0 &&
            found === undefined &&
            resizeSeen.join("|") === "0:0|2:1|undefined:2";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_FindLast_Uses_Internal_Length_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([1, 2, 3]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            let resizeSeen = [];
            const rab = new ArrayBuffer(24, { maxByteLength: 32 });
            const fixed = new Float64Array(rab, 0, 3);
            fixed.set([0, 2, 4]);

            const found = fixed.findLast(function(value, index) {
              resizeSeen.push(String(value) + ":" + index);
              if (index === 2) rab.resize(8);
              return value === 0;
            });

            delete TypedArray.prototype.length;

            sample.findLast(v => v % 2 === 1) +"||"+
            lengthGets +"||" +
            found + "||" +
            resizeSeen.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString, Is.EqualTo("3||0||undefined||4:2|undefined:1|undefined:0"));
    }

    [Test]
    public void TypedArray_FindLastIndex_Uses_Internal_Length_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([1, 2, 3]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            let resizeSeen = [];
            const rab = new ArrayBuffer(24, { maxByteLength: 32 });
            const fixed = new Float64Array(rab, 0, 3);
            fixed.set([0, 2, 4]);

            const foundIndex = fixed.findLastIndex(function(value, index) {
              resizeSeen.push(String(value) + ":" + index);
              if (index === 2) rab.resize(8);
              return value === 0;
            });

            delete TypedArray.prototype.length;

            sample.findLastIndex(v => v % 2 === 1) +"||"+
            lengthGets +"||"+
            foundIndex +"||"+
            resizeSeen.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString, Is.EqualTo("2||0||-1||4:2|undefined:1|undefined:0"));
    }

    [Test]
    public void TypedArray_ForEach_Uses_Internal_Length_Callback_Args_And_Continues_After_Detach()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const seen = [];
            const thisArg = { marker: 1 };
            sample.forEach(function(value, index, receiver) {
              seen.push(String(value) + ":" + index + ":" + (receiver === sample) + ":" + (this === thisArg));
            }, thisArg);

            const detachSeen = [];
            const rab = new ArrayBuffer(16, { maxByteLength: 16 });
            const fixed = new Float64Array(rab, 0, 2);
            fixed.forEach(function(value, index) {
              if (index === 0) {
                rab.resize(0);
              }
              detachSeen.push(String(value) + ":" + index);
            });

            delete TypedArray.prototype.length;

            lengthGets === 0 &&
            seen.join("|") === "42:0:true:true|43:1:true:true|44:2:true:true" &&
            detachSeen.join("|") === "0:0|undefined:1";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Includes_Uses_Initial_Length_For_FromIndex_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(32, { maxByteLength: 64 });
            const fixed = new Float64Array(rab, 0, 4);
            fixed[0] = 0;

            const shrinkIndex = {
              valueOf() {
                rab.resize(16);
                return 0;
              }
            };

            const detachedLike = {
              valueOf() {
                rab.resize(0);
                return 0;
              }
            };

            fixed.includes(undefined) === false &&
            fixed.includes(undefined, shrinkIndex) === true
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_IndexOf_And_Join_Use_Initial_Length_When_Buffer_Changes_During_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(32, { maxByteLength: 64 });
            const tracking = new Float64Array(rab);
            tracking[0] = 1;

            const growIndex = {
              valueOf() {
                rab.resize(48);
                return -4;
              }
            };

            const growSeparator = {
              toString() {
                rab.resize(48);
                return ".";
              }
            };

            tracking.indexOf(1, growIndex) === 0 &&
            tracking.join(growSeparator) === "1.0.0.0.0.0";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsNumberFormatting_Matches_NumberPrototype_ToString_For_Large_Float64_Elements()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([9007199254740992, 1e20, 1e-6, 1e-7]);
            String(sample[0]) === sample[0].toString() &&
            sample.join(",") === [
              sample[0].toString(),
              sample[1].toString(),
              sample[2].toString(),
              sample[3].toString()
            ].join(",");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Join_Throws_For_Symbol_Separator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let threw = false;
            try {
              new Uint8Array().join(Symbol(""));
            } catch (e) {
              threw = e instanceof TypeError;
            }
            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Map_Uses_Internal_Length_Callback_Args_And_Creates_Typed_Result_Upfront()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;
            let speciesCount = -1;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            sample.constructor = {
              [Symbol.species]: function(count) {
                speciesCount = count;
                return new Float64Array(count);
              }
            };

            const seen = [];
            const thisArg = { marker: 1 };
            const mapped = sample.map(function(value, index, receiver) {
              seen.push(String(value) + ":" + index + ":" + (receiver === sample) + ":" + (this === thisArg));
              return value + 1;
            }, thisArg);

            delete TypedArray.prototype.length;

            mapped instanceof Float64Array &&
            mapped.join(",") === "43,44,45" &&
            speciesCount === 3 &&
            lengthGets === 0 &&
            seen.join("|") === "42:0:true:true|43:1:true:true|44:2:true:true";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Reduce_And_ReduceRight_Use_Internal_Length_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const reduceSeen = [];
            const reduceResult = sample.reduce(function(acc, value, index, receiver) {
              reduceSeen.push(String(acc) + ":" + String(value) + ":" + index + ":" + (receiver === sample));
              return acc + value;
            });

            const rab = new ArrayBuffer(16, { maxByteLength: 16 });
            const fixed = new Float64Array(rab, 0, 2);
            const reduceRightSeen = [];
            const reduceRightResult = fixed.reduceRight(function(acc, value, index, receiver) {
              if (index === 1) {
                rab.resize(0);
              }
              reduceRightSeen.push(String(acc) + ":" + String(value) + ":" + index + ":" + (receiver === fixed));
              return acc;
            }, 0);

            delete TypedArray.prototype.length;

            reduceResult === 129 &&
            reduceSeen.join("|") === "42:43:1:true|85:44:2:true" &&
            reduceRightResult === 0 &&
            reduceRightSeen.join("|") === "0:0:1:true|0:undefined:0:true" &&
            lengthGets === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Reverse_Exists_Is_Not_Generic_And_Uses_Internal_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;
            let typeError = false;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const sample = new Float64Array([42, 43, 44]);
            const reversed = sample.reverse();

            try {
              TypedArray.prototype.reverse.call({});
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }

            delete TypedArray.prototype.length;

            typeof TypedArray.prototype.reverse === "function" &&
            reversed === sample &&
            sample.join(",") === "44,43,42" &&
            lengthGets === 0 &&
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Set_From_ArrayLike_Gets_And_Writes_In_Order_And_Continues_After_Detach()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array(5);
            const calls = [];
            const obj = { length: 3 };

            Object.defineProperty(obj, 0, {
              get() {
                calls.push("0:" + sample.join(","));
                return 42;
              }
            });

            Object.defineProperty(obj, 1, {
              get() {
                calls.push("1:" + sample.join(","));
                return 43;
              }
            });

            Object.defineProperty(obj, 2, {
              get() {
                calls.push("2:" + sample.join(","));
                return 44;
              }
            });

            sample.set(obj, 1);

            const detached = new Float64Array([1, 2, 3]);
            let get2Called = false;
            const detachedObj = { length: 3, 0: 99 };
            Object.defineProperty(detachedObj, 1, {
              get() {
                detachBuffer(detached.buffer);
                return 100;
              }
            });
            Object.defineProperty(detachedObj, 2, {
              get() {
                get2Called = true;
                return 101;
              }
            });

            detached.set(detachedObj);

            let abrupt = false;
            const abruptTarget = new Float64Array([1, 2, 3, 4]);
            const abruptObj = { length: 4, 0: 42, 1: 43, 3: 44 };
            Object.defineProperty(abruptObj, 2, {
              get() {
                throw new Error("boom");
              }
            });

            try {
              abruptTarget.set(abruptObj);
            } catch (e) {
              abrupt = e && e.message === "boom";
            }

            const offsetDetach = new Float64Array(2);
            let calledOffset = 0;
            let offsetTypeError = false;
            try {
              offsetDetach.set([1], {
                valueOf() {
                  detachBuffer(offsetDetach.buffer);
                  calledOffset++;
                  return 0;
                }
              });
            } catch (e) {
              offsetTypeError = e && e.name === "TypeError";
            }

            const bytes = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
            const crossType = new Float32Array(bytes.buffer, 0, 2);
            crossType[0] = 42;
            bytes.set(crossType, 1);

            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const fixedSource = new Uint8Array(rab, 0, 4);
            const targetForOob = new Uint8Array(4);
            rab.resize(2);
            let oobTypeError = false;
            try {
              targetForOob.set(fixedSource);
            } catch (e) {
              oobTypeError = e && e.name === "TypeError";
            }

            sample.join(",") === "0,42,43,44,0" &&
            calls.join("|") === "0:0,0,0,0,0|1:0,42,0,0,0|2:0,42,43,0,0" &&
            get2Called &&
            detached.length === 0 &&
            detached.byteLength === 0 &&
            detached.byteOffset === 0 &&
            abrupt &&
            abruptTarget.join(",") === "42,43,3,4" &&
            calledOffset === 1 &&
            offsetTypeError &&
            bytes.join(",") === "0,42,0,66,5,6,7,8" &&
            oobTypeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Slice_Uses_Internal_Length_And_Live_Reads_After_Argument_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const sample = new Float64Array([42, 43]);
            const sliced = sample.slice();

            const rab = new ArrayBuffer(32, { maxByteLength: 64 });
            const tracking = new Float64Array(rab);
            tracking.set([1, 2, 3, 4]);
            const shrink = tracking.slice({
              valueOf() {
                rab.resize(16);
                return 0;
              }
            });

            delete TypedArray.prototype.length;

            sliced instanceof Float64Array &&
            sliced.join(",") === "42,43" &&
            shrink.join(",") === "1,2,0,0" &&
            lengthGets === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Some_Uses_Internal_Length_Callback_Args_And_Live_Element_Reads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array([42, 43, 44]);
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let lengthGets = 0;

            Object.defineProperty(TypedArray.prototype, "length", {
              get() {
                lengthGets++;
                return 0;
              },
              configurable: true
            });

            const seen = [];
            const thisArg = { marker: 1 };
            const someResult = sample.some(function(value, index, receiver) {
              seen.push(String(value) + ":" + index + ":" + (receiver === sample) + ":" + (this === thisArg));
              return false;
            }, thisArg);

            const rab = new ArrayBuffer(16, { maxByteLength: 16 });
            const fixed = new Float64Array(rab, 0, 2);
            const resizedSeen = [];
            const resizedResult = fixed.some(function(value, index) {
              if (index === 0) {
                rab.resize(0);
              }
              resizedSeen.push(String(value) + ":" + index);
              return value === undefined;
            });

            delete TypedArray.prototype.length;

            someResult === false &&
            resizedResult === true &&
            seen.join("|") === "42:0:true:true|43:1:true:true|44:2:true:true" &&
            resizedSeen.join("|") === "0:0|undefined:1" &&
            lengthGets === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_CanonicalNumericIndexString_Does_Not_Fall_Back_To_Ordinary_Get_Has_Or_Delete()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            TypedArray.prototype["-0"] = "blocked";
            TypedArray.prototype["-1"] = "blocked";
            TypedArray.prototype["1.1"] = "blocked";

            const ta = new Int32Array([7]);
            const result =
              ta["-0"] === undefined &&
              ta["-1"] === undefined &&
              ta["1.1"] === undefined &&
              Reflect.has(ta, "-0") === false &&
              Reflect.has(ta, "-1") === false &&
              Reflect.has(ta, "1.1") === false &&
              delete ta["-1"] === true &&
              delete ta["0"] === false;

            delete TypedArray.prototype["-0"];
            delete TypedArray.prototype["-1"];
            delete TypedArray.prototype["1.1"];

            result;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_DefineProperty_Uses_IntegerIndexed_Exotic_Rules_For_Canonical_Numeric_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([42, 43]);

            const setOk = Reflect.defineProperty(ta, "0", {
              value: 8,
              configurable: true,
              enumerable: true,
              writable: true
            });

            const invalidFraction = Reflect.defineProperty(ta, "1.1", {
              value: 99,
              configurable: true,
              enumerable: true,
              writable: true
            });

            const invalidMinusZero = Reflect.defineProperty(ta, "-0", {
              value: 99,
              configurable: true,
              enumerable: true,
              writable: true
            });

            setOk === true &&
            ta[0] === 8 &&
            Object.getOwnPropertyDescriptor(ta, "0").value === 8 &&
            invalidFraction === false &&
            invalidMinusZero === false &&
            ta["1.1"] === undefined &&
            ta["-0"] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_DefineProperty_On_Valid_Index_Returns_True_When_Value_Coercion_Detaches_Buffer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Float64Array([17]);
            const desc = {
              value: {
                valueOf() {
                  $262.detachArrayBuffer(ta.buffer);
                  return 42;
                }
              }
            };

            Reflect.defineProperty(ta, "0", desc) === true &&
            ta[0] === undefined;
            """));

        InstallTest262DetachBufferHarness(realm);
        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ReflectHas_Uses_HasProperty_Semantics_For_NonNumeric_Keys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var proxy = new Proxy(Object.getPrototypeOf(Int8Array).prototype, {
              has: function() { throw new Error("has trap"); }
            });
            var sample = new Float64Array(1);
            Object.setPrototypeOf(sample, proxy);

            var ok0 = Reflect.has(sample, 0) === true;
            var ok1 = Reflect.has(sample, 1) === false;
            var threw = false;
            try {
              Reflect.has(sample, "foo");
            } catch (e) {
              threw = e.message === "has trap";
            }

            ok0 && ok1 && threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Assignment_To_Detached_Buffer_Canonical_Index_Coerces_Value_Before_Failing()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262DetachBufferHarness(realm);
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var sample = new Float64Array([42]);
            $262.detachArrayBuffer(sample.buffer);

            var threw = false;
            try {
              sample[0] = { valueOf() { throw new Error("coerced"); } };
            } catch (e) {
              threw = e.message === "coerced";
            }

            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_PrototypeChain_Assignment_To_Proxy_Receiver_Uses_DefineProperty_Trap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var value = { valueOf() { throw new Error("should not coerce"); } };
            var target = new Float64Array([0]);
            var proxyTrapCalls = 0;
            var receiver = new Proxy(Object.create(target), {
              defineProperty(_target, key, desc) {
                ++proxyTrapCalls;
                Object.defineProperty(_target, key, desc);
                return true;
              }
            });

            receiver[0] = value;

            target[0] === 0 &&
            receiver[0] === value &&
            proxyTrapCalls === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_PrototypeChain_Assignment_Invalid_Index_Does_Not_Hit_Prototype_Setter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            Object.defineProperty(Float64Array.prototype, 1, {
              get() { throw new Error("getter"); },
              set(_v) { throw new Error("setter"); },
              configurable: true
            });

            var target = new Float64Array([0]);
            var receiver = Object.create(target);
            var value = { valueOf() { throw new Error("coerce"); } };
            var ok = true;
            try {
              receiver[1] = value;
            } catch (e) {
              ok = false;
            }

            delete Float64Array.prototype[1];
            ok && !target.hasOwnProperty(1) && !receiver.hasOwnProperty(1);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Receiver_In_Prototype_Chain_Coerces_Value_Once_For_OutOfBounds_Set()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let receiver = new Int32Array(10);
            let obj = Object.create(receiver);
            let valueOfCalled = 0;
            let value = { valueOf() { valueOfCalled++; return 1; } };

            Reflect.set(obj, 100, value, receiver) && valueOfCalled === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ReflectSet_SameReceiver_Returns_True_When_Value_Coercion_Detaches_Buffer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        InstallTest262DetachBufferHarness(realm);
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let ta = new Float64Array(1);
            let result = Reflect.set(ta, 0, {
              valueOf() {
                $262.detachArrayBuffer(ta.buffer);
                return 42;
              }
            });

            result === true && ta[0] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_PrototypeChain_StringReceiver_ValidIndex_Creates_Visible_Own_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var value = { tag: "v" };
            var target = new Float64Array([0]);
            var receiver = Object.setPrototypeOf(new String(""), target);
            receiver[0] = value;

            [
              receiver.hasOwnProperty(0),
              receiver[0] === value,
              receiver[0],
              target[0],
              receiver.length
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("true|true|[object Object]|0|0"));
    }

    [Test]
    public void TypedArray_Property_Assignment_To_Canonical_Numeric_String_Does_Not_Create_Ordinary_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const ta = new Int32Array(2);
            ta["1.1"] = 42;
            ta["-1"] = 99;
            ta["0"] = 7;

            ta[0] === 7 &&
            ta.hasOwnProperty("1.1") === false &&
            ta.hasOwnProperty("-1") === false &&
            ta["1.1"] === undefined &&
            ta["-1"] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ReflectSet_With_Plain_Object_Accessor_Receiver_Does_Not_Overwrite_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var receiver = {
              get 0() { return 1; },
              set 0(_v) { throw new Error("receiver setter should be unreachable!"); },
            };
            var before = Object.getOwnPropertyDescriptor(receiver, "0");
            var target = new Uint8Array([0]);
            var result = Reflect.set(target, 0, 123, receiver);
            var after = Object.getOwnPropertyDescriptor(receiver, "0");

            result === false &&
            before.get === after.get &&
            before.set === after.set &&
            receiver[0] === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Debug_TypedArray_ReflectSet_With_Plain_Object_Accessor_Receiver_State()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var receiver = {
              get 0() { return 1; },
              set 0(_v) { throw new Error("receiver setter should be unreachable!"); },
            };
            var before = Object.getOwnPropertyDescriptor(receiver, "0");
            var target = new Uint8Array([0]);
            var result = Reflect.set(target, 0, 123, receiver);
            var after = Object.getOwnPropertyDescriptor(receiver, "0");

            String(result) + "|" +
            String(before && !!before.get) + "|" +
            String(before && !!before.set) + "|" +
            String(after && !!after.get) + "|" +
            String(after && !!after.set) + "|" +
            String(receiver[0]);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("false|true|true|true|true|1"));
    }

    [Test]
    public void Debug_ObjectLiteral_Getter_On_Numeric_Key_Is_Preserved()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var receiver = { get 0() { return 1; } };
            var desc = Object.getOwnPropertyDescriptor(receiver, "0");
            String(!!desc.get) + "|" + String(!!desc.set) + "|" + String(receiver[0]);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("true|false|1"));
    }

    [Test]
    public void TypedArray_Test262_Combined_ReflectSet_Valid_Index_Altered_Receiver_Case_Passes_Locally()
    {
        var repoRoot = GetRepoRoot();
        var assertSource = File.ReadAllText(Path.Combine(repoRoot, "test262", "harness", "assert.js"));
        var typedArraySource = File.ReadAllText(Path.Combine(repoRoot, "test262", "harness", "testTypedArray.js"));
        var testSource = File.ReadAllText(Path.Combine(repoRoot, "test262", "test", "built-ins",
            "TypedArrayConstructors", "internals", "Set", "key-is-valid-index-reflect-set.js"));

        var fullSource = new StringBuilder();
        fullSource.AppendLine(assertSource);
        fullSource.AppendLine(typedArraySource);
        fullSource.Append(testSource);

        var realm = JsRuntime.Create().DefaultRealm;
        var test262Error = new JsHostFunction(realm, (in info) =>
        {
            var innerVm = info.Realm;
            var args = info.Arguments;
            var callee = info.Function;
            var err = new JsPlainObject(innerVm);
            var msg = args.Length > 0 ? args[0].ToString() : string.Empty;
            err.SetProperty("name", "Test262Error");
            err.SetProperty("message", msg);
            err.SetProperty("constructor", callee);
            return err;
        }, "Test262Error", 1);
        var test262Proto = new JsPlainObject(realm);
        test262Proto.SetProperty("constructor", test262Error);
        test262Error.SetProperty("prototype", test262Proto);
        realm.Global["Test262Error"] = test262Error;

        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript(fullSource.ToString()));

        Assert.DoesNotThrow(() => realm.Execute(script));
    }

    [Test]
    public void Debug_TypedArray_ReflectSet_ValueOf_Case_Breakdown_For_Float64Array()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var valueOfCalls = 0;
            var value = {
              valueOf: function() {
                ++valueOfCalls;
                return 2.3;
              },
            };

            function mark(label) { return label + ":" + valueOfCalls; }
            var parts = [];

            Object.defineProperty(Float64Array.prototype, 0, {
              get: function() { throw new Error("0 getter should be unreachable!"); },
              set: function(_v) { throw new Error("0 setter should be unreachable!"); },
              configurable: true,
            });

            var target, receiver;

            target = new Float64Array([0]);
            receiver = {};
            Reflect.set(target, 0, value, receiver);
            parts.push(mark("empty"));

            target = new Float64Array([0, 0]);
            receiver = new Float64Array([1]);
            Reflect.set(target, 1, value, receiver);
            parts.push(mark("short-ta"));

            target = new Float64Array([0]);
            receiver = Object.preventExtensions({});
            Reflect.set(target, 0, value, receiver);
            parts.push(mark("nonext"));

            target = new Float64Array([0]);
            receiver = {
              get 0() { return 1; },
              set 0(_v) { throw new Error("0 setter should be unreachable!"); },
            };
            Reflect.set(target, 0, value, receiver);
            parts.push(mark("accessor"));

            target = new Float64Array([0]);
            receiver = Object.defineProperty({}, 0, { value: 1, writable: false, configurable: true });
            Reflect.set(target, 0, value, receiver);
            parts.push(mark("readonly"));

            delete Float64Array.prototype[0];

            parts.join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("empty:0|short-ta:0|nonext:0|accessor:0|readonly:0"));
    }

    private static void InstallTest262DetachBufferHarness(JsRealm realm)
    {
        var test262 = new JsPlainObject(realm);
        test262.SetProperty("detachArrayBuffer", new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj) || obj is not JsArrayBufferObject buffer)
                throw new JsRuntimeException(JsErrorKind.TypeError, "detachArrayBuffer requires an ArrayBuffer");
            buffer.Detach();
            return JsValue.Undefined;
        }, "detachArrayBuffer", 1));
        realm.Global["$262"] = test262;
    }
}
