using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class AwaitPrecedenceTests
{
    [Test]
    public void Await_Operand_IsUnaryExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.value = 0;
                                                                   async function foo() {
                                                                     let x = 2;
                                                                     let y = await Promise.resolve(2) * x;
                                                                     globalThis.value = y;
                                                                   }
                                                                   foo();
                                                                   """));

        realm.Execute(script);
        realm.PumpJobs();
        Assert.That(realm.Eval("globalThis.value").Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void Await_Binds_Tighter_Than_ConditionalOperators()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   globalThis.x = "initial value";
                                                                   var shouldNotBeAwaited = {
                                                                     then: function(onFulfilled) {
                                                                       globalThis.x = "unexpected then() call";
                                                                       Promise.resolve().then(onFulfilled);
                                                                     }
                                                                   };
                                                                   async function foo() {
                                                                     await false || shouldNotBeAwaited;
                                                                   }
                                                                   foo();
                                                                   """));

        realm.Execute(script);
        realm.PumpJobs();
        Assert.That(realm.Eval("globalThis.x").AsString(), Is.EqualTo("initial value"));
    }
}
