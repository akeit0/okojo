using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpLiteralTests
{
    [Test]
    public void RegExpLiteral_CreatesDistinctObjects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function makeRegExp() { return /(?:)/; }
                                                                   const a = makeRegExp();
                                                                   const b = makeRegExp();
                                                                   a !== b;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpLiteral_InvalidInEval_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok = false;
                                                                   try { eval("/\\\rn/;"); } catch (e) { ok = e instanceof SyntaxError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpLiteral_NamedGroup_ForwardReference_Matches_Empty_Capture()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\k<a>(?<a>x)/.test("x");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpLiteral_NamedBackreferenceSyntax_Without_NamedGroups_Is_IdentityEscape_In_NonUnicode_Mode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\k<a>/.test("k<a>");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpLiteral_NamedBackreferenceSyntax_Without_NamedGroups_Throws_In_Unicode_Mode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok = false;
                                                                   try { eval("/\\k<a>/u"); } catch (e) { ok = e instanceof SyntaxError; }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpLiteral_LoneSurrogate_NamedGroupName_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var lead = false;
                                                                   var trail = false;
                                                                   try { eval("/(?<a\uD801>.)/"); } catch (e) { lead = e instanceof SyntaxError; }
                                                                   try { eval("/(?<a\uDCA4>.)/"); } catch (e) { trail = e instanceof SyntaxError; }
                                                                   lead && trail;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
