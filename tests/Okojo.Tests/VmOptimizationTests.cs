using Okojo.JavaScript.Embedding;

namespace Okojo.Tests;

public class VmOptimizationTests
{
    [Test]
    public void OwnPropertyIcRemainsAnOwnCache()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = realm.CompileScript(
            """
            function read() {
                const object = { value: 1 };
                return object.value;
            }
            read();
            """
        );
        var function = script
            .ObjectConstants.OfType<Okojo.JavaScript.Objects.JsBytecodeFunction>()
            .Single(static candidate => candidate.Name == "read");

        realm.Execute(script);

        Assert.That(function.Script.NamedPropertyIcEntries, Is.Not.Null);
        Assert.That(
            function.Script.NamedPropertyIcEntries!.Any(static entry => entry.Shape is not null),
            Is.True
        );
    }

    [Test]
    public void PrototypePropertyIcGuardsPrototypeAndHolderShape()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = realm.CompileScript(
            """
            const prototype = { value: 1 };
            const receiver = Object.create(prototype);
            function read() { return receiver.value; }
            const first = read();
            prototype.value = 2;
            const second = read();
            Object.setPrototypeOf(receiver, { value: 3 });
            const third = read();
            [first, second, third].join(":");
            """
        );
        var function = script
            .ObjectConstants.OfType<Okojo.JavaScript.Objects.JsBytecodeFunction>()
            .Single(static candidate => candidate.Name == "read");

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1:2:3"));
        Assert.That(
            function.Script.PrototypeNamedPropertyIcEntries!.Any(static entry =>
                entry.Holder is not null
            ),
            Is.True
        );
    }

    [Test]
    public void InheritedPropertyStoreDoesNotUsePrototypeLoadCache()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            const prototype = { value: 1 };
            const object = Object.create(prototype);
            function write() { object.value = 5; }
            write();
            [prototype.value, object.value].join(":");
            """
        );

        Assert.That(result.AsString(), Is.EqualTo("1:5"));
    }

    [Test]
    public void LeafMathCallFallsBackForCoercibleObjects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            let calls = 0;
            const value = { valueOf() { calls = calls + 1; return 1; } };
            Math.sin(value);
            calls;
            """
        );

        Assert.That(result.IsInt32, Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void DenseKeyedLoadFastPathPreservesHolesAndDescriptors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            const array = [10, 20];
            array.length = 4;
            Array.prototype[2] = 30;
            const inherited = array[2];
            const own = array[0];
            let getterCalls = 0;
            Object.defineProperty(array, "0", {
              get() { getterCalls++; return 99; },
              configurable: true
            });
            const accessor = array[0];
            delete Array.prototype[2];
            [own, inherited, accessor, getterCalls].join(":");
            """
        );

        Assert.That(result.AsString(), Is.EqualTo("10:30:99:1"));
    }

    [Test]
    public void DateSubtractionFastPathPreservesConversionGuards()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            const left = new Date(1000);
            const right = new Date(250);
            const normal = left - right;
            const originalValueOf = Date.prototype.valueOf;
            Date.prototype.valueOf = function() { return 5; };
            const mutatedPrototype = left - right;
            Date.prototype.valueOf = originalValueOf;
            left.valueOf = function() { return 9; };
            const ownOverride = left - right;
            [normal, mutatedPrototype, ownOverride].join(":");
            """
        );

        Assert.That(result.AsString(), Is.EqualTo("750:0:-241"));
    }
}
