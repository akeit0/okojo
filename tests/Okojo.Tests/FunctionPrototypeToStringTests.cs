using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionPrototypeToStringTests
{
    [Test]
    public void FunctionPrototypeToString_ClassDeclarationPreservesFullClassSource()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class /* a */ A /* b */ extends /* c */ B /* d */ { /* e */ constructor /* f */ ( /* g */ ) /* h */ { /* i */ ; /* j */ } /* k */ m /* l */ ( /* m */ ) /* n */ { /* o */ } /* p */ }
                                                                   function B() {}
                                                                   Function.prototype.toString.call(A);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.ToString(), Is.EqualTo(
            "class /* a */ A /* b */ extends /* c */ B /* d */ { /* e */ constructor /* f */ ( /* g */ ) /* h */ { /* i */ ; /* j */ } /* k */ m /* l */ ( /* m */ ) /* n */ { /* o */ } /* p */ }"));
    }

    [Test]
    public void FunctionPrototypeToString_AnonymousAsyncGeneratorExpression_PreservesExpressionSource()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = /* before */async /* a */ function /* b */ * /* c */ F /* d */ ( /* e */ x /* f */ , /* g */ y /* h */ ) /* i */ { /* j */ ; /* k */ ; /* l */ }/* after */;
                                                                   let g = /* before */async /* a */ function /* b */ * /* c */ ( /* d */ x /* e */ , /* f */ y /* g */ ) /* h */ { /* i */ ; /* j */ ; /* k */ }/* after */;
                                                                   [f.toString(), g.toString()].join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "async /* a */ function /* b */ * /* c */ F /* d */ ( /* e */ x /* f */ , /* g */ y /* h */ ) /* i */ { /* j */ ; /* k */ ; /* l */ }|" +
            "async /* a */ function /* b */ * /* c */ ( /* d */ x /* e */ , /* f */ y /* g */ ) /* h */ { /* i */ ; /* j */ ; /* k */ }"));
    }

    [Test]
    public void FunctionPrototypeToString_ArrowFunctions_FallBack_ToNativeSyntax()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let f = (a, b) => a + b;
                                let g = async a => a;
                                /native code/.test(Function.prototype.toString.call(f)) &&
                                /native code/.test(Function.prototype.toString.call(g));
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeToString_Preserves_Crlf_LineTerminators()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        const string source =
            "function\r\n// a\r\nf\r\n// b\r\n(\r\n// c\r\nx\r\n// d\r\n,\r\n// e\r\ny\r\n// f\r\n)\r\n// g\r\n{\r\n// h\r\n;\r\n// i\r\n;\r\n// j\r\n}\r\nf.toString();";
        var script = compiler.Compile(JavaScriptParser.ParseScript(source));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo(
            "function\r\n// a\r\nf\r\n// b\r\n(\r\n// c\r\nx\r\n// d\r\n,\r\n// e\r\ny\r\n// f\r\n)\r\n// g\r\n{\r\n// h\r\n;\r\n// i\r\n;\r\n// j\r\n}"));
    }
}
