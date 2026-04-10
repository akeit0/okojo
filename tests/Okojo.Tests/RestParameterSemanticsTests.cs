using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RestParameterSemanticsTests
{
    [Test]
    public void RestParameter_DoesNotAliasArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f(a, ...rest) {
                                                                     arguments[0] = 1;
                                                                     arguments[1] = 2;
                                                                     return a === 3 &&
                                                                            rest.length === 2 &&
                                                                            rest[0] === 4 &&
                                                                            rest[1] === 5 &&
                                                                            arguments.length === 3 &&
                                                                            arguments[0] === 1 &&
                                                                            arguments[1] === 2;
                                                                   }
                                                                   f(3, 4, 5);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void RestParameter_WorksInDerivedConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function cmp(a, b) {
                                                                     if (a.length !== b.length) return "len:" + String(a.length) + "," + String(b.length);
                                                                     for (var i = 0; i < a.length; ++i) {
                                                                       if (a[i] !== b[i]) return "idx:" + String(i) + "," + String(a[i]) + "," + String(b[i]);
                                                                     }
                                                                     return "ok";
                                                                   }
                                                                   class Base {
                                                                     constructor(...a) {
                                                                       this.base = a;
                                                                       var args = [];
                                                                       for (var i = 0; i < arguments.length; ++i) {
                                                                         args.push(arguments[i]);
                                                                       }
                                                                       this.baseArgsCmp = cmp(args, a);
                                                                     }
                                                                   }
                                                                   class Child extends Base {
                                                                     constructor(...b) {
                                                                       super(1, 2, 3);
                                                                       this.child = b;
                                                                       var args = [];
                                                                       for (var i = 0; i < arguments.length; ++i) {
                                                                         args.push(arguments[i]);
                                                                       }
                                                                       this.childArgsCmp = cmp(args, b);
                                                                     }
                                                                   }
                                                                   var c = new Child(1, 2, 3);
                                                                   c.baseArgsCmp + "|" + c.childArgsCmp + "|" + cmp(c.child, [1,2,3]) + "|" + cmp(c.base, [1,2,3]);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ok|ok|ok|ok"));
    }

    [Test]
    public void RestParameter_DerivedConstructor_DoesNotInclude_NewTarget_As_Argument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class Base {
                                                                     constructor(...a) {
                                                                       this.baseRestLength = a.length;
                                                                       this.baseArgsLength = arguments.length;
                                                                     }
                                                                   }
                                                                   class Child extends Base {
                                                                     constructor(...b) {
                                                                       super(1, 2, 3);
                                                                       this.childRestLength = b.length;
                                                                       this.childArgsLength = arguments.length;
                                                                     }
                                                                   }
                                                                   var c = new Child(1, 2, 3);
                                                                   String(c.baseRestLength) + "|" + String(c.baseArgsLength) + "|" + String(c.childRestLength) + "|" + String(c.childArgsLength);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("3|3|3|3"));
    }
}
