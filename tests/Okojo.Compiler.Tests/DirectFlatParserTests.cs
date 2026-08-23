using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Tests;

public class DirectFlatParserTests
{
    [TestCase("1 + 2 * 3", 7)]
    [TestCase("(1 + 2) * 3", 9)]
    [TestCase("2 ** 3 ** 2", 512)]
    [TestCase("10 - 3 - 2", 5)]
    [TestCase("false || true && false ? 1 : 42", 42)]
    [TestCase("null ?? 42", 42)]
    public void CompileString_ExecutesDirectExpressionPrecedence(string expression, double expected)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile($"{expression};");

        realm.Execute(script);
        Assert.That(realm.Accumulator.FastNumberValue, Is.EqualTo(expected));
    }

    [Test]
    public void ParseScript_EmitsPostOrderFlatNodesDirectly()
    {
        using var ast = DirectFlatParser.ParseScript(
            """
            let x = 40;
            x += 2;
            x;
            """
        );

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        ref readonly var assignmentStatement = ref ast[statements[1]];
        ref readonly var assignment = ref ast[assignmentStatement.Arg0];

        Assert.That(root.Kind, Is.EqualTo(AstKind.Program));
        Assert.That(statements.Length, Is.EqualTo(3));
        Assert.That(assignment.Kind, Is.EqualTo(AstKind.AssignmentExpression));
        Assert.That(assignment.Arg0, Is.LessThan(assignmentStatement.Arg0));
        Assert.That(assignment.Arg1, Is.LessThan(assignmentStatement.Arg0));
        Assert.That(statements[2], Is.LessThan(ast.Root));
    }

    [Test]
    public void CompileString_ExecutesDirectFlatLoop()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            """
            let sum = 0;
            for (let i = 0; i < 10; i++) {
                if (i === 2) continue;
                if (i === 7) break;
                sum += i;
            }
            sum;
            """
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(19));
    }

    [Test]
    public void CompileString_ExecutesNestedFunctionCaptureFromDirectArena()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            """
            let x = 41;
            function answer() {
                return x + 1;
            }
            answer;
            """
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Obj, Is.AssignableTo<JsFunction>());
        var result = realm.InvokeFunction(
            (JsFunction)realm.Accumulator.Obj!,
            JsValue.Undefined,
            ReadOnlySpan<JsValue>.Empty
        );
        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ParseScript_AllocatesLessThanClassParseAndLowerBridge()
    {
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 80).Select(static i => $"let value{i} = {i}; value{i} += 1;")
        );

        using (DirectFlatParser.ParseScript(source)) { }
        using (FlatAstLowerer.Lower(JavaScriptParser.ParseScript(source))) { }

        var directBytes = MeasureAllocatedBytes(() =>
        {
            using var ast = DirectFlatParser.ParseScript(source);
        });
        var bridgeBytes = MeasureAllocatedBytes(() =>
        {
            using var ast = FlatAstLowerer.Lower(JavaScriptParser.ParseScript(source));
        });

        TestContext.Out.WriteLine($"direct={directBytes:N0} bytes bridge={bridgeBytes:N0} bytes");
        Assert.That(directBytes, Is.LessThan(bridgeBytes));
    }

    [Test]
    public void ParseScript_RejectsUnsupportedSyntaxWithoutClassParserFallback()
    {
        var exception = Assert.Throws<JsParseException>(() =>
            DirectFlatParser.ParseScript("answer();")
        );

        Assert.That(exception!.Message, Does.Contain("DirectFlatParser"));
    }

    [TestCase("return 1;")]
    [TestCase("break;")]
    [TestCase("continue;")]
    [TestCase("while (true) { function nested() { break; } }")]
    public void ParseScript_RejectsIllegalAbruptControl(string source)
    {
        Assert.Throws<JsParseException>(() => DirectFlatParser.ParseScript(source));
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
