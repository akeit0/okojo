using System.Runtime.CompilerServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Tests;

public class JsPlannedScriptCompilerTests
{
    [Test]
    public void FlatAstLowerer_ProducesDensePostOrderNodes()
    {
        using var ast = FlatAstLowerer.Lower(
            JavaScriptParser.ParseScript(
                """
                let x = 41;
                x + 1;
                """
            )
        );

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        ref readonly var expressionStatement = ref ast[statements[1]];
        ref readonly var binary = ref ast[expressionStatement.Arg0];

        Assert.That(Unsafe.SizeOf<AstNode>(), Is.EqualTo(16));
        Assert.That(root.Kind, Is.EqualTo(AstKind.Program));
        Assert.That(statements.Length, Is.EqualTo(2));
        Assert.That(binary.Kind, Is.EqualTo(AstKind.BinaryExpression));
        Assert.That(binary.Arg0, Is.LessThan(expressionStatement.Arg0));
        Assert.That(binary.Arg1, Is.LessThan(expressionStatement.Arg0));
        Assert.That(expressionStatement.Arg0, Is.LessThan(statements[1]));
        Assert.That(statements[1], Is.LessThan(ast.Root));
    }

    [Test]
    public void Compile_ExecutesLocalOnlyLetAndAddProgram()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 41;
                x + 1;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
        Assert.That(script.Bytecode.Contains((byte)JsOpCode.AddSmi), Is.True);
    }

    [Test]
    public void Compile_UsesRealBytecodeAndRegisterMetadata()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                var a = 1;
                const b = 2;
                a + b;
                """
            )
        );

        Assert.That(script.Bytecode.Length, Is.GreaterThan(0));
        Assert.That(script.RegisterCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(script.TopLevelLexicalAtoms, Has.Length.EqualTo(1));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.StaGlobalInit));
        Assert.That(script.Bytecode.Contains((byte)JsOpCode.Return), Is.True);
    }

    [Test]
    public void Compile_RejectsUnsupportedStatements_WithoutTouchingJsCompiler()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var ex = Assert.Throws<NotSupportedException>(() =>
            compiler.Compile(
                JavaScriptParser.ParseScript(
                    """
                    debugger;
                    """
                )
            )
        );

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("does not support statement"));
    }

    [Test]
    public void Compile_LowersClassAstSwitchBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let result = 0; switch (2) { case 1: result = 1; break; case 2: result = 42; } result;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstTryCatchBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            JavaScriptParser.ParseScript("try { throw 42; } catch (error) { error; }")
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstObjectMethodAccessorBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let object = { base: 40, method() { return this.base + 2; }, get value() { return this.method(); } }; object.value;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_LowersClassAstRegExpBigIntBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let expression = /ok/i; let amount = 40n + 2n; expression.test('OK') && amount === 42n;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Compile_LowersClassAstTemplateBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let value = 40; let prefix = `answer`; let text = `${prefix}:${` ${value + 2}`}`; text;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("answer: 42"));
    }

    [Test]
    public void Compile_ExecutesBlockScopedLexicals()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                {
                    let y = 40;
                    x = y + 2;
                }
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIdentifierAssignmentExpression()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                x = x + 41;
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesIfWithComparison()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 1;
                if (x < 2) {
                    x = 42;
                } else {
                    x = 0;
                }
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_ExecutesCompoundAssignment()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 40;
                let delta = 2;
                x += delta;
                x -= delta;
                x += delta;
                x;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Compile_EmitsNoCaptureFunctionDeclaration()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                function answer() {
                    return 42;
                }
                answer;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void Compile_ExecutesFunctionDeclaration_CapturingRootLexical()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            JavaScriptParser.ParseScript(
                """
                let x = 41;
                function answer() {
                    return x + 1;
                }
                answer;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Obj, Is.AssignableTo<JsFunction>());
        var fn = (JsFunction)realm.Accumulator.Obj!;
        var result = realm.InvokeFunction(fn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }
}
