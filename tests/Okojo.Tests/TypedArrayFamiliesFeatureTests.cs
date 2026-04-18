using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TypedArrayFamiliesFeatureTests
{
    [Test]
    public void TypedArray_Families_Are_Installed_Globally_With_BytesPerElement()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            const cases = [
              [Int8Array, "Int8Array", 1],
              [Uint8Array, "Uint8Array", 1],
              [Uint8ClampedArray, "Uint8ClampedArray", 1],
              [Int16Array, "Int16Array", 2],
              [Uint16Array, "Uint16Array", 2],
              [Int32Array, "Int32Array", 4],
              [Uint32Array, "Uint32Array", 4],
              [Float16Array, "Float16Array", 2],
              [Float32Array, "Float32Array", 4],
              [Float64Array, "Float64Array", 8],
              [BigInt64Array, "BigInt64Array", 8],
              [BigUint64Array, "BigUint64Array", 8],
            ];

            let ok = true;
            for (const item of cases) {
              const ctor = item[0];
              const name = item[1];
              const bpe = item[2];
              const value = new ctor(1);
              ok = ok &&
                typeof ctor === "function" &&
                Object.getPrototypeOf(ctor) === TypedArray &&
                ctor.BYTES_PER_ELEMENT === bpe &&
                ctor.prototype.BYTES_PER_ELEMENT === bpe &&
                Object.getPrototypeOf(value) === ctor.prototype &&
                Object.prototype.toString.call(value) === "[object " + name + "]";
            }

            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Hidden_TypedArray_Constructor_Is_Callable_Object_But_Always_Throws_And_Shared_Statics_Are_Inherited()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let callTypeError = false;
            let constructTypeError = false;
            try { TypedArray(); } catch (e) { callTypeError = e && e.name === "TypeError"; }
            try { new TypedArray(1); } catch (e) { constructTypeError = e && e.name === "TypeError"; }

            const custom = Int32Array.from.call(function(length) {
              return new Int8Array(length);
            }, [1, 2, 3]);

            typeof TypedArray === "function" &&
            TypedArray[Symbol.species] === TypedArray &&
            callTypeError &&
            constructTypeError &&
            custom.length === 3 &&
            custom[0] === 1 &&
            custom[1] === 2 &&
            custom[2] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Hidden_TypedArray_Prototype_Has_TypedArray_Constructor_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            const desc = Object.getOwnPropertyDescriptor(TypedArray.prototype, "constructor");
            TypedArray.prototype.constructor === TypedArray &&
            desc.value === TypedArray &&
            desc.writable === true &&
            desc.enumerable === false &&
            desc.configurable === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_From_Rejects_NonConstructor_This_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            const from = TypedArray.from;
            const m = ({ m() {} }).m;
            let typeError = false;
            try { from.call(m, []); } catch (e) { typeError = e && e.name === "TypeError"; }
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Construct_Uses_NewTarget_Realm_TypedArray_Prototype_Fallback()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunction"] = JsValue.FromObject(otherRealm.FunctionConstructor);
        realm.Global["OtherFloat64Array"] = otherRealm.Global["Float64Array"];

        var result = realm.Eval("""
                                var C = new OtherFunction();
                                C.prototype = null;
                                var ta = Reflect.construct(Float64Array, [], C);
                                Object.getPrototypeOf(ta) === OtherFloat64Array.prototype;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Construct_Does_Not_Read_NewTarget_Prototype_Before_Length_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var touched = false;
                                var newTarget = function() {}.bind(null);
                                Object.defineProperty(newTarget, "prototype", {
                                  get() {
                                    touched = true;
                                    throw new Error("prototype getter should not run");
                                  }
                                });

                                try {
                                  Reflect.construct(Float64Array, [Symbol()], newTarget);
                                  false;
                                } catch (e) {
                                  e.name === "TypeError" && touched === false;
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Families_Normalize_Representative_Writes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const i8 = new Int8Array([257, -129]);
            const u8c = new Uint8ClampedArray([300, -1, 2.5, 1.5, 0.5]);
            const i16 = new Int16Array([65535]);
            const u16 = new Uint16Array([-1]);
            const u32 = new Uint32Array([-1]);
            const f16 = new Float16Array([1.5]);
            const f32 = new Float32Array([Math.PI]);
            const f64 = new Float64Array([Math.PI]);
            const bi64 = new BigInt64Array([18446744073709551615n]);
            const bu64 = new BigUint64Array([-1n]);

            i8[0] === 1 &&
            i8[1] === 127 &&
            u8c.join(",") === "255,0,2,2,0" &&
            i16[0] === -1 &&
            u16[0] === 65535 &&
            u32[0] === 4294967295 &&
            f16[0] === 1.5 &&
            Math.abs(f32[0] - Math.PI) < 0.000001 &&
            f64[0] === Math.PI &&
            bi64[0] === -1n &&
            bu64[0] === 18446744073709551615n;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Buffer_View_Respects_Element_Width_And_BigInt_Family_Split()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const buffer = new ArrayBuffer(8);
            const view = new Int16Array(buffer, 2, 2);
            view[0] = 257;
            view[1] = -1;
            const raw = new Uint8Array(buffer);

            let offsetRangeError = false;
            let familyTypeError = false;
            try { new Int16Array(buffer, 1); } catch (e) { offsetRangeError = e && e.name === "RangeError"; }
            try { new BigInt64Array(new Uint8Array([1])); } catch (e) { familyTypeError = e && e.name === "TypeError"; }

            view.length === 2 &&
            view.byteOffset === 2 &&
            raw[2] === 1 &&
            raw[3] === 1 &&
            raw[4] === 255 &&
            raw[5] === 255 &&
            offsetRangeError &&
            familyTypeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Contextual_Of_Can_Be_Used_As_Binding_Identifier()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var of = Object.getPrototypeOf(Int8Array).of;
            typeof of === "function";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Copy_Does_Not_Consult_ArrayBuffer_Species_When_Source_Buffer_Is_Subclassed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let throwOnGrossBufferConstruction = false;

            class GrossBuffer extends ArrayBuffer {
              constructor() {
                super(...arguments);
                if (throwOnGrossBufferConstruction) {
                  throw new Error("unreachable");
                }
              }

              static get [Symbol.species]() {
                throw new Error("unreachable");
              }
            }

            let grossBuf = new GrossBuffer(16);
            throwOnGrossBufferConstruction = true;
            let grossTA = new Uint8Array(grossBuf);
            let mysteryTA = new Int8Array(grossTA);

            Object.getPrototypeOf(mysteryTA.buffer) === ArrayBuffer.prototype &&
            mysteryTA.buffer.constructor === ArrayBuffer;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromBase64_Decodes_Alphabet_Options()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let badDefault = false;
            let badExplicit = false;

            try { Uint8Array.fromBase64("x-_y"); } catch (e) { badDefault = e && e.name === "SyntaxError"; }
            try { Uint8Array.fromBase64("x+/y", { alphabet: "base64url" }); } catch (e) { badExplicit = e && e.name === "SyntaxError"; }

            Uint8Array.fromBase64("x+/y").join(",") === "199,239,242" &&
            Uint8Array.fromBase64("x+/y", { alphabet: "base64" }).join(",") === "199,239,242" &&
            Uint8Array.fromBase64("x-_y", { alphabet: "base64url" }).join(",") === "199,239,242" &&
            badDefault &&
            badExplicit;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromBase64_Ignores_Ascii_Whitespace()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const cases = ["Z g==", "Z\tg==", "Z\x0Ag==", "Z\x0Cg==", "Z\x0Dg=="];
            let ok = true;
            for (const value of cases) {
              const arr = Uint8Array.fromBase64(value);
              ok = ok && arr.length === 1 && arr[0] === 102;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromHex_Decodes_And_Rejects_Illegal_Characters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const illegal = ['a.a', 'aa^', 'a a', 'a\ta', 'a\x0Aa', 'a\x0Ca', 'a\x0Da', 'a\u00A0a', 'a\u2009a', 'a\u2028a'];
            let ok = Uint8Array.fromHex("c7eff2").join(",") === "199,239,242";
            for (const value of illegal) {
              let threw = false;
              try { Uint8Array.fromHex(value); } catch (e) { threw = e && e.name === "SyntaxError"; }
              ok = ok && threw;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromHex_Rejects_NonString_Input_Without_ToString_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let calls = 0;
            const value = { toString() { calls++; throw new Error("unreachable"); } };
            let typeError = false;
            try { Uint8Array.fromHex(value); } catch (e) { typeError = e && e.name === "TypeError"; }
            typeError && calls === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromBase64_Rejects_NonString_Input_And_NonString_Options_Without_ToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let sourceToStringCalls = 0;
            const badSource = { toString() { sourceToStringCalls++; throw new Error("unreachable"); } };
            let sourceTypeError = false;
            try { Uint8Array.fromBase64(badSource, { get alphabet() { throw new Error("should not touch options"); } }); }
            catch (e) { sourceTypeError = e && e.name === "TypeError"; }

            let optionToStringCalls = 0;
            const badOption = { toString() { optionToStringCalls++; throw new Error("unreachable"); } };
            let alphabetTypeError = false;
            let lastChunkTypeError = false;
            try { Uint8Array.fromBase64("Zg==", { alphabet: badOption }); } catch (e) { alphabetTypeError = e && e.name === "TypeError"; }
            try { Uint8Array.fromBase64("Zg==", { lastChunkHandling: badOption }); } catch (e) { lastChunkTypeError = e && e.name === "TypeError"; }

            sourceTypeError &&
            alphabetTypeError &&
            lastChunkTypeError &&
            sourceToStringCalls === 0 &&
            optionToStringCalls === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Uint8Array_FromBase64_Handles_LastChunk_Modes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let strictError = false;
            let invalidError = false;
            let paddedStop = "";
            let nonZeroPaddedStop = "";
            let partialStop = "";
            try { Uint8Array.fromBase64("ZXhhZg", { lastChunkHandling: "strict" }); } catch (e) { strictError = e && e.name === "SyntaxError"; }
            try { Uint8Array.fromBase64("AA=", { lastChunkHandling: "loose" }); } catch (e) { invalidError = e && e.name === "SyntaxError"; }
            paddedStop = Uint8Array.fromBase64("ZXhhZg==", { lastChunkHandling: "stop-before-partial" }).join(",");
            nonZeroPaddedStop = Uint8Array.fromBase64("ZXhhZh==", { lastChunkHandling: "stop-before-partial" }).join(",");
            partialStop = Uint8Array.fromBase64("ZXhhZg=", { lastChunkHandling: "stop-before-partial" }).join(",");

            Uint8Array.fromBase64("ZXhhZg").join(",") === "101,120,97,102" &&
            paddedStop === "101,120,97,102" &&
            nonZeroPaddedStop === "101,120,97,102" &&
            Uint8Array.fromBase64("ZXhhZg", { lastChunkHandling: "stop-before-partial" }).join(",") === "101,120,97" &&
            partialStop === "101,120,97" &&
            Uint8Array.fromBase64("A", { lastChunkHandling: "stop-before-partial" }).length === 0 &&
            strictError &&
            invalidError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_From_Constructs_Target_First_And_Ignores_Writes_After_Resize_Or_Detach()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = JsValue.FromObject(new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length != 0 && args[0].TryGetObject(out var obj) && obj is JsArrayBufferObject buffer)
                buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1));
        var result = realm.Eval("""
                                const TypedArray = Object.getPrototypeOf(Int8Array);

                                const rab = new ArrayBuffer(3, { maxByteLength: 5 });
                                const resizeTarget = new Int8Array(rab);
                                const resizeResult = Int32Array.from.call(function() { return resizeTarget; }, [0, 1, 2], v => {
                                  if (v === 1) rab.resize(1);
                                  return v + 10;
                                });

                                const ab = new ArrayBuffer(3);
                                const detachTarget = new Int8Array(ab);
                                detachTarget.set([0, 1, 2]);
                                const detachResult = Int32Array.from.call(function() { return detachTarget; }, detachTarget, v => {
                                  if (v === 1) detachBuffer(ab);
                                  return v + 10;
                                });

                                [
                                  resizeResult === resizeTarget,
                                  resizeTarget.length,
                                  resizeTarget[0],
                                  detachResult === detachTarget,
                                  detachTarget.length
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|1|10|true|0"));
    }

    [Test]
    public void TypedArray_From_Saves_Iterator_Values_Before_Later_Number_Conversion_Mutates_Source_Array()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let values = [0, {
              valueOf() {
                values.length = 0;
                return 100;
              }
            }, 2];

            let ta = Int32Array.from(values);
            ta.length === 3 &&
            ta[0] === 0 &&
            ta[1] === 100 &&
            ta[2] === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void
        TypedArray_Of_Ignores_Write_That_Becomes_Out_Of_Bounds_During_Value_Coercion_And_Allows_Later_In_Bounds_Writes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let rab = new ArrayBuffer(3, { maxByteLength: 4 });
            let ta = new Int8Array(rab);

            let one = {
              valueOf() {
                rab.resize(0);
                return 1;
              }
            };

            let two = {
              valueOf() {
                rab.resize(4);
                return 2;
              }
            };

            let result = Int8Array.of.call(function() { return ta; }, one, two, 3);

            result === ta &&
            ta.length === 4 &&
            ta[0] === 0 &&
            ta[1] === 2 &&
            ta[2] === 3 &&
            ta[3] === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ByteOffset_Returns_Zero_When_Buffer_Is_Detached()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["detachBuffer"] = JsValue.FromObject(new JsHostFunction(realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length != 0 && args[0].TryGetObject(out var obj) && obj is JsArrayBufferObject buffer)
                buffer.Detach();
            return JsValue.Undefined;
        }, "detachBuffer", 1));

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sample = new Float64Array(new ArrayBuffer(128), 8, 1);
            detachBuffer(sample.buffer);
            sample.byteOffset === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_ByteOffset_And_ByteLength_Handle_Resizable_OutOfBounds_State()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(32, { maxByteLength: 40 });
            const fixed = new Float64Array(rab, 8, 2);
            const tracking = new Float64Array(rab, 8);

            rab.resize(0);

            fixed.byteOffset === 0 &&
            fixed.byteLength === 0 &&
            tracking.byteOffset === 0 &&
            tracking.byteLength === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Resizable_LengthTracking_TypedArray_Allows_NonAligned_Current_Buffer_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(10, { maxByteLength: 20 });
            const view = new Float64Array(rab);
            view.length === 1 && view.byteLength === 8;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Of_Throws_When_Custom_Constructor_Returns_Too_Small_A_TypedArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let typeError = false;
            try {
              TypedArray.of.call(function() { return new Uint8Array(1); }, 1, 2);
            } catch (e) {
              typeError = e && e.name === "TypeError";
            }
            typeError;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Of_Smaller_Custom_Result_Throws_A_Real_TypeError_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const TypedArray = Object.getPrototypeOf(Int8Array);
            let ok = false;
            try {
              TypedArray.of.call(function() { return new Float32Array(1); }, 1, 2);
            } catch (e) {
              ok = e && e.name === "TypeError" && e.constructor === TypeError;
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TypedArray_Of_Smaller_Custom_Result_Works_For_Test262_NonBigInt_Constructor_Families()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const TypedArray = Object.getPrototypeOf(Int8Array);
                                const ctors = [Float64Array, Float32Array, Int32Array, Int16Array, Int8Array, Uint32Array, Uint16Array, Uint8Array, Uint8ClampedArray];
                                if (typeof Float16Array !== "undefined") {
                                  ctors.push(Float16Array);
                                }

                                function makePassthrough(TA, primitiveOrIterable) {
                                  return primitiveOrIterable;
                                }

                                function makeArray(TA, primitiveOrIterable) {
                                  const n = Number(primitiveOrIterable);
                                  return Array.from({ length: n }, function() { return "0"; });
                                }

                                function makeArrayLike(TA, primitiveOrIterable) {
                                  const arr = makeArray(TA, primitiveOrIterable);
                                  const obj = { length: arr.length };
                                  for (let i = 0; i < obj.length; i++) obj[i] = arr[i];
                                  return obj;
                                }

                                function makeIterable(TA, primitiveOrIterable) {
                                  const src = makeArray(TA, primitiveOrIterable);
                                  const obj = {};
                                  obj[Symbol.iterator] = function() { return src[Symbol.iterator](); };
                                  return obj;
                                }

                                function makeArrayBuffer(TA, primitiveOrIterable) {
                                  const arr = makeArray(TA, primitiveOrIterable);
                                  return new TA(arr).buffer;
                                }

                                const factories = [makePassthrough, makeArray, makeArrayLike, makeIterable, makeArrayBuffer];
                                let firstFailure = "ok";

                                outer:
                                for (const TA of ctors) {
                                  for (const factory of factories) {
                                    const ctor = function() { return new TA(factory(TA, 1)); };
                                    try {
                                      TypedArray.of.call(ctor, 1, 2);
                                      firstFailure = TA.name + ":" + factory.name + ":no-throw";
                                      break outer;
                                    } catch (e) {
                                      if (!(e && e.name === "TypeError" && e.constructor === TypeError)) {
                                        firstFailure = TA.name + ":" + factory.name + ":" + (e && e.name) + ":" + (e && e.constructor && e.constructor.name);
                                        break outer;
                                      }
                                    }
                                  }
                                }

                                firstFailure;
                                """);

        Assert.That(result.AsString(), Is.EqualTo("ok"));
    }
}
