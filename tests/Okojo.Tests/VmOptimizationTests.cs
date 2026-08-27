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
}
