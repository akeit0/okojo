using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DestructuringReleaseReproTests
{
    [Test]
    public void ObjectLiteral_ComputedThenNamedThenNamed_DoesNot_Reuse_Wrong_NamedStoreCache()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var o = {
              [Symbol.iterator]() { return this; },
              next() { return { done: true, value: 1 }; },
              return: 0
            };

            typeof o.next === "function" && o.return === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayAssignmentDestructuring_Minimal_IteratorNext_Should_Be_Callable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var x;
            ([x] = {
              [Symbol.iterator]() { return this; },
              next() { return { done: true, value: 1 }; },
              return: 0
            });
            x === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayAssignmentDestructuring_Minimal_DefaultThrow_Should_Preserve_Thrown_Primitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var x;
            ([x = (() => { throw 7; })()] = {
              [Symbol.iterator]() { return this; },
              next() { return { done: false }; },
              return: 0
            });
            """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("JS_THROW_VALUE"));
        Assert.That(ex.ThrownValue.HasValue, Is.True);
        Assert.That(ex.ThrownValue!.Value.Int32Value, Is.EqualTo(7));
    }
}
