using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RegExpUnicodeGapTests
{
    [Test]
    public void RegExpUnicodeEscapeBrace_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\u{0}/u.test('\u0000') &&
                                                                   /\u{1}/u.test('\u0001') &&
                                                                   /\u{3f}/u.test('?') &&
                                                                   /\u{10ffff}/u.test('\udbff\udfff');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeAstralQuantifier_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /𝌆{2}/u.test('𝌆𝌆');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeSurrogatePairAtom_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /^[\ud834\udf06]$/u.test('\ud834\udf06');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeCaseFolding_WithIgnoreCase_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\u212a/iu.test('k') && /\u212a/iu.test('K');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    [Ignore(
        "Intentional gap: non-unicode ignoreCase canonicalization differences (e.g., U+212A) are not fully modeled.")]
    public void RegExpUnicodeCaseFolding_RequiresUFlag()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\u212a/i.test('k') === false &&
                                                                   /\u212a/i.test('K') === false &&
                                                                   /\u212a/iu.test('k') === true &&
                                                                   /\u212a/iu.test('K') === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeNullEscape_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var nullChar = String.fromCharCode(0);
                                                                   /\0/u.exec(nullChar)[0] === nullChar &&
                                                                   /^\0a$/u.test('\0a') &&
                                                                   /\0②/u.exec('\x00②')[0] === '\x00②';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeNullEscape_WithStringMatchAndSearch_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ok1 = '\x00②'.match(/\0②/u)[0] === '\x00②';
                                                                   var ok2 = '\u0000፬'.search(/\0፬$/u) === 0;
                                                                   ok1 && ok2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeDot_MatchesSurrogatePairAsSingleAtom()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /^.$/u.test('\ud800\udc00');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeDecimalEscape_Backref_DoesNotMatchLoneSurrogate()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /(.+).*\1/u.test('\ud800\udc00\ud800') === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeEscapeBrace_LeadingZeros_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /\u{000000003f}/u.test('?');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeCharacterClassEscape_S_MatchesSurrogatePair()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /^\S$/u.test('\ud800\udc00');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeAstralClassRange_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var rangeRe = /[💩-💫]/u;
                                                                   rangeRe.test('\ud83d\udca8') === false &&
                                                                   rangeRe.test('\ud83d\udca9') === true &&
                                                                   rangeRe.test('\ud83d\udcaa') === true &&
                                                                   rangeRe.test('\ud83d\udcab') === true &&
                                                                   rangeRe.test('\ud83d\udcac') === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RegExpUnicodeAstralNegatedClass_LeadAndTrailSurrogates_Match()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   /[^𝌆]/u.test('\ud834') && /[^𝌆]/u.test('\udf06');
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
