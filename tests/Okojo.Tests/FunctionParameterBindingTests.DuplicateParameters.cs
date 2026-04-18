using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class FunctionParameterBindingTests
{
    [Test]
    public void DuplicateParameters_LastOccurrenceWins()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function f1(x, x) { return x; }
                                                                   function f2(x, x, x) { return x * x * x; }
                                                                   function f3(x, x) { return 'a' + x; }
                                                                   f1(1, 2) === 2 && f2(1, 2, 3) === 27 && f3(1, 2) === 'a2';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DuplicateParameters_MissingLastOccurrence_BecomesUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function f1(x, a, b, x) { return x; }
                                                                   f1(1, 2) === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DuplicateParameters_ArgumentsObject_MapsOnlyLastOccurrence()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function f(x, x) {
                                                                     var before = [x, arguments[0], arguments[1]].join('|');
                                                                     arguments[0] = 5;
                                                                     var afterFirstWrite = [x, arguments[0], arguments[1]].join('|');
                                                                     x = 7;
                                                                     var afterParamWrite = [arguments[0], arguments[1], x].join('|');
                                                                     return before + ';' + afterFirstWrite + ';' + afterParamWrite;
                                                                   }
                                                                   f(1, 2);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|1|2;2|5|2;5|7|7"));
    }
}
