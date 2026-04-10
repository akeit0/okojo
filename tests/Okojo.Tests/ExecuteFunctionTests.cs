using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ExecuteFunctionTests
{
    [Test]
    public void Execute_BytecodeFunction_WithNestedCalls_CompletesWithoutEarlyExit()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function functionCall() {
                                                                       let identity = function (x) {
                                                                           return x;
                                                                       };

                                                                       var s = 0;
                                                                       for (var i = 0; i < 10000; i++) {
                                                                           s = identity(i) + 1;
                                                                       }

                                                                       return s;
                                                                   }
                                                                   functionCall;
                                                                   """));

        realm.Execute(script);

        var function = realm.Accumulator.AsObject() as JsBytecodeFunction;
        realm.Execute(function!);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10000));
    }
}
