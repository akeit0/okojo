using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayIteratorFeatureTests
{
    [Test]
    public void Array_Prototype_SymbolIterator_Aliases_Values_And_Yields_Items()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const iter = [10, 20][Symbol.iterator]();
                                const first = iter.next();
                                const second = iter.next();
                                const done = iter.next();
                                [
                                  Array.prototype[Symbol.iterator] === Array.prototype.values,
                                  first.value,
                                  first.done,
                                  second.value,
                                  second.done,
                                  done.value,
                                  done.done,
                                  iter[Symbol.iterator]() === iter
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|10|false|20|false||true|true"));
    }

    [Test]
    public void Array_Prototype_Entries_And_Keys_Return_Array_Iterators()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const keys = ["a", "b"].keys();
                                const entries = ["a", "b"].entries();
                                [
                                  Object.prototype.toString.call(keys),
                                  keys.next().value,
                                  entries.next().value.join(","),
                                  entries.next().value.join(",")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("[object Array Iterator]|0|0,a|1,b"));
    }

    [Test]
    public void Array_Prototype_Entries_Uses_Live_Length_Until_Exhausted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var array = [];
                                var iterator = array.entries();
                                var first;
                                var second;
                                var third;

                                array.push("a");

                                first = iterator.next();
                                second = iterator.next();

                                array.push("b");
                                third = iterator.next();

                                [
                                  first.done,
                                  first.value[0],
                                  first.value[1],
                                  second.done,
                                  second.value,
                                  third.done,
                                  third.value
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("false|0|a|true||true|"));
    }

    [Test]
    public void Array_Prototype_Entries_Throws_On_Next_When_Resizable_TypedArray_View_Is_Out_Of_Bounds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const rab = new ArrayBuffer(4, { maxByteLength: 8 });
                                const fixedLength = new Uint8Array(rab, 0, 4);
                                const fixedLengthWithOffset = new Uint8Array(rab, 2, 2);
                                const lengthTracking = new Uint8Array(rab, 0);
                                const lengthTrackingWithOffset = new Uint8Array(rab, 2);

                                const fixedIter = Array.prototype.entries.call(fixedLength);
                                const fixedOffsetIter = Array.prototype.entries.call(fixedLengthWithOffset);
                                const lengthIter = Array.prototype.entries.call(lengthTracking);
                                const lengthOffsetIter = Array.prototype.entries.call(lengthTrackingWithOffset);

                                rab.resize(3);

                                let fixedThrows = false;
                                let fixedOffsetThrows = false;
                                let lengthOk = false;
                                let lengthOffsetOk = false;

                                try { fixedIter.next(); } catch (e) { fixedThrows = e && e.name === "TypeError"; }
                                try { fixedOffsetIter.next(); } catch (e) { fixedOffsetThrows = e && e.name === "TypeError"; }
                                try {
                                  const step = lengthIter.next();
                                  lengthOk = step.done === false && step.value[0] === 0 && step.value[1] === 0;
                                } catch (e) {}
                                try {
                                  const step = lengthOffsetIter.next();
                                  lengthOffsetOk = step.done === false && step.value[0] === 0 && step.value[1] === 0;
                                } catch (e) {}

                                rab.resize(1);

                                let lengthOffsetThrows = false;
                                try { lengthOffsetIter.next(); } catch (e) { lengthOffsetThrows = e && e.name === "TypeError"; }

                                fixedThrows && fixedOffsetThrows && lengthOk && lengthOffsetOk && lengthOffsetThrows;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
