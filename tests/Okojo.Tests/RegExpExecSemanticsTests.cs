using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpExecSemanticsTests
{
    [Test]
    public void RegExpExec_Global_AdvancesAndResetsLastIndex()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var re = /a/g;
                                                                   var a = re.exec("baaa");
                                                                   var li1 = re.lastIndex;
                                                                   var b = re.exec("baaa");
                                                                   var li2 = re.lastIndex;
                                                                   var c = re.exec("bbb");
                                                                   var li3 = re.lastIndex;
                                                                   a[0] === "a" && li1 === 2 &&
                                                                   b[0] === "a" && li2 === 3 &&
                                                                   c === null && li3 === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpExec_Sticky_RequiresMatchAtLastIndex()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var re = /a/y;
                                                                   re.lastIndex = 1;
                                                                   var ok = re.exec("baaa");
                                                                   var li1 = re.lastIndex;
                                                                   re.lastIndex = 0;
                                                                   var miss = re.exec("baaa");
                                                                   var li2 = re.lastIndex;
                                                                   ok !== null && ok[0] === "a" && li1 === 2 &&
                                                                   miss === null && li2 === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpTest_Global_UsesRegExpExecLastIndexSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var re = /a/g;
                                                                   var t1 = re.test("ba");
                                                                   var li1 = re.lastIndex;
                                                                   var t2 = re.test("ba");
                                                                   var li2 = re.lastIndex;
                                                                   t1 === true && li1 === 2 &&
                                                                   t2 === false && li2 === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpExec_UnicodeSticky_RespectsLastIndexBoundary()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var re = /\u{61}/uy;
                                                                   re.lastIndex = 1;
                                                                   var m1 = re.exec("ba");
                                                                   var li1 = re.lastIndex;
                                                                   var m2 = re.exec("ba");
                                                                   var li2 = re.lastIndex;
                                                                   m1 !== null && m1[0] === "a" && li1 === 2 &&
                                                                   m2 === null && li2 === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
