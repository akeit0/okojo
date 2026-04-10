using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ForInLexicalTests
{
    [Test]
    public void ForIn_LetCapture_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function fn(x) {
                                                                     let a = [];
                                                                     for (let p in x) {
                                                                       a.push(function () { return p; });
                                                                     }
                                                                     let k = 0;
                                                                     for (let q in x) {
                                                                       if (q !== a[k]()) return false;
                                                                       ++k;
                                                                     }
                                                                     return true;
                                                                   }
                                                                   fn({a : [0], b : 1, c : {v : 1}, get d() {}, set e(x) {}});
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void VarLetIdentifier_InSloppyMode_ParsesAndEvaluatesInObjectShorthand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var let = 1;
                                                                   var object = {let};
                                                                   object.let === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
