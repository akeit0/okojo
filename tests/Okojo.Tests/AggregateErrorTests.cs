using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AggregateErrorTests
{
    [Test]
    public void AggregateError_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const e = new AggregateError([1, 2], "msg", { cause: 42 });
                                                                   [
                                                                     e instanceof AggregateError,
                                                                     e instanceof Error,
                                                                     e.name,
                                                                     e.message,
                                                                     e.errors.length,
                                                                     e.errors[0],
                                                                     e.cause,
                                                                     Object.prototype.propertyIsEnumerable.call(e, "errors"),
                                                                     Object.prototype.propertyIsEnumerable.call(e, "message"),
                                                                     Object.prototype.propertyIsEnumerable.call(e, "cause")
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|AggregateError|msg|2|1|42|false|false|false"));
    }

    [Test]
    public void AggregateError_Call_Without_New_Uses_AggregateError_Prototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const e = AggregateError([], "");
                                                                   (Object.getPrototypeOf(e) === AggregateError.prototype) &&
                                                                   (e instanceof AggregateError) &&
                                                                   (e instanceof Error);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    [Ignore(
        "Blocked by host constructor cross-realm newTarget fallback path; see OKOJO_FEATURE_AGGREGATEERROR_PHASE1.md")]
    public void AggregateError_CrossRealm_NewTarget_Fallback_Prototype_Repro()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const other = globalThis.__createRealmForTest__();
                                                                   const NewTarget = new other.Function();
                                                                   NewTarget.prototype = undefined;
                                                                   const e = Reflect.construct(AggregateError, [[]], NewTarget);
                                                                   Object.getPrototypeOf(e) === other.AggregateError.prototype;
                                                                   """));

        realm.Global["__createRealmForTest__"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var otherRealm = innerRealm.Agent.CreateRealm();
            return JsValue.FromObject(otherRealm.GlobalObject);
        }, "__createRealmForTest__", 0));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AggregateError_Processes_Message_Before_Errors_Iteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let sequence = [];
                                                                   const message = {
                                                                     toString() {
                                                                       sequence.push(1);
                                                                       return "";
                                                                     }
                                                                   };
                                                                   const errors = {
                                                                     [Symbol.iterator]() {
                                                                       sequence.push(2);
                                                                       return {
                                                                         next() {
                                                                           sequence.push(3);
                                                                           return { done: true };
                                                                         }
                                                                       };
                                                                     }
                                                                   };
                                                                   new AggregateError(errors, message);
                                                                   sequence.join(",");
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,2,3"));
    }

    [Test]
    public void AggregateError_Without_Errors_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   new AggregateError();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }
}
