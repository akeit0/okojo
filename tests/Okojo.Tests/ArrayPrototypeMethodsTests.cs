using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayPrototypeMethodsTests
{
    [Test]
    public void ArrayPrototype_Query_Methods_Use_Array_Semantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = [1, , 3, 1];
            const mapped = a.map(x => x * 2);
            const filtered = a.filter(x => x !== undefined);

            a.at(-1) === 1 &&
            a.includes(undefined) === true &&
            a.indexOf(undefined) === -1 &&
            a.lastIndexOf(1) === 3 &&
            [2, 4, 6].every(x => x % 2 === 0) &&
            a.some(x => x === 3) &&
            a.find(x => x === 3) === 3 &&
            a.findIndex(x => x === 3) === 2 &&
            a.findLast(x => x === 1) === 1 &&
            a.findLastIndex(x => x === 1) === 3 &&
            [1, 2, 3].reduce((acc, v) => acc + v, 0) === 6 &&
            ["a", "b", "c"].reduceRight((acc, v) => acc + v, "") === "cba" &&
            mapped.length === 4 &&
            mapped[0] === 2 &&
            (1 in mapped) === false &&
            mapped[2] === 6 &&
            filtered.join(",") === "1,3,1";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Mutating_Methods_Work_And_Preserve_Holes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = [1, 2];
            const pushLen = a.push(3);
            const popped = a.pop();
            const unshiftLen = a.unshift(0);
            const shifted = a.shift();

            const b = [1, , 3];
            b.reverse();

            const c = [1, 2, 3, 4];
            c.copyWithin(1, 2, 4);

            const d = [1, 2, 3];
            d.fill(9, 1, 3);

            const e = [1, 2, 3, 4];
            const deleted = e.splice(1, 2, 8, 9);

            const f = [3, 1, 2];
            f.sort();

            const sliced = [1, , 3, 4].slice(1, -1);

            pushLen === 3 &&
            popped === 3 &&
            unshiftLen === 3 &&
            shifted === 0 &&
            a.join(",") === "1,2" &&
            b[0] === 3 &&
            (1 in b) === false &&
            b[2] === 1 &&
            c.join(",") === "1,3,4,4" &&
            d.join(",") === "1,9,9" &&
            e.join(",") === "1,8,9,4" &&
            deleted.join(",") === "2,3" &&
            f.join(",") === "1,2,3" &&
            sliced.length === 2 &&
            (0 in sliced) === false &&
            sliced[1] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Sort_Does_Not_Throw_When_Shrinking_Length_Removes_Trailing_Indices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const array = [undefined, 'c', , 'b', undefined, , 'a', 'd'];

            Object.defineProperty(array, '2', {
              get() {
                return this.foo;
              },
              set(v) {
                array.length = array.length - 2;
                this.foo = v;
              }
            });

            array.sort();

            array[0] === 'a' &&
            array[1] === 'b' &&
            array[2] === 'c' &&
            array[3] === 'd' &&
            array[4] === undefined &&
            array[5] === undefined &&
            array[6] === undefined &&
            array.length === 7 &&
            array.foo === 'c';
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Repros_For_Undefined_Results_And_ToString_Fallback_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var obj = {};
            obj.shift = Array.prototype.shift;
            obj.pop = Array.prototype.pop;

            obj[0] = -1;
            obj.length = { toString() { return 0; } };
            var shifted = obj.shift();

            obj[0] = -1;
            obj.length = { toString() { return 0; } };
            var popped = obj.pop();

            var sparse = [];
            sparse[0] = 0;
            sparse[3] = 3;
            sparse.shift();
            sparse.length = 1;
            var sparseShifted = sparse.shift();

            delete Object.prototype.toString;
            var proxyDateTag = Array.prototype.toString.call(new Proxy(new Date(0), {}));

            shifted === undefined &&
            popped === undefined &&
            sparseShifted === undefined &&
            proxyDateTag === "[object Object]";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Uses_Length_When_End_Is_Undefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = [0, 1, 2, 3];
            const b = [0, 1, 2, 3];
            a.copyWithin(0, 1, undefined);
            b.copyWithin(0, 1);
            a.join(",") === "1,2,3,3" && b.join(",") === "1,2,3,3";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Throws_From_Target_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let called = false;
            let threw = false;
            try {
              [].copyWithin({ valueOf() { called = true; throw 123; } });
            } catch (e) {
              threw = e === 123;
            }
            called && threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Every_Reads_Length_Before_Callback_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var obj = { 0: 11, 1: 12 };
            var accessed = false;

            Object.defineProperty(obj, "length", {
              get: function() {
                return {
                  toString: function() {
                    accessed = true;
                    return "2";
                  }
                };
              },
              configurable: true
            });

            try {
              Array.prototype.every.call(obj, null);
            } catch (e) {
            }

            accessed === true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Every_On_Arguments_Does_Not_Grow_From_Indexed_Write()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function callbackfn1(val, idx, obj) { return val > 10; }
            function callbackfn2(val, idx, obj) { return val > 11; }

            var func = function(a, b) {
              arguments[2] = 9;
              return Array.prototype.every.call(arguments, callbackfn1) &&
                !Array.prototype.every.call(arguments, callbackfn2);
            };

            func(12, 11);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Every_On_String_Object_Uses_ParseInt_Global()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function callbackfn1(val, idx, obj) { return parseInt(val, 10) > 1; }
            function callbackfn2(val, idx, obj) { return parseInt(val, 10) > 2; }

            var str = new String("432");
            String.prototype[3] = "1";

            Array.prototype.every.call(str, callbackfn1) &&
            Array.prototype.every.call(str, callbackfn2) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_ToLocaleString_Passes_Locales_And_Options_To_Elements()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const marker = {};
                                const shapes = [];
                                const spy = {
                                  toLocaleString(...args) {
                                    shapes.push([
                                      args.length,
                                      args[0] === undefined,
                                      args[1] === undefined,
                                      args[0] === "th-u-nu-thai",
                                      args[1] === marker
                                    ].join("|"));
                                    return "ok";
                                  }
                                };
                                [spy].toLocaleString();
                                [spy].toLocaleString("th-u-nu-thai", marker);
                                shapes.join(",");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("2|true|true|false|false,2|false|false|true|true"));
    }

    [Test]
    public void ArrayPrototype_Every_Treats_EvalError_Object_As_Truthy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var accessed = false;
            function callbackfn(val, idx, obj) {
              accessed = true;
              return new EvalError();
            }

            [11].every(callbackfn) && accessed;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_ForEach_Works_On_Generic_ArrayLike_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var result = false;

            function callbackfn(val, idx, obj) {
              result = ('[object Math]' === Object.prototype.toString.call(obj));
            }

            Math.length = 1;
            Math[0] = 1;
            Array.prototype.forEach.call(Math, callbackfn);

            result;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Filter_Works_On_Generic_ArrayLike_With_Named_Index_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function callbackfn(val, idx, obj) {
              return '[object JSON]' === Object.prototype.toString.call(JSON);
            }

            JSON.length = 1;
            JSON[0] = 1;
            var newArr = Array.prototype.filter.call(JSON, callbackfn);

            newArr[0] === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_FindLast_Uses_MaxSafeInteger_LengthOfArrayLike()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var tooBigLength = Number.MAX_VALUE;
            var maxExpectedIndex = 9007199254740990;
            var arrayLike = { length: tooBigLength };
            var calledWithIndex = [];

            Array.prototype.findLast.call(arrayLike, function(_value, index) {
              calledWithIndex.push(index);
              return true;
            });

            calledWithIndex.length === 1 && calledWithIndex[0] === maxExpectedIndex;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Fill_Works_On_Near_MaxSafeInteger_ArrayLike()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var value = {};
            var startIndex = Number.MAX_SAFE_INTEGER - 3;
            var arrayLike = { length: Number.MAX_SAFE_INTEGER };

            Array.prototype.fill.call(arrayLike, value, startIndex, startIndex + 3);

            arrayLike[startIndex] === value &&
            arrayLike[startIndex + 1] === value &&
            arrayLike[startIndex + 2] === value;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Fill_Works_On_Resizable_Buffer_Backed_TypedArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const fixedLength = new Uint8Array(rab, 0, 4);
            Array.prototype.fill.call(fixedLength, 1);
            const readBack = new Uint8Array(rab, 0, rab.byteLength);
            readBack.length === 4 &&
            readBack[0] === 1 &&
            readBack[1] === 1 &&
            readBack[2] === 1 &&
            readBack[3] === 1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Fill_Returns_Boxed_Boolean_And_Treats_Undefined_End_As_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = [0, 0];
            const b = [0, 0];

            const boxedTrue = Array.prototype.fill.call(true);
            const boxedFalse = Array.prototype.fill.call(false);

            a.fill(1, 0, undefined);
            b.fill(1, undefined);

            boxedTrue instanceof Boolean &&
            boxedFalse instanceof Boolean &&
            a.join(",") === "1,1" &&
            b.join(",") === "1,1";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Fill_Call_With_Boolean_Returns_Boolean_Object_Exact_Test262_Snippet()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                Array.prototype.fill.call(true) instanceof Boolean &&
                                Array.prototype.fill.call(false) instanceof Boolean;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_FlatMap_Flattens_Exactly_One_Level()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const result = [1, 2, 3].flatMap(function(ele) {
              return [[ele * 2]];
            });

            result.length === 3 &&
            Array.isArray(result[0]) &&
            result[0][0] === 2 &&
            Array.isArray(result[1]) &&
            result[1][0] === 4 &&
            Array.isArray(result[2]) &&
            result[2][0] === 6;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Pop_Uses_LengthOfArrayLike_For_Large_Generic_Objects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var a = {};
            a.pop = Array.prototype.pop;
            a.length = Number.POSITIVE_INFINITY;
            var poppedA = a.pop();

            var b = {};
            b.pop = Array.prototype.pop;
            b[0] = "x";
            b[4294967295] = "y";
            b.length = 4294967296;
            var poppedB = b.pop();

            poppedA === undefined &&
            a.length === 9007199254740990 &&
            poppedB === "y" &&
            b.length === 4294967295 &&
            b[0] === "x" &&
            b[4294967295] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_IndexOf_Checks_Length_Before_FromIndex_And_Uses_Has_Before_Get()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var threw = false;
            var fromIndex = {
              valueOf: function() {
                throw 123;
              }
            };

            var zeroLengthOk = [].indexOf(2, fromIndex) === -1;

            var array = [1, null, 3];
            Object.setPrototypeOf(array, new Proxy(Array.prototype, {
              has: function(t, pk) {
                return pk in t;
              },
              get: function() {
                threw = true;
                throw new Error("[[Get]] trap called");
              }
            }));

            var mutatingFromIndex = {
              valueOf: function() {
                array.length = 0;
                return 0;
              }
            };

            Array.prototype.indexOf.call(array, 100, mutatingFromIndex);

            zeroLengthOk && threw === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_IndexOf_ProxyPrototype_Has_Does_Not_Intern_Canonical_Index_String()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var array = [1, null, 3];

            Object.setPrototypeOf(array, new Proxy(Array.prototype, {
              has: function(t, pk) {
                return pk in t;
              }
            }));

            var fromIndex = {
              valueOf: function() {
                array.length = 0;
                return 0;
              }
            };

            Array.prototype.indexOf.call(array, 100, fromIndex);
            true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_IndexOf_AllowProxyTraps_Has_Only_Path_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
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
                ownKeys: overrides.ownKeys || throwTest262Error('[[OwnPropertyKeys]] trap called'),
                apply: overrides.apply || throwTest262Error('[[Call]] trap called'),
                construct: overrides.construct || throwTest262Error('[[Construct]] trap called')
              };
            }

            var array = [1, null, 3];
            Object.setPrototypeOf(array, new Proxy(Array.prototype, allowProxyTraps({
              has: function(t, pk) {
                return pk in t;
              }
            })));

            var fromIndex = {
              valueOf: function() {
                array.length = 0;
                return 0;
              }
            };

            Array.prototype.indexOf.call(array, 100, fromIndex);
            true;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Join_Snapshots_Length_Before_Separator_Coercion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const rab = new ArrayBuffer(4, { maxByteLength: 8 });
            const lengthTracking = new Uint8Array(rab);
            let evil = {
              toString: () => {
                rab.resize(6);
                return '.';
              }
            };

            Array.prototype.join.call(lengthTracking, evil) === '0.0.0.0';
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Fill_Works_On_Initial_Resizable_Buffer_Ctor_Set()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function subClass(type) {
              try {
                return new Function('return class My' + type + ' extends ' + type + ' {}')();
              } catch (e) {}
            }

            const MyUint8Array = subClass('Uint8Array');
            const MyFloat32Array = subClass('Float32Array');
            const MyBigInt64Array = subClass('BigInt64Array');

            const builtinCtors = [
              Uint8Array,
              Int8Array,
              Uint16Array,
              Int16Array,
              Uint32Array,
              Int32Array,
              Float32Array,
              Float64Array,
              Uint8ClampedArray,
            ];

            if (typeof Float16Array !== 'undefined') builtinCtors.push(Float16Array);
            if (typeof BigUint64Array !== 'undefined') builtinCtors.push(BigUint64Array);
            if (typeof BigInt64Array !== 'undefined') builtinCtors.push(BigInt64Array);

            const ctors = builtinCtors.concat(MyUint8Array, MyFloat32Array);
            if (typeof MyBigInt64Array !== 'undefined') ctors.push(MyBigInt64Array);

            function readDataFromBuffer(ab, ctor) {
              let result = [];
              const ta = new ctor(ab, 0, ab.byteLength / ctor.BYTES_PER_ELEMENT);
              for (let item of ta) result.push(Number(item));
              return result.join(',');
            }

            function arrayFillHelper(ta, n) {
              if (ta instanceof BigInt64Array || ta instanceof BigUint64Array) {
                Array.prototype.fill.call(ta, BigInt(n));
              } else {
                Array.prototype.fill.call(ta, n);
              }
            }

            let failed = "";
            for (let ctor of ctors) {
              const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
              const fixedLength = new ctor(rab, 0, 4);
              arrayFillHelper(fixedLength, 1);
              const got = readDataFromBuffer(rab, ctor);
              if (got !== "1,1,1,1") {
                failed = ctor.name + ":" + got;
                break;
              }
            }

            failed === "";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Slice_Throws_RangeError_Immediately_For_Result_Length_Above_Uint32_Max()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let threwA = false;
                                let threwB = false;

                                try {
                                  const obj = { 0: "x", 4294967295: "y", length: 4294967296 };
                                  Array.prototype.slice.call(obj, 0, 4294967296);
                                } catch (e) {
                                  threwA = e && e.name === "RangeError";
                                }

                                try {
                                  const obj = { 0: "x", 4294967296: "y", length: 4294967297 };
                                  Array.prototype.slice.call(obj, 0, 4294967297);
                                } catch (e) {
                                  threwB = e && e.name === "RangeError";
                                }

                                threwA && threwB;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Splice_And_Unshift_Throw_Before_Huge_Index_Shifts_When_Length_Would_Overflow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let spliceTypeError = false;
                                let spliceRangeError = false;
                                let unshiftTypeError = false;

                                try {
                                  Array.prototype.splice.call({ length: 2 ** 53 - 1 }, 0, 0, null);
                                } catch (e) {
                                  spliceTypeError = e && e.name === "TypeError";
                                }

                                try {
                                  Array.prototype.splice.call({ length: 2 ** 32 }, 0);
                                } catch (e) {
                                  spliceRangeError = e && e.name === "RangeError";
                                }

                                try {
                                  Array.prototype.unshift.call({ length: 2 ** 53 - 1 }, null);
                                } catch (e) {
                                  unshiftTypeError = e && e.name === "TypeError";
                                }

                                spliceTypeError && spliceRangeError && unshiftTypeError;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_ToSpliced_Clamps_Length_And_Copies_Only_Final_Result_Window()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const arrayLike = {
                                  "9007199254740989": 2 ** 53 - 3,
                                  "9007199254740990": 2 ** 53 - 2,
                                  "9007199254740991": 2 ** 53 - 1,
                                  "9007199254740992": 2 ** 53,
                                  "9007199254740994": 2 ** 53 + 2,
                                  length: 2 ** 53 + 20,
                                };

                                const out = Array.prototype.toSpliced.call(arrayLike, 0, 2 ** 53 - 3);
                                out.length === 2 &&
                                out[0] === 2 ** 53 - 3 &&
                                out[1] === 2 ** 53 - 2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Returns_Object_Wrapper_For_Boolean_Primitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Array.prototype.copyWithin.call(true) instanceof Boolean &&
            Array.prototype.copyWithin.call(false) instanceof Boolean;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Boolean_Primitive_Exact_Test262_Snippet()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var a = Array.prototype.copyWithin.call(true) instanceof Boolean;
            var b = Array.prototype.copyWithin.call(false) instanceof Boolean;
            a && b;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Handles_Length_Near_MaxSafeInteger()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var startIndex = Number.MAX_SAFE_INTEGER - 3;
            var arrayLike = {
              0: 0,
              1: 1,
              2: 2,
              length: Number.MAX_SAFE_INTEGER,
            };

            arrayLike[startIndex] = -3;
            arrayLike[startIndex + 2] = -1;

            Array.prototype.copyWithin.call(arrayLike, 0, startIndex, startIndex + 3);

            arrayLike[0] === -3 &&
            (1 in arrayLike) === false &&
            arrayLike[2] === -1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Length_Near_MaxSafeInteger_Exact_Test262_Snippet()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var startIndex = Number.MAX_SAFE_INTEGER - 3;
            var arrayLike = {
              0: 0,
              1: 1,
              2: 2,
              length: Number.MAX_SAFE_INTEGER,
            };

            arrayLike[startIndex] = -3;
            arrayLike[startIndex + 2] = -1;

            Array.prototype.copyWithin.call(arrayLike, 0, startIndex, startIndex + 3);

            arrayLike[0] === -3 &&
            (1 in arrayLike) === false &&
            arrayLike[2] === -1;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Throws_From_Proxy_Has_Start()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var o = { 0: 42, length: 1 };
            var p = new Proxy(o, {
              has: function() { throw 123; }
            });

            var threw = false;
            try {
              Array.prototype.copyWithin.call(p, 0, 0);
            } catch (e) {
              threw = e === 123;
            }

            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_CopyWithin_Throws_From_Proxy_Delete_Target()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var o = { 42: true, length: 43 };
            var p = new Proxy(o, {
              deleteProperty: function(t, prop) {
                if (prop === "42") throw 456;
              }
            });

            var threw = false;
            try {
              Array.prototype.copyWithin.call(p, 42, 0);
            } catch (e) {
              threw = e === 456;
            }

            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Copy_Producing_Methods_Return_New_Arrays()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const nested = [1, [2, [3]]];
            const flat = nested.flat(2);
            const flatMapped = [1, 2].flatMap(x => [x, x * 10]);

            const src = [1, , 3];
            const reversed = src.toReversed();
            const sorted = [3, 1, 2].toSorted();
            const spliced = [1, 2, 3].toSpliced(1, 1, 9, 8);
            const replaced = src.with(1, 2);

            flat.join(",") === "1,2,3" &&
            flatMapped.join(",") === "1,10,2,20" &&
            reversed.length === 3 &&
            reversed[0] === 3 &&
            reversed[1] === undefined &&
            (1 in reversed) === true &&
            reversed[2] === 1 &&
            sorted.join(",") === "1,2,3" &&
            spliced.join(",") === "1,9,8,3" &&
            replaced.join(",") === "1,2,3" &&
            (1 in src) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Generic_Receivers_And_Stringification_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const shiftTarget = { length: 2, 0: "a", 1: "b" };
            const popTarget = { length: 1, 0: "x" };
            const fillTarget = { length: 3, 0: "a", 2: "c" };
            const joinTarget = { length: 3, 0: "a", 2: "c" };
            const locale = [{ toLocaleString() { return "x"; } }, null];
            const sliced = Array.prototype.slice.call(joinTarget, 0, 3);

            Array.prototype.shift.call(shiftTarget) === "a" &&
            shiftTarget.length === 1 &&
            shiftTarget[0] === "b" &&
            Array.prototype.pop.call(popTarget) === "x" &&
            popTarget.length === 0 &&
            Array.prototype.fill.call(fillTarget, "z", 1, 3) === fillTarget &&
            fillTarget[1] === "z" &&
            fillTarget[2] === "z" &&
            Array.prototype.join.call(joinTarget, "-") === "a--c" &&
            locale.toLocaleString() === "x," &&
            Array.prototype.toString.call([1, 2, 3]) === "1,2,3" &&
            sliced.length === 3 &&
            sliced[0] === "a" &&
            (1 in sliced) === false &&
            sliced[2] === "c";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayConstructor_Prototype_Property_Has_Const_Descriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const desc = Object.getOwnPropertyDescriptor(Array, "prototype");
            desc.value === Array.prototype &&
            desc.writable === false &&
            desc.enumerable === false &&
            desc.configurable === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_At_Uses_Live_TypedArray_Length_For_Resizable_Buffers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            class MyUint8Array extends Uint8Array {}
            class MyFloat32Array extends Float32Array {}
            class MyBigInt64Array extends BigInt64Array {}

            const ctors = [
              Uint8Array,
              Int8Array,
              Uint16Array,
              Int16Array,
              Uint32Array,
              Int32Array,
              Float32Array,
              Float64Array,
              Uint8ClampedArray,
              Float16Array,
              BigUint64Array,
              BigInt64Array,
              MyUint8Array,
              MyFloat32Array,
              MyBigInt64Array,
            ];

            function convert(value) {
              return typeof value === "bigint" ? Number(value) : value;
            }

            function maybeBigInt(ta, n) {
              return ta instanceof BigInt64Array || ta instanceof BigUint64Array ? BigInt(n) : n;
            }

            let failed = "";
            for (const ctor of ctors) {
              const rab = new ArrayBuffer(4 * ctor.BYTES_PER_ELEMENT, { maxByteLength: 8 * ctor.BYTES_PER_ELEMENT });
              const lengthTracking = new ctor(rab, 0);
              const writer = new ctor(rab);
              for (let i = 0; i < 4; ++i) {
                writer[i] = maybeBigInt(writer, i);
              }

              const initial = convert(Array.prototype.at.call(lengthTracking, -1));
              rab.resize(3 * ctor.BYTES_PER_ELEMENT);
              const shrunk = convert(Array.prototype.at.call(lengthTracking, -1));
              rab.resize(6 * ctor.BYTES_PER_ELEMENT);
              const grown = convert(Array.prototype.at.call(lengthTracking, -1));

              if (initial !== 3 || shrunk !== 2 || grown !== 0) {
                let ownLength;
                try { ownLength = String(lengthTracking.length); } catch (e) { ownLength = "throw:" + e.name; }
                failed = ctor.name + ":" + String(initial) + ":" + String(shrunk) + ":" + String(grown) +
                  ":" + String(Object.getPrototypeOf(lengthTracking) === ctor.prototype) +
                  ":" + String(Object.getPrototypeOf(ctor.prototype) === Uint8Array.prototype || Object.getPrototypeOf(ctor.prototype) === Float32Array.prototype || Object.getPrototypeOf(ctor.prototype) === BigInt64Array.prototype) +
                  ":" + ownLength +
                  ":" + Object.prototype.toString.call(lengthTracking);
                break;
              }
            }

            failed || "ok";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ok"));
    }

    [Test]
    public void ArrayPrototype_Unscopables_And_Boxed_Returns_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const unscopables = Array.prototype[Symbol.unscopables];
            const desc = Object.getOwnPropertyDescriptor(Array.prototype, Symbol.unscopables);

            Object.getPrototypeOf(unscopables) === null &&
            unscopables.findLast === true &&
            unscopables.toSorted === true &&
            Object.prototype.hasOwnProperty.call(unscopables, "with") === false &&
            desc.writable === false &&
            desc.enumerable === false &&
            desc.configurable === true &&
            (Array.prototype.reverse.call(true) instanceof Boolean) &&
            (Array.prototype.sort.call(false) instanceof Boolean);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Slice_LastIndexOf_And_ToLocaleString_Handle_Edge_Cases()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            const sliced = [0, 1, 2, 3, 4].slice(3, undefined);

            var array = [5, undefined, 7];
            let getCalled = false;
            Object.setPrototypeOf(array, new Proxy(Array.prototype, {
              has(t, pk) { return pk in t; },
              get(t, pk, r) { getCalled = true; return Reflect.get(t, pk, r); }
            }));

            Array.prototype.lastIndexOf.call(array, 100, {
              valueOf() {
                array.length = 0;
                return 2;
              }
            });

            Boolean.prototype.toString = function() { return typeof this; };
            const separator = ["", ""].toLocaleString();

            [
              String(sliced.length),
              String(sliced[0]),
              String(sliced[1]),
              String(getCalled),
              [true, false].toLocaleString(),
              separator
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|3|4|false|boolean,boolean|,"));
    }

    [Test]
    public void ArrayPrototype_ToLocaleString_Uses_Primitive_Element_Method_And_Snapshots_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            const separator = ["", ""].toLocaleString();

            Boolean.prototype.toString = function() {
              return typeof this;
            };

            const primitive = [true, false].toLocaleString();

            const array = [0, 0, 0, 0];
            array[0] = { toLocaleString() { array.length = 2; return "0"; } };
            const snap = Array.prototype.toLocaleString.call(array);

            primitive + "|" + separator + "|" + snap;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("boolean,boolean|,|0,0,,"));
    }

    [Test]
    public void
        ArrayPrototype_ToLocaleString_Invokes_Element_Method_With_No_Arguments_And_Supports_ForOf_Object_Destructuring_Repro()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const unique = { toString() { return "<sentinel object>"; } };
            const testCases = [
              { label: "no arguments", args: [] },
              { label: "undefined locale", args: [undefined] },
              { label: "string locale", args: ["ar"] },
              { label: "object locale", args: [unique] },
              { label: "undefined locale and options", args: [undefined, unique] },
              { label: "string locale and options", args: ["zh", unique] },
              { label: "object locale and options", args: [unique, unique] },
              { label: "extra arguments", args: [unique, unique, unique] },
            ];

            let labels = "";
            let argCounts = "";
            for (const { label, args } of testCases) {
              labels += label + "|";
              const spy = {
                toLocaleString() {
                  argCounts += arguments.length + "|";
                  return "ok";
                }
              };
              [spy].toLocaleString(...args);
            }

            labels + "#" + argCounts;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "no arguments|undefined locale|string locale|object locale|undefined locale and options|string locale and options|object locale and options|extra arguments|#2|2|2|2|2|2|2|2|"));
    }

    [Test]
    public void StringPrototype_Slice_Works_For_Negative_Start()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "ABCDE".slice(-2) === "DE" &&
            "ABCDE".slice(1, undefined) === "BCDE";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StringPrototype_StartsWith_Works_For_Default_And_Position()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "Float64Array".startsWith("Float") &&
            "Float64Array".startsWith("64", 5) &&
            "Float64Array".startsWith("Float", -1) &&
            "Float64Array".startsWith("Array", Infinity) === false;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Sort_Accessor_Delete_Capture_Repro_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const array = [undefined, 'c', , 'b', undefined, , 'a', 'd'];

            Object.defineProperty(array, '2', {
              get() {
                return this.foo;
              },
              set(v) {
                delete array[3];
                this.foo = v;
              }
            });

            array.sort();

            [
              array[0],
              array[1],
              array[2],
              array[3],
              String(array[4]),
              String(array[5]),
              String(array[6]),
              String('7' in array),
              String(array.hasOwnProperty('7')),
              String(array.length),
              array.foo
            ].join('|');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a|b|c|d|undefined|undefined|undefined|false|false|8|c"));
    }

    [Test]
    public void ArrayPrototype_ToReversed_And_ToSorted_Do_Not_Preserve_Holes_And_Reject_Oversized_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var reversedInput = [0, , 2, , 4];
            Array.prototype[3] = 3;
            var reversed = reversedInput.toReversed();
            delete Array.prototype[3];

            var sortedInput = [3, , 4, , 1];
            Array.prototype[3] = 2;
            var sorted = sortedInput.toSorted();
            delete Array.prototype[3];

            var rangeError = false;
            try {
              Array.prototype.toSorted.call({ length: 4294967296, get 0() { throw new Error("no"); } });
            } catch (e) {
              rangeError = e instanceof RangeError;
            }

            [
              reversed.join(","),
              String(reversed.hasOwnProperty(3)),
              sorted.join(","),
              String(sorted.hasOwnProperty(4)),
              String(rangeError)
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("4,3,2,,0|true|1,2,3,4,|true|true"));
    }

    [Test]
    public void ArrayPrototype_ToSpliced_ToString_And_Unshift_Repros_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            delete Object.prototype.toString;
            const fallback = Array.prototype.toString.call({ join: null });

            var arr = [0, , 2, , 4];
            Array.prototype[3] = 3;
            var spliced = arr.toSpliced(0, 0);
            delete Array.prototype[3];

            var rangeError = false;
            try {
              Array.prototype.toSpliced.call({ length: 4294967296, get 0() { throw new Error("no"); } }, 0, 0);
            } catch (e) {
              rangeError = e instanceof RangeError;
            }

            var array = [];
            var arrayPrototypeSet0Calls = 0;
            Object.defineProperty(Array.prototype, "0", {
              set(_val) {
                Object.freeze(array);
                arrayPrototypeSet0Calls++;
              },
              configurable: true
            });

            var unshiftThrew = false;
            try {
              array.unshift(1);
            } catch (e) {
              unshiftThrew = e instanceof TypeError;
            }
            delete Array.prototype[0];

            [
              fallback,
              spliced.join(","),
              String(spliced.hasOwnProperty(1)),
              String(spliced.hasOwnProperty(3)),
              String(rangeError),
              String(Array.prototype.unshift.length),
              String(unshiftThrew),
              String(array.hasOwnProperty(0)),
              String(array.length),
              String(arrayPrototypeSet0Calls)
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("[object Object]|0,,2,3,4|true|true|true|1|true|false|0|1"));
    }

    [Test]
    public void ArrayPrototype_With_Does_Not_Preserve_Holes_And_Range_Checks_Raw_Relative_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var arr = [0, , 2, , 4];
            Array.prototype[3] = 3;
            var result = arr.with(2, 6);
            delete Array.prototype[3];

            var threw = false;
            try {
              [0, 1, 2].with(-4, 7);
            } catch (e) {
              threw = e instanceof RangeError;
            }

            [
              result.join(","),
              String(result.hasOwnProperty(1)),
              String(result.hasOwnProperty(3)),
              String(threw)
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0,,6,3,4|true|true|true"));
    }

    [Test]
    public void ArrayPrototype_With_Does_Not_Get_Replaced_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var arr = [0, 1, 2, 3];
            Object.defineProperty(arr, "2", {
              get() {
                throw new Error("should not get");
              }
            });

            var result = arr.with(2, 6);
            result.join(",");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0,1,6,3"));
    }

    [Test]
    public void ArrayPrototype_With_Throws_RangeError_Before_Reading_Elements_When_Length_Exceeds_Array_Limit()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var arrayLike = {
              get 0() { throw new Error("Get 0"); },
              get 4294967295() { throw new Error("Get 4294967295"); },
              get 4294967296() { throw new Error("Get 4294967296"); },
              length: 2 ** 32
            };

            var threw = false;
            try {
              Array.prototype.with.call(arrayLike, 0, 0);
            } catch (e) {
              threw = e instanceof RangeError;
            }

            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Reverse_Uses_Has_Before_Get_For_Large_Proxy_ArrayLikes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function StopReverse() {}
            var arrayLike = {
              0: "zero",
              2: "two",
              get 4() { throw new StopReverse(); },
              9007199254740987: "2**53-5",
              9007199254740990: "2**53-2",
              length: 2 ** 53 + 2
            };

            var traps = [];
            var proxy = new Proxy(arrayLike, {
              getOwnPropertyDescriptor(t, pk) {
                traps.push(`GetOwnPropertyDescriptor:${String(pk)}`);
                return Reflect.getOwnPropertyDescriptor(t, pk);
              },
              defineProperty(t, pk, desc) {
                traps.push(`DefineProperty:${String(pk)}`);
                return Reflect.defineProperty(t, pk, desc);
              },
              has(t, pk) {
                traps.push(`Has:${String(pk)}`);
                return Reflect.has(t, pk);
              },
              get(t, pk, r) {
                traps.push(`Get:${String(pk)}`);
                return Reflect.get(t, pk, r);
              },
              set(t, pk, v, r) {
                traps.push(`Set:${String(pk)}`);
                return Reflect.set(t, pk, v, r);
              },
              deleteProperty(t, pk) {
                traps.push(`Delete:${String(pk)}`);
                return Reflect.deleteProperty(t, pk);
              }
            });

            try { Array.prototype.reverse.call(proxy); } catch (e) {}

            traps.join("|");
            """));

        realm.Execute(script);
        var trace = realm.Accumulator.AsString();
        Assert.That(trace,
            Is.EqualTo(
                "Get:length|Has:0|Get:0|Has:9007199254740990|Get:9007199254740990|Set:0|GetOwnPropertyDescriptor:0|DefineProperty:0|Set:9007199254740990|GetOwnPropertyDescriptor:9007199254740990|DefineProperty:9007199254740990|Has:1|Has:9007199254740989|Has:2|Get:2|Has:9007199254740988|Delete:2|Set:9007199254740988|GetOwnPropertyDescriptor:9007199254740988|DefineProperty:9007199254740988|Has:3|Has:9007199254740987|Get:9007199254740987|Set:3|GetOwnPropertyDescriptor:3|DefineProperty:3|Delete:9007199254740987|Has:4|Get:4"));
    }

    [Test]
    public void ArrayPrototype_Sort_CompareFn_Leaves_Undefined_Last_And_Propagates_Throws()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var myComparefn = function(x, y) {
              if (x === undefined) return -1;
              if (y === undefined) return 1;
              return 0;
            };

            var x = [undefined, 1];
            x.sort(myComparefn);

            var threw = false;
            try {
              [1, 0].sort(function() { throw 123; });
            } catch (e) {
              threw = e === 123;
            }

            [String(x[0]), String(x[1]), String(threw)].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|undefined|true"));
    }

    [Test]
    public void ArrayPrototype_Sort_Defaults_Are_Stable_And_Generic()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var obj = {
              valueOf: function() { return 1; },
              toString: function() { return -2; }
            };

            var array = [undefined, 2, 1, "X", -1, "a", true, obj, NaN, Infinity];
            array.sort(function(x, y) {
              var xS = String(x);
              var yS = String(y);
              if (xS < yS) return 1;
              if (xS > yS) return -1;
              return 0;
            });

            var generic = {
              0: undefined,
              1: 2,
              2: 1,
              3: "X",
              4: -1,
              5: "a",
              6: true,
              7: obj,
              8: NaN,
              9: Infinity,
              length: 10,
              sort: Array.prototype.sort
            };
            generic.sort(function(x, y) {
              var xS = String(x);
              var yS = String(y);
              if (xS < yS) return 1;
              if (xS > yS) return -1;
              return 0;
            });

            [
              array.map(String).join(","),
              [generic[0], generic[1], generic[2], generic[3], generic[4], generic[5], generic[6], generic[7], generic[8], generic[9]].map(String).join(",")
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "true,a,X,NaN,Infinity,2,1,-2,-1,undefined|true,a,X,NaN,Infinity,2,1,-2,-1,undefined"));
    }

    [Test]
    public void ArrayPrototype_Filter_Defines_Fresh_Result_Elements_Despite_Inherited_Indexed_Accessors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Array.prototype, "0", {
              get: function() { return 5; },
              configurable: true
            });

            const arr = [];
            Object.defineProperty(arr, "0", {
              get: function() { return 11; },
              configurable: true
            });

            const result = arr.filter(function(v, i) { return i === 0 && v === 11; });
            const summary = [result.length, result[0], Object.prototype.hasOwnProperty.call(result, "0")].join("|");

            delete Array.prototype[0];
            summary;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|11|true"));
    }

    [Test]
    public void ObjectLiteral_Numeric_Data_Properties_Define_Own_Elements_Despite_Inherited_Accessors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Object.prototype, "0", {
              get: function() { return false; },
              configurable: true
            });

            const result = Array.prototype.indexOf.call({ 0: true, 1: 1, length: 2 }, true);
            delete Object.prototype[0];
            result;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }
}
