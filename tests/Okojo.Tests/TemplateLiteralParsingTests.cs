using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TemplateLiteralParsingTests
{
    [Test]
    public void TemplateLiteral_Interpolation_Can_Contain_Regex_Literal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("`type=${typeof (/'/g)}`;"));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("type=object"));
    }

    [Test]
    public void TemplateLiteral_Interpolation_Still_Allows_Division_Expression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var a = 12;
            var b = 3;
            `value=${a / b}`;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("value=4"));
    }

    [Test]
    public void TemplateLiteral_Interpolation_Uses_ToString_Semantics_For_Boxed_Symbols()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Symbol.prototype, Symbol.toPrimitive, { value: null });
            `${Object(Symbol())}`;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Symbol()"));
    }
}
