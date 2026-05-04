using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using System.Text;

namespace Okojo.Tests;

public class ForInTests
{
    [Test]
    public void ForIn_Object_OwnEnumerableKeys_CountsExpected()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let count = 0;
                                                                   for (var k in { a: 1, b: 2, c: 3 }) {
                                                                       count = count + 1;
                                                                   }
                                                                   count;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void ForIn_String_EnumeratesIndexKeys()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let out = "";
                                                                   for (var k in "ab") {
                                                                       out = out + k;
                                                                   }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("01"));
    }

    [Test]
    public void ForIn_Null_IsNoOp()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let count = 0;
                                                                   for (var k in null) {
                                                                       count = count + 1;
                                                                   }
                                                                   count;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void ForIn_Compiler_EmitsDedicatedForInBytecodes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(o) {
                                                                       let c = 0;
                                                                       for (var k in o) c = c + 1;
                                                                       return c;
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var code = t.Script.Bytecode;

        var sawEnumerate = false;
        var sawNext = false;
        var sawStep = false;
        for (var i = 0; i < code.Length; i++)
        {
            var op = (JsOpCode)code[i];
            if (op == JsOpCode.ForInEnumerate)
                sawEnumerate = true;
            if (op == JsOpCode.ForInNext)
                sawNext = true;
            if (op == JsOpCode.ForInStep)
                sawStep = true;
        }

        Assert.That(sawEnumerate, Is.True);
        Assert.That(sawNext, Is.True);
        Assert.That(sawStep, Is.True);
    }

    [Test]
    public void ForIn_MemberExpression_Head_Assigns_To_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let target = {};
                                                                   let count = 0;
                                                                   for (target.value in { attr: null }) {
                                                                       count = count + 1;
                                                                   }
                                                                   target.value === 'attr' && count === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForIn_SloppyLetIdentifier_Is_Allowed_As_LeftHandSideExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = { key: 1 };
                                                                   var let;
                                                                   for (let in obj) ;
                                                                   let === 'key';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForIn_Skips_Key_Deleted_Before_Visit()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = Object.create(null);
                                                                   obj.aa = 1;
                                                                   obj.ba = 2;
                                                                   obj.ca = 3;
                                                                   var accum = "";

                                                                   function erase(hash, prefix) {
                                                                       for (var key in hash) {
                                                                           if (key.indexOf(prefix) === 0) {
                                                                               delete hash[key];
                                                                           }
                                                                       }
                                                                   }

                                                                   for (var key in obj) {
                                                                       erase(obj, "b");
                                                                       accum += key + obj[key];
                                                                   }

                                                                   accum === "aa1ca3" || accum === "ca3aa1";
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForIn_TypedArray_From_ResizableBuffer_Enumerates_Indices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let rab = new ArrayBuffer(100, { maxByteLength: 200 });
                                                                   let ta = new Uint8Array(rab, 0, 3);
                                                                   let keys = '';
                                                                   for (const key in ta) {
                                                                       keys += key;
                                                                   }
                                                                   keys === '012';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForIn_With_High_Register_Index_Uses_Scaled_Runtime_Helper()
    {
        var locals = new StringBuilder();
        for (var i = 0; i < 270; i++)
            locals.Append("var r").Append(i).Append('=').Append(i).Append(';');

        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript($$"""
                                                                              function test(obj) {
                                                                                {{locals}}
                                                                                var out = "";
                                                                                for (var key in obj) {
                                                                                  if (obj.hasOwnProperty(key)) out += key;
                                                                                }
                                                                                return out;
                                                                              }
                                                                              test({ a: 1, b: 2 });
                                                                              """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ab"));
    }
}
