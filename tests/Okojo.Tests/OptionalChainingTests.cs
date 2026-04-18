using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class OptionalChainingTests
{
    [Test]
    public void OptionalChain_ShortCircuits_Across_Following_Call_And_Member_Segments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const a = undefined;
                                                                   let x = 1;
                                                                   a?.[++x];
                                                                   a?.b.c(++x).d;
                                                                   undefined?.[++x];
                                                                   undefined?.b.c(++x).d;
                                                                   x === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void OptionalCall_ShortCircuits_And_Skips_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   let fn = undefined;
                                                                   fn?.(x++);
                                                                   x === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void OptionalMemberCall_ShortCircuits_On_Loaded_Callee_And_Skips_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   const obj = { method: undefined };
                                                                   obj.method?.(x++);
                                                                   x === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void OptionalMemberCall_Preserves_Receiver_And_Calls_When_Present()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const obj = {
                                                                     value: 3,
                                                                     method(arg) {
                                                                       return this.value + arg;
                                                                     }
                                                                   };
                                                                   obj.method?.(4) === 7;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void OptionalChain_Parses_After_NewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let seen = "unset";
                                                                   class C {
                                                                     constructor () {
                                                                       seen = new.target?.a;
                                                                     }
                                                                   }
                                                                   new C();
                                                                   seen === undefined;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void OptionalCall_IndirectEval_Uses_Global_Lexical_Environment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   const a = 'global';
                                                                   function f() {
                                                                     const a = 'local';
                                                                     return eval?.('a');
                                                                   }
                                                                   f() === 'global';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
