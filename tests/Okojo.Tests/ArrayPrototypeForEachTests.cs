using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayPrototypeForEachTests
{
    [Test]
    public void ArrayPrototype_ForEach_InvokesCallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var sum = 0;
                                                                   [1, 2, 3].forEach(function (v) { sum += v; });
                                                                   sum === 6;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_ForEach_ArrowThis_IgnoresThisArg()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var calls = 0;
                                                                   var usurper = {};
                                                                   [1].forEach(v => {
                                                                     calls++;
                                                                     if (this === usurper) throw new Error("thisArg overrode lexical this");
                                                                   }, usurper);
                                                                   calls === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
