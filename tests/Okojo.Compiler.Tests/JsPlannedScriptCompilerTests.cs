using Okojo.Bytecode;
using Okojo.Compiler.Experimental;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler.Tests;

public class JsPlannedScriptCompilerTests
{
    [Test]
    public void Compile_ExecutesLocalOnlyLetAndAddProgram()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 41;
                                                                   x + 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_UsesRealBytecodeAndRegisterMetadata()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var a = 1;
                                                                   const b = 2;
                                                                   a + b;
                                                                   """));

        Assert.That(script.Bytecode.Length, Is.GreaterThan(0));
        Assert.That(script.RegisterCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(script.Bytecode.Contains((byte)JsOpCode.Return), Is.True);
    }

    [Test]
    public void Compile_RejectsUnsupportedStatements_WithoutTouchingJsCompiler()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var ex = Assert.Throws<NotSupportedException>(() => compiler.Compile(JavaScriptParser.ParseScript("""
            while (true) 1;
            """)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("does not support statement"));
    }

    [Test]
    public void Compile_ExecutesBlockScopedLexicals()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   {
                                                                       let y = 40;
                                                                       x = y + 2;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIdentifierAssignmentExpression()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   x = x + 41;
                                                                   x;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIfWithComparison()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   if (x < 2) {
                                                                       x = 42;
                                                                   } else {
                                                                       x = 0;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesCompoundAssignment()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 40;
                                                                   x += 3;
                                                                   x -= 1;
                                                                   x;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_EmitsNoCaptureFunctionDeclaration()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function answer() {
                                                                       return 42;
                                                                   }
                                                                   answer;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void Compile_ExecutesFunctionDeclaration_CapturingRootLexical()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 41;
                                                                   function answer() {
                                                                       return x + 1;
                                                                   }
                                                                   answer;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Obj, Is.AssignableTo<JsFunction>());
        var fn = (JsFunction)realm.Accumulator.Obj!;
        var result = realm.InvokeFunction(fn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }
}
