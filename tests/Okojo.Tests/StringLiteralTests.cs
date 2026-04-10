using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class StringLiteralTests
{
    [Test]
    public void StringLiteral_Allows_Raw_LineSeparator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("\"\u2028\" === \"\\u2028\";"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StringLiteral_Allows_Raw_ParagraphSeparator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("\"\u2029\" === \"\\u2029\";"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Eval_Allows_Raw_LineSeparator_Inside_StringLiteral()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("eval(\"'\\u2028'\") === \"\\u2028\";"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StringLiteral_Legacy_Octal_Escapes_Match_Sloppy_JavaScript()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   '\1' === '\x01' &&
                                                                   '\11' === '\x09' &&
                                                                   '\40' === '\x20' &&
                                                                   '\400' === '\x200' &&
                                                                   '\08' === '\x008';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
