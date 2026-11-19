using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ArrayLengthTests
{
    [Test]
    public void IndexedAppendObservesPrototypeChangesAfterDenseWrites()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            function append(array, index, value) {
              "use strict";
              array[index] = value;
            }
            const array = [];
            for (let i = 0; i < 8; ++i) append(array, i, i);

            let seen = 0;
            Object.defineProperty(Array.prototype, "8", {
              set(value) { seen = this === array ? value : -1; },
              configurable: true
            });
            append(array, 8, 42);
            const setterObserved = seen === 42 && array.length === 8 && !Object.hasOwn(array, 8);
            delete Array.prototype[8];

            Object.defineProperty(Object.prototype, "8", { value: 99, configurable: true });
            let threw = false;
            try { append(array, 8, 43); } catch (e) { threw = e instanceof TypeError; }
            delete Object.prototype[8];

            append(array, 8, 44);
            const descriptor = Object.getOwnPropertyDescriptor(array, "8");
            setterObserved && threw && array.length === 9 && descriptor.value === 44 &&
              descriptor.writable && descriptor.enumerable && descriptor.configurable;
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void RestrictedArrayAssignmentCanCallAnInheritedIndexSetter()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            const prototype = { set 0(value) { this.seen = value; } };
            const noExtension = [];
            const fixedLength = [];
            for (const array of [noExtension, fixedLength]) {
                array.seen = 0;
                Object.setPrototypeOf(array, prototype);
            }
            Object.preventExtensions(noExtension);
            Object.defineProperty(fixedLength, "length", { writable: false });

            function write(array, index, value) {
                "use strict";
                array[index] = value;
            }
            write(noExtension, 0, 42);
            write(fixedLength, 0, 43);

            noExtension.seen === 42 && fixedLength.seen === 43 &&
                noExtension.length === 0 && fixedLength.length === 0 &&
                !Object.hasOwn(noExtension, 0) && !Object.hasOwn(fixedLength, 0) &&
                !Reflect.set(noExtension, 1, 7) && !Reflect.set(fixedLength, 1, 7);
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void NonExtensibleArrayCanRedefineAnExistingElement()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            const array = [];
            Object.defineProperty(array, "2", { value: 17, enumerable: true, configurable: true });
            Object.preventExtensions(array);
            Object.defineProperty(array, "2", { writable: true });
            array[2] = 23;
            const descriptor = Object.getOwnPropertyDescriptor(array, "2");
            array.length === 3 && !Object.isExtensible(array) &&
                !(0 in array) && !(1 in array) && descriptor.value === 23 &&
                descriptor.writable && descriptor.enumerable && descriptor.configurable;
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void PushAtMaximumArrayLengthPreservesSparseWritesAndRangeError()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            const array = new Array(4294967294);
            const pushed = array.push(42);
            let threw = false;
            try { array.push(43); } catch (e) { threw = e instanceof RangeError; }
            pushed === 4294967295 && array.length === 4294967295 &&
                array[4294967294] === 42 && threw && array[4294967295] === 43;
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void PushCallsAnIndexedSetTrapOnThePrototype()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            const array = [];
            let sameReceiver = false;
            let calls = 0;
            const prototype = new Proxy(Array.prototype, {
                set(target, key, value, receiver) {
                    calls++;
                    sameReceiver = key === "0" && value === 17 && receiver === array;
                    return true;
                }
            });
            Object.setPrototypeOf(array, prototype);
            Array.prototype.push.call(array, 17) === 1 && array.length === 1 &&
                calls === 1 && sameReceiver && !Object.hasOwn(array, 0);
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Array_Constructor_Allows_Uint32_Max_Length_But_Not_Above()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                var okMax = new Array(4294967295).length === 4294967295;
                var threw = false;
                try {
                  new Array(4294967296);
                } catch (e) {
                  threw = e instanceof RangeError;
                }
                okMax && threw;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Array_Length_Assignment_And_DefineProperty_Invalid_Length_Throw_RangeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                var assignThrew = false;
                try {
                  [].length = 4294967296;
                } catch (e) {
                  assignThrew = e instanceof RangeError;
                }

                var defineThrew = false;
                try {
                  Object.defineProperty([], "length", { value: -1, configurable: true });
                } catch (e) {
                  defineThrew = e instanceof RangeError;
                }

                assignThrew && defineThrew;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Array_Length_Shrink_Deletes_Sparse_High_Index_Without_Linear_Countdown()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                var x = [0, 1, 2];
                x[4294967294] = 4294967294;
                x.length = 2;
                x[0] === 0 &&
                x[1] === 1 &&
                x[2] === undefined &&
                x[4294967294] === undefined;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
