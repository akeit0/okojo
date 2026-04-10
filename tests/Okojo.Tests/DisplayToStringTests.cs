using System.Text;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Reflection;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DisplayToStringTests
{
    [Test]
    public void PlainObject_ToString_ShowsEnumerableProperties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = { x: 3 };
                                                                   o;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("{ x: 3 }"));
    }

    [Test]
    public void Function_ToString_ShowsAnonymousFunctionDisplay()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (() => {});
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("[Function (anonymous)]"));
    }

    [Test]
    public void PlainObject_ToString_QuotesNonIdentifierKeys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ({ "round#2": 12.34, "1": "one", normal_key: true });
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("{ '1': 'one', 'round#2': 12.34, normal_key: true }"));
    }

    [Test]
    public void PlainObject_ToString_ShowsAccessorKinds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ({
                                                                       get x() { return 1; },
                                                                       set y(v) { }
                                                                   });
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.ToString(), Is.EqualTo("{ x: [Getter], y: [Setter] }"));
    }

    [Test]
    public void PlainObject_ToDisplayString_WithIndent_PrintsMultiline()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   ({ a: 1, b: { c: 2 } });
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.TryGetObject(out var obj), Is.True);
        Assert.That(obj, Is.Not.Null);

        var text = obj!.ToDisplayString(2);
        var nl = Environment.NewLine;
        var expected = "{" + nl +
                       "  a: 1," + nl +
                       "  b: {" + nl +
                       "    c: 2" + nl +
                       "  }" + nl +
                       "}";
        Assert.That(text, Is.EqualTo(expected));
    }

    [Test]
    public void ClrNamespaceAndType_ToString_UseToStringTagDisplay()
    {
        var realm = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrNamespaceSample).Assembly)).DefaultRealm;
        var ns = realm.GetClrNamespace("Okojo.Tests");
        Assert.That(ns.ToString(), Is.EqualTo("[CLR Namespace Okojo.Tests]"));

        Assert.That(ns.TryGetProperty("ClrNamespaceSample", out var typeValue), Is.True);
        Assert.That(typeValue.ToString(), Is.EqualTo("[CLR Type Okojo.Tests.ClrNamespaceSample]"));
    }

    [Test]
    public void HostObject_ToString_UsesClrTypeDisplay()
    {
        var realm = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(StringBuilder).Assembly)).DefaultRealm;
        var hostValue = realm.WrapHostValue(new StringBuilder(100));

        Assert.That(hostValue.ToString(), Is.EqualTo("[System.Text.StringBuilder]"));
    }
}
