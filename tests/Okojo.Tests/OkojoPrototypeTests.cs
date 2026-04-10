using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class OkojoPrototypeTests
{
    [Test]
    public void TestBoxedPrototypeValueOfAndToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function t() {
                let n = Object(1).valueOf();
                let b = Object(true).valueOf();
                let s = Object("xy").valueOf();
                let ns = Object(1).toString();
                let bs = Object(false).toString();
                let ss = Object("xy").toString();
                if (n !== 1) return 0;
                if (b !== true) return 0;
                if (s !== "xy") return 0;
                if (ns !== "1") return 0;
                if (bs !== "false") return 0;
                if (ss !== "xy") return 0;
                return 1;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void TestFunctionPrototypeToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function t() {
                return (function x(){}).toString();
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function x(){}"));
    }

    [Test]
    public void TestPrimitiveNumberMemberCallToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("(1).toString();"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1"));
    }

    [Test]
    public void TestObjectNumberBoxedAddition()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("Object(1) + 2;"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32 || realm.Accumulator.IsNumber, Is.True);
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3d));
    }

    [Test]
    public void ObjectPrototypeValueOf_BoxesPrimitives_AndThrowsOnNullishThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok1 = typeof Object.prototype.valueOf.call(true) === "object";
                                var ok2 = typeof Object.prototype.valueOf.call(false) === "object";
                                var ok3 = false;
                                var ok4 = false;
                                try { Object.prototype.valueOf.call(undefined); } catch (e) { ok3 = e && e.name === "TypeError"; }
                                try { var f = Object.prototype.valueOf; f(); } catch (e) { ok4 = e && e.name === "TypeError"; }
                                ok1 && ok2 && ok3 && ok4;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ObjectPrototypeToString_BuiltinTags_WorkForCoreObjects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let arr = [];
                                var ok1 = Object.prototype.toString.call((function(){ return arguments; })()) === "[object Arguments]";
                                arr.push(ok1);
                                var ok2 = Object.prototype.toString.call(Error()) === "[object Error]";
                                arr.push(ok2);
                                var ok3 = Object.prototype.toString.call(/./) === "[object RegExp]";
                                arr.push(ok3);
                                var ok4 = Object.prototype.toString.call(new Date(0)) === "[object Date]";
                                arr.push(ok4);
                                var ok5 = Object.prototype.toString.call("") === "[object String]";
                                arr.push(ok5);
                                arr.join(",");
                                """);

        Assert.That(result.Tag, Is.EqualTo(Tag.JsTagString));
        Assert.That(result.AsString(), Is.EqualTo("true,true,true,true,true"));
    }

    [Test]
    public void ObjectFreeze_StringObject_IndexProperty_RemainsOwn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var s = new String("abc");
                                Object.freeze(s);
                                var d = Object.getOwnPropertyDescriptor(s, "0");
                                s.hasOwnProperty("0") &&
                                d.value === "a" &&
                                d.writable === false &&
                                d.configurable === false;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }
}
