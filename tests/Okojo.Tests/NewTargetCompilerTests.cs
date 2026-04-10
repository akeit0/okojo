using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class NewTargetCompilerTests
{
    [Test]
    public void Compiler_Emits_Construct_For_NewExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function Foo() {}
                                                                   new Foo();
                                                                   """));

        Assert.That(Array.IndexOf(script.Bytecode, (byte)JsOpCode.Construct) >= 0, Is.True);
    }

    [Test]
    public void FunctionFlag_HasNewTarget_IsTrueOnlyWhenReferenced()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);

        var withNewTarget = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                          function A() { return new.target ? 1 : 0; }
                                                                          """));
        var a = withNewTarget.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "A");
        Assert.That(a.HasNewTarget, Is.True);

        var withoutNewTarget = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                             function B() { return 1; }
                                                                             """));
        var b = withoutNewTarget.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "B");
        Assert.That(b.HasNewTarget, Is.False);
    }

    [Test]
    public void NewTarget_Branches_Differently_For_New_And_Direct_Call()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let fromNew = 0;
                                                                   let fromCall = 0;
                                                                   function Foo() {
                                                                       if (!new.target) {
                                                                           fromCall = 100;
                                                                       } else {
                                                                           fromNew = 1;
                                                                       }
                                                                   }
                                                                   new Foo();
                                                                   Foo();
                                                                   fromNew + fromCall;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(100 + 1));
    }

    [Test]
    public void New_Constructor_Primitive_Return_Falls_Back_To_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function Foo() {
                                                                       this.x = 1;
                                                                       return 7;
                                                                   }
                                                                   let o = new Foo();
                                                                   o.x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void Unreachable_New_After_Return_IsNotEmitted_In_Function_Body()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function Foo() {
                                                                       if (!new.target) {
                                                                           return 1;
                                                                       } else {
                                                                           return 2;
                                                                       }
                                                                   }
                                                                   """));

        var foo = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "Foo");
        Assert.That(Array.IndexOf(foo.Script.Bytecode, (byte)JsOpCode.Construct), Is.EqualTo(-1));
    }

    //[Test]
    public void NewTarget_IfElse_Returns_DoesNotEmit_DeadJumpOrTrailingReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function Foo() {
                                                                       if (!new.target) {
                                                                           return 1;
                                                                       } else {
                                                                           return 2;
                                                                       }
                                                                   }
                                                                   """));

        var foo = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "Foo");
        var code = foo.Script.Bytecode;

        var returnCount = code.Count(b => b == (byte)JsOpCode.Return);
        var jumpCount = code.Count(b => b == (byte)JsOpCode.Jump);
        var constructCount = code.Count(b => b == (byte)JsOpCode.Construct);

        Assert.That(returnCount, Is.EqualTo(2));
        Assert.That(jumpCount, Is.EqualTo(0));
        Assert.That(constructCount, Is.EqualTo(0));
    }
}
