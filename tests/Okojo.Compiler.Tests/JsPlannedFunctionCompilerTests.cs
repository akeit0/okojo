using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Tests;

public class JsPlannedFunctionCompilerTests
{
    [TestCase("x - y", 38)]
    [TestCase("x / y", 20)]
    [TestCase("x % 3", 1)]
    [TestCase("x << y", 160)]
    [TestCase("x > y ? x : y", 40)]
    [TestCase("(x = y, x + 1)", 3)]
    [TestCase("+(false || (x > y && true))", 1)]
    [TestCase("null ?? x", 40)]
    [TestCase("void x === void y ? 42 : 0", 42)]
    [TestCase("[x, , y][0] + [x, y].length", 42)]
    [TestCase("({ value: x, [y]: x + 1 }).value + ({ [y]: x })[y]", 80)]
    [TestCase("({ value: 1 }).value += x", 41)]
    [TestCase("({ value: 0 }).value ||= x", 40)]
    public void CompileFunction_ExecutesFlatExpressionFamilies(string expression, double expected)
    {
        var (realm, compiled) = CompileFunction(
            $"function evaluate(x, y) {{ return {expression}; }}"
        );

        var result = realm.InvokeFunction(
            compiled,
            JsValue.Undefined,
            [JsValue.FromInt32(40), JsValue.FromInt32(2)]
        );

        Assert.That(result.FastNumberValue, Is.EqualTo(expected));
    }

    [Test]
    public void CompileFunction_ExecutesStringLiteralAddition()
    {
        var (realm, compiled) = CompileFunction("function evaluate(y) { return \"answer\" + y; }");

        var result = realm.InvokeFunction(compiled, JsValue.Undefined, [JsValue.FromInt32(42)]);

        Assert.That(result.AsString(), Is.EqualTo("answer42"));
    }

    [Test]
    public void CompileFunction_ExecutesClassBridgeMemberCallAndComputedLoad()
    {
        var (realm, compiled) = CompileFunction(
            "function invoke(target, key) { return target.add(2) + target[key]; }"
        );
        var target = realm.Evaluate("({ value: 40, add(n) { return this.value + n; } })");

        var result = realm.InvokeFunction(compiled, JsValue.Undefined, [target, "value"]);

        Assert.That(result.Int32Value, Is.EqualTo(82));
    }

    [Test]
    public void CompileFunction_ExecutesCompoundAndShortCircuitAssignments()
    {
        var (realm, compiled) = CompileFunction(
            """
            function evaluate(x, y) {
                let value = x;
                value -= y;
                value *= 2;
                value /= 4;
                value **= 2;
                let logical = 0;
                logical ||= x;
                logical &&= y;
                let fallback = null;
                fallback ??= 40;
                return value + logical + fallback;
            }
            """
        );

        var result = realm.InvokeFunction(
            compiled,
            JsValue.Undefined,
            [JsValue.FromInt32(10), JsValue.FromInt32(2)]
        );

        Assert.That(result.FastNumberValue, Is.EqualTo(58));
    }

    [Test]
    public void CompileFunction_ExecutesFlatLoopsWithBreakAndContinue()
    {
        var (realm, compiled) = CompileFunction(
            """
            function evaluate(limit) {
                let sum = 0;
                for (let i = 0; i < limit; i++) {
                    if (i === 2) continue;
                    if (i === 7) break;
                    sum += i;
                }
                let n = 0;
                while (n < 2) {
                    sum += 10;
                    n++;
                }
                do {
                    sum += 1;
                    n--;
                } while (n > 0);
                return sum;
            }
            """
        );

        var result = realm.InvokeFunction(compiled, JsValue.Undefined, [JsValue.FromInt32(10)]);

        Assert.That(result.Int32Value, Is.EqualTo(41));
    }

    [Test]
    public void CompileScript_ExecutesClassBridgeSpreadCallsAndConstruction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function Box(a, b) {
                return { value: a * 10 + b };
            }
            function collect(a, b, c) {
                return a * 100 + b * 10 + c;
            }
            let values = [4, 2];
            let result = new Box(...values);
            result.value + collect(...[1, 2], 3);
            """
        );
        var script = compiler.Compile(program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(165));
    }

    [Test]
    public void CompileScript_ExecutesClassBridgeArrayBindingDeclaration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            let [first, , third = 3, ...rest] = [1, 2, undefined, 4, 5];
            first * 100 + third * 10 + rest.length;
            """
        );
        var script = compiler.Compile(program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(132));
    }

    [Test]
    public void CompileScript_ExecutesClassBridgeObjectBindingDeclaration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            let {} = {};
            let key = 'b';
            let source = { a: 1, b: undefined, c: 3, d: 4, 2: 5, nested: { x: 6 } };
            let { a: first, [key]: second = 7, c, 2: numeric, nested: { x }, ...rest } = source;
            let { length } = 'abc';
            first * 100 + second * 10 + c + numeric + x + rest.d + length;
            """
        );
        var script = compiler.Compile(program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(191));
    }

    [Test]
    public void CompileScript_ExecutesClassBridgeDestructuringAssignments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            let first, tail, rest;
            let target = {};
            let arraySource = [1, 2, 3];
            let objectSource = { value: 4, extra: 5 };
            let arrayResult = ([first, target.array, ...tail] = arraySource);
            let objectResult = ({ value: target.object, ...rest } = objectSource);
            arrayResult === arraySource && objectResult === objectSource
                ? first + target.array + tail.length + target.object + rest.extra
                : -1;
            """
        );
        var script = compiler.Compile(program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(13));
    }

    [Test]
    public void CompileFunction_ProducesBytecodeForParametersAndReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function sum(x, y) {
                return x + y;
            }
            """
        );
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("sum", plan, function.Body);

        Assert.That(compiled.Script.Bytecode.Length, Is.GreaterThan(0));
        Assert.That(compiled.Script.RegisterCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(compiled.Name, Is.EqualTo("sum"));
        Assert.That(
            realm
                .InvokeFunction(
                    compiled,
                    JsValue.Undefined,
                    [JsValue.FromInt32(40), JsValue.FromInt32(2)]
                )
                .Int32Value,
            Is.EqualTo(42)
        );
    }

    [Test]
    public void CompileFunction_EmitsComparisonAndBranchBytecode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function choose(x) {
                if (x < 2) {
                    x += 40;
                } else {
                    x = 0;
                }
                return x;
            }
            """
        );
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("choose", plan, function.Body);

        Assert.That(
            compiled.Script.Bytecode.Contains((byte)JsOpCode.TestLessThan)
                || compiled.Script.Bytecode.Contains((byte)JsOpCode.TestLessThanSmi),
            Is.True
        );
        Assert.That(
            compiled.Script.Bytecode.Contains((byte)JsOpCode.JumpIfFalse)
                || compiled.Script.Bytecode.Contains((byte)JsOpCode.JumpIfToBooleanFalse),
            Is.True
        );
        Assert.That(compiled.Script.Bytecode.Contains((byte)JsOpCode.Return), Is.True);
        Assert.That(
            realm.InvokeFunction(compiled, JsValue.Undefined, [JsValue.FromInt32(1)]).Int32Value,
            Is.EqualTo(41)
        );
        Assert.That(
            realm.InvokeFunction(compiled, JsValue.Undefined, [JsValue.FromInt32(3)]).Int32Value,
            Is.EqualTo(0)
        );
    }

    [Test]
    public void CompileFunction_ExecutesInnerFunction_CapturingParameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function makeAdder(x) {
                function addOne() {
                    return x + 1;
                }
                return addOne;
            }
            """
        );
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("makeAdder", plan, function.Body);
        var closureValue = realm.InvokeFunction(
            compiled,
            JsValue.Undefined,
            [JsValue.FromInt32(41)]
        );
        Assert.That(closureValue.Obj, Is.AssignableTo<JsFunction>());
        var closure = (JsFunction)closureValue.Obj!;
        var result = realm.InvokeFunction(closure, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileFunction_ExecutesInnerFunction_AssigningCapturedOuterLexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function run() {
                let x = 1;
                function bump() {
                    x += 41;
                    return x;
                }
                return bump;
            }
            """
        );
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("run", plan, function.Body);
        var closureValue = realm.InvokeFunction(
            compiled,
            JsValue.Undefined,
            ReadOnlySpan<JsValue>.Empty
        );
        Assert.That(closureValue.Obj, Is.AssignableTo<JsFunction>());
        var closure = (JsFunction)closureValue.Obj!;
        var result = realm.InvokeFunction(closure, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileFunction_ExecutesInnerFunction_CapturingBlockLexical()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(
            """
            function make() {
                let fn = 0;
                {
                    let x = 41;
                    function answer() {
                        return x + 1;
                    }
                    fn = answer;
                }
                return fn;
            }
            """
        );
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("make", plan, function.Body);
        var closureValue = realm.InvokeFunction(
            compiled,
            JsValue.Undefined,
            ReadOnlySpan<JsValue>.Empty
        );
        Assert.That(closureValue.Obj, Is.AssignableTo<JsFunction>());
        var closure = (JsFunction)closureValue.Obj!;
        var result = realm.InvokeFunction(closure, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    private static (JsRealm Realm, JsBytecodeFunction Compiled) CompileFunction(string source)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript(source);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);
        return (realm, compiler.CompileFunction(function.Name, plan, function.Body));
    }
}
