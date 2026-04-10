using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class WhitespaceParserTests
{
    [Test]
    public void MongolianVowelSeparator_IsNotAcceptedInsideIdentifier()
    {
        Assert.That(() => JavaScriptParser.ParseScript("var\u180Efoo;"), Throws.InstanceOf<JsParseException>());
    }

    [Test]
    public void ZeroWidthNoBreakSpace_IsAcceptedAfterRegularExpressionLiteral()
    {
        Assert.That(() => JavaScriptParser.ParseScript("/x/g\uFEFF;"), Throws.Nothing);
    }

    [Test]
    public void DoWhileStatement_AllowsSameLineTerminationWithoutExplicitSemicolon()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var x;
                                                                   do break; while (0) x = 42;
                                                                   x;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }
}
