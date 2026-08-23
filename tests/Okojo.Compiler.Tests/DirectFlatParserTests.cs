using Okojo.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
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
        using var ast = FlatJavaScriptParser.ParseScript(
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
    public void ParseScript_StoresFunctionParametersInDenseParsingTables()
    {
        using var ast = FlatJavaScriptParser.ParseScript(
            "function add(left, right) { return left + right; }"
        );

        ref readonly var root = ref ast[ast.Root];
        var declaration = ast[ast.ChildRange(root.Arg0, root.Arg1)[0]];
        var function = ast.GetFunction(declaration.Arg0);
        var parameters = ast.GetParameters(function);

        Assert.That(ast.GetString(function.NameStringIndex), Is.EqualTo("add"));
        Assert.That(parameters.Length, Is.EqualTo(2));
        Assert.That(ast.GetString(parameters[0].NameStringIndex), Is.EqualTo("left"));
        Assert.That(ast.GetString(parameters[1].NameStringIndex), Is.EqualTo("right"));
        Assert.That(function.HasSimpleParameterList, Is.True);
    }

    [Test]
    public void ParseScript_StoresAdvancedFunctionParameterMetadataAndPatterns()
    {
        using var ast = FlatJavaScriptParser.ParseScript(
            "function read(a, { b }, c = 1, ...rest) { return b; }"
        );

        ref readonly var root = ref ast[ast.Root];
        var declaration = ast[ast.ChildRange(root.Arg0, root.Arg1)[0]];
        var function = ast.GetFunction(declaration.Arg0);
        var parameters = ast.GetParameters(function);

        Assert.That(function.FunctionLength, Is.EqualTo(2));
        Assert.That(function.RestParameterIndex, Is.EqualTo(3));
        Assert.That(function.HasSimpleParameterList, Is.False);
        Assert.That(parameters[1].Kind, Is.EqualTo(JsFormalParameterBindingKind.Pattern));
        Assert.That(ast[parameters[1].PatternNode].Kind, Is.EqualTo(AstKind.ObjectBindingPattern));
        Assert.That(parameters[2].InitializerNode, Is.GreaterThanOrEqualTo(0));
        Assert.That(parameters[3].Kind, Is.EqualTo(JsFormalParameterBindingKind.Rest));
    }

    [Test]
    public void ParseScript_StoresNamedFunctionExpressionInFlatTables()
    {
        const string source = "let fn = function self(value = 1) { return value; };";
        using var ast = FlatJavaScriptParser.ParseScript(source);

        ref readonly var root = ref ast[ast.Root];
        var declaration = ast[ast.ChildRange(root.Arg0, root.Arg1)[0]];
        var declarator = ast[ast.ChildRange(declaration.Arg0, declaration.Arg1)[0]];
        var expression = ast[declarator.Arg2];
        var function = ast.GetFunction(expression.Arg0);

        Assert.That(expression.Kind, Is.EqualTo(AstKind.FunctionExpression));
        Assert.That(ast.GetString(function.NameStringIndex), Is.EqualTo("self"));
        Assert.That(function.HasSimpleParameterList, Is.False);
        Assert.That(ast.GetPosition(declarator.Arg2), Is.EqualTo(source.IndexOf("function")));
    }

    [Test]
    public void CompileString_ExecutesAnonymousAndNamedFunctionExpressions()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let outer = 40;
            let anonymous = function (value = 2) { return outer + value; };
            let named = function self(value) { return value ? self(value - 1) + 1 : 0; };
            anonymous() + named(3);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(45));
    }

    [Test]
    public void CompileString_InitializesNamedFunctionSelfBeforeParameterDefaults()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let fn = function self(value = self) { return value; }; fn() === fn;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator, Is.EqualTo(JsValue.True));
    }

    [Test]
    public void CompileString_ExecutesThisExpressionFromMethodCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let object = { value: 42, read: function () { return this.value; } }; object.read();"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_ExecutesOrderedPatternDefaultAndRestParameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function read({ a = 1, b = 2, ...rest } = {}, [first, ...tail] = [3], value = a + b, ...extra) {
                return a + b + rest.c + first + tail.length + value + extra.length;
            }
            read({ c: 4 }, [5, 6], undefined, 7, 8);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(18));
    }

    [Test]
    public void CompileString_EnforcesParameterTdzAndCapturesPatternBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var captureScript = compiler.Compile(
            """
            function make([value] = [42]) {
                function read() { return value; }
                return read;
            }
            make()();
            """
        );
        var tdzScript = new JsPlannedScriptCompiler(realm).Compile(
            "function fail(first = second, second = 2) { return first; } fail;"
        );

        realm.Execute(captureScript);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
        realm.Execute(tdzScript);
        var fail = (JsFunction)realm.Accumulator.Obj!;
        Assert.Throws<JsRuntimeException>(() =>
            realm.InvokeFunction(fail, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty)
        );
    }

    [Test]
    public void CompileString_ParameterInitializerSkipsBodyVarBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let value = 42; function read(result = value) { var value = 1; return result; } read();"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_ExecutesRestPatternParameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function sum(...[first, second, ...tail]) { return first + second + tail.length; } sum;"
        );

        realm.Execute(script);
        var sum = (JsFunction)realm.Accumulator.Obj!;
        var result = realm.InvokeFunction(sum, JsValue.Undefined, [20, 21, 0]);

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_UsesLastSloppyDuplicateSimpleParameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile("function last(value, value) { return value; } last(1, 42);");

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_ClosesParameterPatternIteratorWhenDefaultThrows()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatParameterIteratorClosed = 0;");
        var iterable = realm.Evaluate(
            """
            ({
                [Symbol.iterator]() {
                    return {
                        next() { return { value: undefined, done: false }; },
                        return() {
                            __flatParameterIteratorClosed++;
                            return { done: true };
                        }
                    };
                }
            })
            """
        );
        var fail = realm.Evaluate("(function () { throw new Error('boom'); })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(fail, [value = fail()]) { return value; } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        Assert.Throws<JsRuntimeException>(() =>
            realm.InvokeFunction(run, JsValue.Undefined, [fail, iterable])
        );
        Assert.That(realm.Evaluate("__flatParameterIteratorClosed").Int32Value, Is.EqualTo(1));
    }

    [TestCase("function invalid(a, a = 1) {}")]
    [TestCase("function invalid({ a }, a) {}")]
    [TestCase("function invalid(a = 1) { 'use strict'; }")]
    [TestCase("'use strict'; function invalid(a, a) {}")]
    public void ParseScript_RejectsInvalidNonSimpleParameterLists(string source)
    {
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));
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
    public void CompileString_EmitsLogicalConditionsInTestMode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function choose(a, b, c) {
                if ((123, a && (b || !c))) return 1;
                while (a ? b : c) {
                    if (b) break;
                    c = 0;
                }
                return 0;
            }
            choose(true, false, false) * 100
                + choose(true, false, true) * 10
                + choose(false, true, false);
            """
        );

        realm.Execute(script);

        var choose = script.ObjectConstants.OfType<JsBytecodeFunction>().Single();
        var disassembly = Disassembler.Dump(choose.Script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(100));
        Assert.That(disassembly, Does.Not.Contain(nameof(JsOpCode.LogicalNot)));
        Assert.That(disassembly, Does.Not.Contain("LdaSmi 123"));
        Assert.That(disassembly, Does.Contain(nameof(JsOpCode.JumpIfToBooleanTrue)));
    }

    [Test]
    public void CompileString_CreatesFreshCapturedBindingForEachLoopIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function captureLoop(offset) {
                let first;
                let second;
                let third;
                for (let i = 0; i < 4; i++) {
                    function read() {
                        return offset + i;
                    }
                    if (i === 0) {
                        first = read;
                        continue;
                    }
                    if (i === 1) second = read;
                    if (i === 2) {
                        third = read;
                        break;
                    }
                }
                return first() * 100 + second() * 10 + third();
            }
            captureLoop(10);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1122));
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
    public void CompileString_ExecutesDirectCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile(
            "function add(left, right) { return left + right; } add(40, 2);"
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_ExecutesWideDirectCallOperands()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var arguments = string.Join(", ", Enumerable.Range(0, 260).Select(static i => i));

        var script = compiler.Compile(
            $"function first(value) {{ return value; }} first({arguments});"
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(0));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Wide));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.CallUndefinedReceiver));
    }

    [Test]
    public void CompileString_ExecutesMemberCallAndLoadsWithReceiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function read(target, key) { return target.add(2) + target[key]; } read;"
        );
        realm.Execute(script);
        var read = (JsFunction)realm.Accumulator.Obj!;
        var target = realm.Evaluate("({ value: 40, add(n) { return this.value + n; } })");

        var result = realm.InvokeFunction(read, JsValue.Undefined, [target, "value"]);

        Assert.That(result.Int32Value, Is.EqualTo(82));
    }

    [Test]
    public void CompileString_ExecutesArrayLiteralWithHoleAndDynamicElement()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);

        var script = compiler.Compile("let values = [1, 2 + 3, , 4]; values.length + values[1];");

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void CompileString_ExecutesNestedArrayBindingsWithDefaultsElisionsAndRest()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let [first, , third = 3, ...rest] = [1, 2, undefined, 4, 5];
            let [ignored, [nested = 6]] = [0, []];
            first * 100 + third * 10 + rest.length + nested;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(138));
    }

    [Test]
    public void CompileString_StoresArrayBindingBeforeNextIteratorStepAndClosesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatArrayBindingClosed = 0;");
        var makeIterable = realm.Evaluate(
            """
            (function (readFirst) {
                let step = 0;
                return {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                step++;
                                if (step === 1) return { value: 4, done: false };
                                if (step === 2) return { value: readFirst(), done: false };
                                return { value: 9, done: false };
                            },
                            return() {
                                __flatArrayBindingClosed++;
                                return { done: true };
                            }
                        };
                    }
                };
            })
            """
        );
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function run(makeIterable) {
                function readFirst() { return first; }
                let [first, second] = makeIterable(readFirst);
                return first * 10 + second;
            }
            run;
            """
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        var result = realm.InvokeFunction(run, JsValue.Undefined, [makeIterable]);

        Assert.That(result.Int32Value, Is.EqualTo(44));
        Assert.That(realm.Evaluate("__flatArrayBindingClosed").Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CompileString_ClosesArrayBindingIteratorWhenDefaultThrows()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatArrayBindingAbruptClose = 0;");
        var iterable = realm.Evaluate(
            """
            ({
                [Symbol.iterator]() {
                    return {
                        next() { return { value: undefined, done: false }; },
                        return() {
                            __flatArrayBindingAbruptClose++;
                            return { done: true };
                        }
                    };
                }
            })
            """
        );
        var fail = realm.Evaluate("(function () { throw new Error('boom'); })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(source, fail) { let [value = fail()] = source; return value; } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        Assert.Throws<JsRuntimeException>(() =>
            realm.InvokeFunction(run, JsValue.Undefined, [iterable, fail])
        );
        Assert.That(realm.Evaluate("__flatArrayBindingAbruptClose").Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CompileString_CreatesFreshCapturedArrayBindingForEachLoopIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let first, second;
            for (let [i] = [0]; i < 2; i++) {
                function read() { return i; }
                if (i === 0) first = read;
                else second = read;
            }
            first() * 10 + second();
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CompileString_ExecutesWideArrayBindingRegisters()
    {
        var names = string.Join(", ", Enumerable.Range(0, 260).Select(static i => $"value{i}"));
        var values = string.Join(", ", Enumerable.Range(0, 260));
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile($"let [{names}] = [{values}]; value259;");

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(259));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Wide));
    }

    [Test]
    public void CompileString_LoadsUnshadowedUndefinedIntrinsicAndPrefersLocalBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var intrinsicScript = new JsPlannedScriptCompiler(realm).Compile("undefined;");
        var shadowedScript = new JsPlannedScriptCompiler(realm).Compile(
            "function read() { let undefined = 42; return undefined; } read();"
        );

        realm.Execute(intrinsicScript);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
        realm.Execute(shadowedScript);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_ExecutesNestedObjectBindingsWithComputedDefaultsAndRest()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let key = 'b';
            let source = { a: 1, b: undefined, c: 3, d: 4, 2: 5, nested: { x: 6 } };
            let { a: first, [key]: second = 7, c, 2: numeric, nested: { x }, ...rest } = source;
            let { length } = 'abc';
            first * 100 + second * 10 + c + numeric + x + rest.d + length;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(191));
    }

    [Test]
    public void CompileString_EvaluatesObjectBindingKeyDefaultAndNextPropertyInOrder()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let order = 0;
            let { [order = order + 1]: value = (order = order + 10), next: after = order } = {};
            order * 100 + value * 10 + after;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1221));
    }

    [Test]
    public void CompileString_RejectsNullishObjectBindingBeforeComputedKeyEffect()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatObjectBindingTouched = 0;");
        var touch = realm.Evaluate(
            "(function () { __flatObjectBindingTouched++; return 'value'; })"
        );
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(source, touch) { let { [touch()]: value } = source; return value; } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        Assert.Throws<JsRuntimeException>(() =>
            realm.InvokeFunction(run, JsValue.Undefined, [JsValue.Null, touch])
        );
        Assert.That(realm.Evaluate("__flatObjectBindingTouched").Int32Value, Is.Zero);
    }

    [Test]
    public void CompileString_CreatesFreshCapturedObjectBindingForEachLoopIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let first, second;
            for (let { value: i } = { value: 0 }; i < 2; i++) {
                function read() { return i; }
                if (i === 0) first = read;
                else second = read;
            }
            first() * 10 + second();
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CompileString_PreservesAndExcludesSymbolKeysInObjectBindingRest()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var symbol = realm.Evaluate("Symbol('flat-object-binding')");
        realm.Global["__flatObjectBindingSymbol"] = symbol;
        var source = realm.Evaluate("({ keep: 2, [__flatObjectBindingSymbol]: 9 })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function run(source, symbol) {
                let { [symbol]: removed, ...rest } = source;
                return removed * 100 + rest.keep * 10 + (rest[symbol] === undefined ? 1 : 0);
            }
            run;
            """
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        var result = realm.InvokeFunction(run, JsValue.Undefined, [source, symbol]);

        Assert.That(result.Int32Value, Is.EqualTo(921));
    }

    [Test]
    public void CompileString_ExecutesWideObjectBindingRegisters()
    {
        var properties = string.Join(
            ", ",
            Enumerable.Range(0, 260).Select(static i => $"p{i}: {i}")
        );
        var names = string.Join(", ", Enumerable.Range(0, 260).Select(static i => $"p{i}"));
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            $"let source = {{ {properties} }}; let {{ {names} }} = source; p259;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(259));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Wide));
    }

    [Test]
    public void CompileString_ExecutesNestedDestructuringAssignmentsAndReturnsOriginalSources()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            let first, second, nested, tail, rest;
            let target = {};
            let arrayKey = 'slot';
            let sourceKey = 'b';
            let arraySource = [undefined, 3, [4], 5, 6];
            let objectSource = { second: undefined, b: 7, nested: { x: 8 }, extra: 9 };
            let arrayResult = ([first = 1, target[arrayKey], [nested], ...tail] = arraySource);
            let objectResult = ({ second = 2, [sourceKey]: target.value, nested: { x: target.deep }, ...rest } = objectSource);
            arrayResult === arraySource && objectResult === objectSource
                ? first + second + nested + target.slot + target.value + target.deep + tail.length + rest.extra
                : -1;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(36));
    }

    [Test]
    public void CompileString_PreparesArrayAssignmentMemberBeforeIteratorStepAndClosesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate(
            """
            globalThis.__flatAssignmentOrder = '';
            globalThis.__flatAssignmentTarget = {};
            """
        );
        var source = realm.Evaluate(
            """
            ({
                [Symbol.iterator]() {
                    __flatAssignmentOrder += 'i';
                    return {
                        next() {
                            __flatAssignmentOrder += 's';
                            return { value: 42, done: false };
                        },
                        return() {
                            __flatAssignmentOrder += 'c';
                            return { done: true };
                        }
                    };
                }
            })
            """
        );
        var receiver = realm.Evaluate(
            "(function () { __flatAssignmentOrder += 'r'; return __flatAssignmentTarget; })"
        );
        var key = realm.Evaluate("(function () { __flatAssignmentOrder += 'k'; return 'value'; })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(source, receiver, key) { ([receiver()[key()]] = source); } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        realm.InvokeFunction(run, JsValue.Undefined, [source, receiver, key]);

        Assert.That(realm.Evaluate("__flatAssignmentOrder").AsString(), Is.EqualTo("irksc"));
        Assert.That(realm.Evaluate("__flatAssignmentTarget.value").Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_PreparesObjectAssignmentMemberBeforeSourceLoad()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate(
            """
            globalThis.__flatObjectAssignmentOrder = '';
            globalThis.__flatObjectAssignmentTarget = {};
            """
        );
        var source = realm.Evaluate(
            "({ get value() { __flatObjectAssignmentOrder += 's'; return 42; } })"
        );
        var receiver = realm.Evaluate(
            "(function () { __flatObjectAssignmentOrder += 'r'; return __flatObjectAssignmentTarget; })"
        );
        var key = realm.Evaluate(
            "(function () { __flatObjectAssignmentOrder += 'k'; return 'value'; })"
        );
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(source, receiver, key) { ({ value: receiver()[key()] } = source); } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        realm.InvokeFunction(run, JsValue.Undefined, [source, receiver, key]);

        Assert.That(realm.Evaluate("__flatObjectAssignmentOrder").AsString(), Is.EqualTo("rks"));
        Assert.That(
            realm.Evaluate("__flatObjectAssignmentTarget.value").Int32Value,
            Is.EqualTo(42)
        );
    }

    [Test]
    public void CompileString_ClosesArrayAssignmentIteratorWhenDefaultThrows()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatArrayAssignmentClosed = 0;");
        var iterable = realm.Evaluate(
            """
            ({
                [Symbol.iterator]() {
                    return {
                        next() { return { value: undefined, done: false }; },
                        return() {
                            __flatArrayAssignmentClosed++;
                            return { done: true };
                        }
                    };
                }
            })
            """
        );
        var fail = realm.Evaluate("(function () { throw new Error('boom'); })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(source, fail) { let value; ([value = fail()] = source); } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        Assert.Throws<JsRuntimeException>(() =>
            realm.InvokeFunction(run, JsValue.Undefined, [iterable, fail])
        );
        Assert.That(realm.Evaluate("__flatArrayAssignmentClosed").Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CompileString_ExecutesWideDestructuringAssignmentRegisters()
    {
        var declarations = string.Join(", ", Enumerable.Range(0, 260).Select(static i => $"v{i}"));
        var values = string.Join(", ", Enumerable.Range(0, 260));
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile($"let {declarations}; [{declarations}] = [{values}]; v259;");

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(259));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Wide));
    }

    [Test]
    public void ParseScript_RejectsTrailingCommaAfterObjectRestBinding()
    {
        var exception = Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("let { ...rest, } = source;")
        );

        Assert.That(exception!.Message, Does.Contain("Rest binding"));
    }

    [Test]
    public void CompileString_ExecutesObjectLiteralPropertyShapes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function make(value, key) { return { first: 1, [key]: value, second: value + 1, first: 4, value, 2: 5, 'quoted': 6 }; } let result = make(40, 'dynamic'); result.first + result.dynamic + result.second + result.value + result[2] + result.quoted;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(136));
    }

    [Test]
    public void CompileString_NormalizesComputedObjectKeyBeforeValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let order = 0; let result = { [(order = order + 1)]: (order = order + 1), after: order }; order * 100 + result[1] * 10 + result.after;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(222));
    }

    [Test]
    public void CompileString_ExecutesMemberAssignmentCompoundAndUpdate()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let target = { value: 0 }; let key = 'value'; target[key] = 1; let old = target[key]++; target.value += 40; old + target.value;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(43));
    }

    [Test]
    public void CompileString_EvaluatesCompoundMemberKeyOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let count = 0; let target = { value: 1 }; target[(count = count + 1, 'value')] += 41; count * 100 + target.value;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(142));
    }

    [Test]
    public void CompileString_ExecutesLogicalMemberAssignmentsWithShortCircuiting()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "let keys = 0; let values = 0; let target = { truthy: 1, falsy: 0, nullish: null, defined: 2 }; target[(keys = keys + 1, 'truthy')] ||= (values = values + 1); target.falsy ||= (values = values + 1); target.truthy &&= (values = values + 1); let result = target.defined ??= (values = values + 1); target.nullish ??= (values = values + 1); keys * 10000 + values * 1000 + target.truthy * 100 + target.falsy * 10 + target.nullish + result;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(13215));
    }

    [Test]
    public void CompileString_ConstructsAfterEvaluatingCalleeBeforeArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function Box(value) {
                return { value };
            }
            let order = 0;
            function factory() {
                order = order * 10 + 1;
                return Box;
            }
            function argument() {
                order = order * 10 + 2;
                return 42;
            }
            function ConstructorFactory() {
                return Box;
            }
            let result = new (factory())(argument());
            let empty = new Box;
            let nested = new new ConstructorFactory()(8);
            order * 100 + result.value + nested.value + (empty.value === void 0 ? 0 : 10000);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1250));
    }

    [Test]
    public void CompileString_EmitsWideConstructOperands()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var arguments = string.Join(", ", Enumerable.Range(0, 260));
        var script = compiler.Compile(
            $"function First(value) {{ return {{ value }}; }} let result = new First({arguments}); result.value;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.Zero);
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Wide));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Construct));
    }

    [Test]
    public void CompileString_ExecutesSpreadCallsMembersAndConstruction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            """
            function collect(a, b, c) {
                return a * 100 + b * 10 + c;
            }
            function Box(a, b) {
                return { value: a * 10 + b };
            }
            let values = [1, 2];
            let direct = collect(...values, 3);
            let member = [1].concat(...[[2, 3]]).length;
            let constructed = new Box(...[4, 2]).value;
            direct + member + constructed;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(168));
    }

    [Test]
    public void CompileString_MaterializesSpreadBeforeEvaluatingFollowingArgument()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatSpreadOrder = [];");
        var iterable = realm.Evaluate("(function* () { __flatSpreadOrder.push(1); yield 0; })()");
        var target = realm.Evaluate("(function () { return __flatSpreadOrder.join(''); })");
        var later = realm.Evaluate("(function () { __flatSpreadOrder.push(2); return 0; })");
        var compiler = new JsPlannedScriptCompiler(realm);
        var script = compiler.Compile(
            "function run(target, iterable, later) { return target(...iterable, later()); } run;"
        );
        realm.Execute(script);
        var run = (JsFunction)realm.Accumulator.Obj!;

        var result = realm.InvokeFunction(run, JsValue.Undefined, [target, iterable, later]);

        Assert.That(result.AsString(), Is.EqualTo("12"));
    }

    [Test]
    public void ParseScript_AllocatesLessThanClassParseAndLowerBridge()
    {
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 80).Select(static i => $"let value{i} = {i}; value{i} += 1;")
        );

        using (FlatJavaScriptParser.ParseScript(source)) { }
        using (FlatAstLowerer.Lower(JavaScriptParser.ParseScript(source))) { }

        var directBytes = MeasureAllocatedBytes(() =>
        {
            using var ast = FlatJavaScriptParser.ParseScript(source);
        });
        var bridgeBytes = MeasureAllocatedBytes(() =>
        {
            using var ast = FlatAstLowerer.Lower(JavaScriptParser.ParseScript(source));
        });

        TestContext.Out.WriteLine($"direct={directBytes:N0} bytes bridge={bridgeBytes:N0} bytes");
        Assert.That(directBytes, Is.LessThan(bridgeBytes));
    }

    [Test]
    public void ParseScript_RejectsNewTargetWithoutClassParserFallback()
    {
        var exception = Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("new.target;")
        );

        Assert.That(exception!.Message, Does.Contain("FlatJavaScriptParser"));
    }

    [Test]
    public void ParseScript_RejectsUnsupportedObjectMethod()
    {
        var exception = Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("let value = { method() {} };")
        );

        Assert.That(exception!.Message, Does.Contain("methods and accessors"));
    }

    [TestCase("return 1;")]
    [TestCase("break;")]
    [TestCase("continue;")]
    [TestCase("while (true) { function nested() { break; } }")]
    public void ParseScript_RejectsIllegalAbruptControl(string source)
    {
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));
    }

    [Test]
    public void CompileString_ReplaysAbruptCompletionsAfterFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function run(mode) {
                let total = 0;
                for (let i = 0; i < 3; i++) {
                    try {
                        if (mode === 0) return total + 1;
                        if (mode === 1) break;
                        if (mode === 2) continue;
                        throw 4;
                    } catch (error) {
                        total += error;
                    } finally {
                        total += 10;
                    }
                    total += 100;
                }
                return total;
            }
            run(0) * 1000000 + run(1) * 10000 + run(2) * 100 + run(3);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1103342));
    }

    [Test]
    public void CompileString_RestoresHandlerContextAndAllowsFinallyOverride()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let cleanup = 0;
            function override() {
                try { return 1; } finally { return 2; }
            }
            function captured() {
                let read;
                try {
                    { let value = 40; read = function () { return value; }; throw 2; }
                } catch (error) {
                    let value = 100;
                    return read() + error;
                } finally {
                    cleanup++;
                }
            }
            let nestedCleanup = 0;
            function nested() {
                try {
                    try { return 3; } finally { nestedCleanup += 10; }
                } finally {
                    nestedCleanup += 100;
                }
            }
            override() * 1000000 + captured() * 10000 + cleanup * 1000 + nested() * 100 + nestedCleanup;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2421410));
    }

    [Test]
    public void CompileString_ExecutesOptionalAndDestructuredCatchBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let optional = 0;
            try { throw 1; } catch { optional = 42; }
            let destructured = 0;
            try { throw { left: 20, right: 22 }; }
            catch ({ left, right }) { destructured = left + right; }
            optional * 100 + destructured;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4242));
    }

    [Test]
    public void CompileString_PopsTryHandlerWhenLoopControlExitsTry()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function run() {
                while (true) {
                    try { break; } catch (error) { return 100; }
                }
                try { throw 42; } catch (error) { return error; }
            }
            run();
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_LoadsStoresUpdatesAndTypesGlobalBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatGlobal = 39;");
        var script = new JsPlannedScriptCompiler(realm).Compile(
            "__flatGlobal++; __flatGlobal += 2; typeof __flatMissing === 'undefined' ? __flatGlobal : 0;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void CompileString_AppliesSloppyAndStrictUnresolvableStoreRules()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("__flatSloppyCreated = 42;"));

        Assert.That(realm.Evaluate("__flatSloppyCreated").Int32Value, Is.EqualTo(42));
        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(
                new JsPlannedScriptCompiler(realm).Compile("'use strict'; __flatStrictMissing = 1;")
            )
        );
        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(new JsPlannedScriptCompiler(realm).Compile("__flatReadMissing;"))
        );
    }

    [Test]
    public void CompileString_HoistsFunctionDeclarationsAtScopeEntry()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let root = answer();
            function answer() { return 10; }
            function outer() {
                let before = inner();
                function inner() { return 1; }
                function inner() { return 20; }
                {
                    before += inside();
                    function inside() { return 3; }
                }
                return before;
            }
            function capture() {
                function read() { return late; }
                let result = read();
                var late = 42;
                return result === void 0;
            }
            root * 10000 + outer() * 10 + (capture() ? 1 : 0);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(100231));
    }

    [Test]
    public void CompileString_HoistsVarWithoutResettingParametersAtDeclarationSite()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function run(value) {
                value += 1;
                {
                    var value;
                    let local = 40;
                    var lifted = local + 1;
                }
                return value * 100 + lifted;
            }
            run(1);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(241));
    }

    [Test]
    public void Collect_LiftsAndMergesCompatibleVarBindings()
    {
        using var ast = FlatJavaScriptParser.ParseScript(
            "{ var value = 1; } var value; function value() { return 2; }"
        );
        using var collected = CompilerBindingCollector.Collect(ast);

        var binding = collected
            .Bindings.ToArray()
            .Single(static binding => binding.Name == "value");

        Assert.That(binding.ScopeId, Is.Zero);
        Assert.That(binding.Kind, Is.EqualTo(CompilerCollectedBindingKind.FunctionDeclaration));
    }

    [Test]
    public void CompileString_PersistsGlobalDeclarationsAcrossScripts()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var declarations = new JsPlannedScriptCompiler(realm).Compile(
            """
            var __plannedPersistentVar = 40;
            let __plannedPersistentLet = 2;
            const __plannedPersistentConst = 3;
            function __plannedPersistentRead() {
                return __plannedPersistentVar + __plannedPersistentLet + __plannedPersistentConst;
            }
            """
        );

        realm.Execute(declarations);
        realm.Execute(
            new JsPlannedScriptCompiler(realm).Compile(
                "__plannedPersistentVar += 1; __plannedPersistentLet += 1;"
            )
        );
        var read = new JsPlannedScriptCompiler(realm).Compile("__plannedPersistentRead();");
        realm.Execute(read);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(47));
        Assert.That(declarations.Bytecode, Does.Contain((byte)JsOpCode.StaGlobalInit));
        Assert.That(declarations.Bytecode, Does.Contain((byte)JsOpCode.StaGlobalFuncDecl));
        Assert.That(declarations.TopLevelLexicalAtoms, Has.Length.EqualTo(2));
        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(
                new JsPlannedScriptCompiler(realm).Compile("__plannedPersistentConst = 4;")
            )
        );
    }

    [Test]
    public void CompileString_RejectsGlobalDeclarationConflicts()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("let __plannedConflict = 1;"));

        Assert.Throws<JsRuntimeException>(() =>
            new JsPlannedScriptCompiler(realm).Compile("var __plannedConflict;")
        );
        Assert.Throws<JsRuntimeException>(() =>
            new JsPlannedScriptCompiler(realm).Compile("let __plannedConflict = 2;")
        );
        Assert.Throws<JsRuntimeException>(() =>
            new JsPlannedScriptCompiler(realm).Compile("let undefined;")
        );
        Assert.Throws<JsRuntimeException>(() =>
            new JsPlannedScriptCompiler(realm).Compile("let duplicate; const duplicate = 1;")
        );
    }

    [Test]
    public void CompileString_EnforcesLexicalTdzBeforeDeclaration()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(
                new JsPlannedScriptCompiler(realm).Compile(
                    "function read() { return value; let value = 1; } read();"
                )
            )
        );
        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(
                new JsPlannedScriptCompiler(realm).Compile(
                    "typeof __plannedLater; let __plannedLater = 1;"
                )
            )
        );
    }

    [TestCase("function run() { const value = 1; value = 2; } run();")]
    [TestCase("function run() { const value = 1; function write() { value++; } write(); } run();")]
    [TestCase("function run() { const value = 1; [value] = [2]; } run();")]
    public void CompileString_RejectsAssignmentToLocalAndCapturedConstBindings(string source)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(source);

        var error = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(error!.Message, Does.Contain("Assignment to constant variable"));
    }

    [Test]
    public void CompileString_AppliesNamedFunctionSelfAssignmentRules()
    {
        var sloppyRealm = JsRuntime.Create().DefaultRealm;
        var sloppy = new JsPlannedScriptCompiler(sloppyRealm).Compile(
            "(function named() { named = 1; return typeof named; })();"
        );
        sloppyRealm.Execute(sloppy);

        Assert.That(sloppyRealm.Accumulator.AsString(), Is.EqualTo("function"));

        var strictRealm = JsRuntime.Create().DefaultRealm;
        var strict = new JsPlannedScriptCompiler(strictRealm).Compile(
            """
            (function named() {
                "use strict";
                function write() { named = 1; }
                write();
            })();
            """
        );

        Assert.Throws<JsRuntimeException>(() => strictRealm.Execute(strict));
    }

    [TestCase("throw\n1;")]
    [TestCase("try {}")]
    [TestCase("try {} catch ({ value, value }) {}")]
    public void ParseScript_RejectsMalformedTryAndThrow(string source)
    {
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
