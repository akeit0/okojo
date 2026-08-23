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
    public void CompileString_ExecutesForInEnumerationAndControl()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function read(object) {
                let result = '';
                let assigned = '';
                let closures = [];
                for (const key in object) {
                    closures.push(() => key);
                    if (key === 'skip') continue;
                    assigned = key;
                    result += key;
                    if (key === 'stop') break;
                }
                let bare;
                for (bare in { tail: 1 }) {}
                let nullishCount = 0;
                for (var ignored in null) nullishCount++;
                let inherited = '';
                let child = Object.create({ inherited: 1 });
                child.own = 1;
                for (let property in child) inherited += property + ',';
                let pattern = '';
                for (const [first] in { ab: 1, cd: 2 }) pattern += first;
                var varPattern = '';
                for (var [varFirst] in { xy: 1 }) varPattern = varFirst;
                return result + '|' + assigned + '|' + bare + '|'
                    + closures[0]() + ',' + closures[1]() + ',' + closures[2]()
                    + '|' + inherited + '|' + nullishCount + '|' + pattern + '|' + varPattern;
            }
            read({ first: 1, skip: 2, stop: 3, after: 4 });
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("firststop|stop|tail|first,skip,stop|own,inherited,|0|ac|x")
        );
    }

    [Test]
    public void CompileAst_ExecutesForInEnumeration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let result = ''; for (const [key] in { ab: 1, cd: 2 }) result += key; result;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("ac"));
    }

    [TestCase("for (let first, second in {}) {}")]
    [TestCase("for (let value = 1 in {}) {}")]
    public void ParseScript_RejectsUnsupportedOrInvalidForInHeads(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

    [Test]
    public void CompileString_ExecutesForOfWithIteratorClose()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let closes = 0;
            function iterable(throwOnClose = false) {
                return {
                    [Symbol.iterator]() {
                        let value = 0;
                        return {
                            next() { value++; return { value: [value], done: value > 4 }; },
                            return() { closes++; if (throwOnClose) throw 'close'; return {}; }
                        };
                    }
                };
            }
            let result = '';
            let reads = [];
            for (const [value] of iterable()) {
                reads.push(() => value);
                if (value === 2) continue;
                result += value;
                if (value === 3) break;
            }
            function first(values) { for (const value of values) return value; }
            let returned = first(iterable());
            let thrown = '';
            try { for (const value of iterable(true)) throw 'body'; }
            catch (error) { thrown = error; }
            let closeError = '';
            try { for (const value of iterable(true)) break; }
            catch (error) { closeError = error; }
            for (const value of iterable()) { if (value[0] < 0) break; }
            let finallyFlow = '';
            for (const value of [[1], [2]]) {
                try { finallyFlow += value[0]; continue; }
                finally { finallyFlow += 'f'; }
            }
            for (const value of iterable()) {
                try { finallyFlow += 'b'; break; }
                finally { finallyFlow += 'f'; }
            }
            result + '|' + reads[0]() + ',' + reads[1]() + ',' + reads[2]()
                + '|' + returned[0] + '|' + thrown + '|' + closeError
                + '|' + finallyFlow + '|' + closes;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("13|1,2,3|1|body|close|1f2fbf|5"));
    }

    [Test]
    public void CompileAst_ExecutesForOfEnumeration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let result = ''; for (const value of [1, 2, 3]) result += value; result;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("123"));
    }

    [Test]
    public void CompileString_AssignsIterationValuesToMemberTargets()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let targets = [{}, {}];
            let baseCalls = 0;
            let keyCalls = 0;
            function base(index) { baseCalls++; return targets[index]; }
            function key() { keyCalls++; return 'value'; }
            for (base(0)[key()] in { first: 1, second: 2 }) {}
            for (base(1)[key()] of [3, 4]) {}
            let named = {};
            for (named.last of [1, 2]) {}
            let closes = 0;
            let error = '';
            let iterable = {
                [Symbol.iterator]() {
                    return {
                        next() { return { value: 5, done: false }; },
                        return() { closes++; return {}; }
                    };
                }
            };
            try {
                for (base(1)[(() => { throw 'key'; })()] of iterable) {}
            } catch (caught) {
                error = caught;
            }
            targets[0].value + '|' + targets[1].value + '|' + named.last
                + '|' + baseCalls + '|' + keyCalls + '|' + closes + '|' + error;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("second|4|2|5|4|1|key"));
    }

    [Test]
    public void CompileString_EmitsDebuggerOpcodeAndContinuesWithoutHook()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            "let value = 1; debugger; value += 1; value;"
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
        Assert.That(script.Bytecode, Does.Contain((byte)JsOpCode.Debugger));
    }

    [Test]
    public void CompileString_ExecutesLabeledControlAcrossFinallyAndForOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let innerCloses = 0;
            let outerCloses = 0;
            function iterable(values, outer) {
                return {
                    [Symbol.iterator]() {
                        let index = 0;
                        return {
                            next() { return index < values.length
                                ? { value: values[index++], done: false }
                                : { done: true }; },
                            return() { outer ? outerCloses++ : innerCloses++; return {}; }
                        };
                    }
                };
            }
            let result = '';
            outer: for (const value of iterable([1, 2, 3, 4], true)) {
                inner: for (const nested of iterable([value], false)) {
                    try {
                        if (value === 2) continue outer;
                        if (value === 3) break outer;
                        break inner;
                    } finally {
                        result += 'f';
                    }
                }
                result += value;
            }
            block: { result += 'b'; break block; result += 'x'; }
            first: second: for (let index = 0; index < 2; index++) {
                result += 'c';
                continue first;
            }
            result + '|' + innerCloses + '|' + outerCloses;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("f1ffbcc|3|1"));
    }

    [Test]
    public void CompileAst_ExecutesLabeledControl()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let result = ''; outer: for (const value of [1, 2, 3]) { if (value === 2) continue outer; result += value; } result;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("13"));
    }

    [TestCase("break missing;")]
    [TestCase("label: { continue label; }")]
    [TestCase("label: label: ;")]
    [TestCase("outer: while (true) { function nested() { break outer; } }")]
    public void ParseScript_RejectsInvalidLabeledControl(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

    [Test]
    public void CompileString_ExecutesOptionalChainsWithV8LinkSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let effects = '';
            function key() { effects += 'k'; return 'value'; }
            function argument() { effects += 'a'; return 2; }
            let object = {
                value: 3,
                method(value) { return this.value + value; },
                missing: undefined
            };
            let nullish = null;
            nullish?.[key()];
            nullish?.method(argument());
            nullish?.(argument());
            let named = object?.value;
            let computed = object?.[key()];
            let memberCall = object?.method(argument());
            let optionalMemberCall = object.method?.(argument());
            let spreadCall = object.method?.(...[argument()]);
            let optionalDirect = ((value) => value)?.(4);
            object.missing?.(argument());
            function Box() { this.value = 9; }
            let constructed = new ({ Box }?.Box)().value;
            let condition = nullish?.value ? 'bad' : 'ok';
            let skippedDelete = delete nullish?.[key()];
            let deleted = delete object?.value;
            let error = '';
            try { object?.missing.value; } catch (caught) { error = caught.name; }
            let boundaryError = '';
            try { (nullish?.value).missing; } catch (caught) { boundaryError = caught.name; }
            named + '|' + computed + '|' + memberCall + '|' + optionalMemberCall
                + '|' + spreadCall + '|' + optionalDirect + '|' + constructed + '|' + condition
                + '|' + skippedDelete + '|' + deleted + '|' + error
                + '|' + boundaryError + '|' + effects + '|' + ('value' in object);
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("3|3|5|5|5|4|9|ok|true|true|TypeError|TypeError|kaaa|false")
        );
    }

    [TestCase("target?.value = 1;")]
    [TestCase("target?.value++;")]
    [TestCase("++target?.value;")]
    [TestCase("new target?.value();")]
    public void ParseScript_RejectsInvalidOptionalChainForms(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

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
    public void ParseScript_StoresArrowPatternAndRestParameterMetadata()
    {
        using var ast = FlatJavaScriptParser.ParseScript(
            "let arrow = (first, { item: [value = 1, ...rest] }, ...tail) => value;"
        );
        ref readonly var root = ref ast[ast.Root];
        var declaration = ast[ast.ChildRange(root.Arg0, root.Arg1)[0]];
        var declarator = ast[ast.ChildRange(declaration.Arg0, declaration.Arg1)[0]];
        var expression = ast[declarator.Arg2];
        var function = ast.GetFunction(expression.Arg0);
        var parameters = ast.GetParameters(function);
        using var collected = CompilerBindingCollector.Collect(ast);

        Assert.That(expression.Kind, Is.EqualTo(AstKind.ArrowFunctionExpression));
        Assert.That(function.IsArrow, Is.True);
        Assert.That(function.FunctionLength, Is.EqualTo(2));
        Assert.That(function.RestParameterIndex, Is.EqualTo(2));
        Assert.That(function.HasSimpleParameterList, Is.False);
        Assert.That(parameters.Length, Is.EqualTo(3));
        Assert.That(parameters[1].Kind, Is.EqualTo(JsFormalParameterBindingKind.Pattern));
        Assert.That(ast[parameters[1].PatternNode].Kind, Is.EqualTo(AstKind.ObjectExpression));
        Assert.That(parameters[2].Kind, Is.EqualTo(JsFormalParameterBindingKind.Rest));
        Assert.That(
            collected.Bindings.ToArray().Select(binding => binding.Name),
            Does.Contain("value").And.Contain("rest").And.Contain("tail")
        );
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
    public void CompileString_InfersAnonymousFunctionNames()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let declared = function () {};
            let assigned;
            assigned = function () {};
            let [arrayDefault = function () {}] = [];
            let objectDefault;
            ({ missing: objectDefault = function () {} } = {});
            function defaults(value = function () {}) { return value.name; }
            let key = 'computed';
            let symbol = Symbol('symbolic');
            let object = {
                method: function () {},
                [key]: function () {},
                [symbol]: function () {},
                explicit: function named() {}
            };
            let member = {};
            member.property = function () {};
            let parenthesized;
            (parenthesized) = function () {};
            let inferred = function () { inferred = 1; return inferred; };
            let inferredName = inferred.name;
            let invoke = inferred;
            let outerWrite = invoke();
            declared.name + '|' + assigned.name + '|' + arrayDefault.name + '|'
                + objectDefault.name + '|' + defaults() + '|' + object.method.name + '|'
                + object[key].name + '|' + object[symbol].name + '|' + object.explicit.name + '|'
                + member.property.name + '|' + parenthesized.name + '|' + inferredName + '|'
                + outerWrite;
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo(
                "declared|assigned|arrayDefault|objectDefault|value|method|computed|[symbolic]|named|||inferred|1"
            )
        );
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
    public void ParseScript_StoresSwitchCasesInDenseFlatRanges()
    {
        using var ast = FlatJavaScriptParser.ParseScript(
            "switch (value) { case 1: value = 2; break; default: value = 3; }"
        );

        ref readonly var root = ref ast[ast.Root];
        var statement = ast[ast.ChildRange(root.Arg0, root.Arg1)[0]];
        var cases = ast.ChildRange(statement.Arg1, statement.Arg2);

        Assert.That(statement.Kind, Is.EqualTo(AstKind.SwitchStatement));
        Assert.That(cases.Length, Is.EqualTo(2));
        Assert.That(ast[cases[0]].Kind, Is.EqualTo(AstKind.SwitchCase));
        Assert.That(ast[cases[0]].Arg0, Is.GreaterThanOrEqualTo(0));
        Assert.That(ast[cases[1]].Arg0, Is.EqualTo(-1));
        Assert.That(ast.ChildRange(ast[cases[0]].Arg1, ast[cases[0]].Arg2).Length, Is.EqualTo(2));
    }

    [Test]
    public void CompileString_ExecutesSwitchSelectionFallthroughAndBreak()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function choose(value) {
                let result = 0;
                switch (value) {
                    case 1:
                        return 10;
                    default:
                        result = 20;
                    case 2:
                        result += 2;
                        break;
                    case 3:
                        result = 30;
                }
                return result;
            }
            choose(1) * 1000 + choose(2) * 100 + choose(3) * 10 + choose(9);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10522));
    }

    [Test]
    public void CompileString_SharesSwitchLexicalScopeAndRoutesBreakThroughFinally()
    {
        const string makeSource = """
            function make(value) {
                let read;
                switch (value) {
                    case 0:
                        let shared = 42;
                    case 1:
                        read = function () { return shared; };
                        break;
                }
                return read;
            }
            """;
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile(makeSource + "make(0)();"));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));

        var tdzRealm = JsRuntime.Create().DefaultRealm;
        Assert.Throws<JsRuntimeException>(() =>
            tdzRealm.Execute(
                new JsPlannedScriptCompiler(tdzRealm).Compile(makeSource + "make(1)();")
            )
        );

        var finallyRealm = JsRuntime.Create().DefaultRealm;
        var finallyScript = new JsPlannedScriptCompiler(finallyRealm).Compile(
            """
            let iterations = 0;
            let effects = 0;
            while (iterations < 2) {
                switch (iterations) {
                    case 0:
                        break;
                    default:
                        try { break; } finally { effects += 40; }
                }
                iterations++;
            }
            iterations + effects;
            """
        );
        finallyRealm.Execute(finallyScript);
        Assert.That(finallyRealm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [TestCase("switch (0) { default: break; default: break; }")]
    [TestCase("switch (0) { case 0:")]
    [TestCase("switch (0) { case 0: continue; }")]
    public void ParseScript_RejectsMalformedSwitch(string source)
    {
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));
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
    public void CompileString_ExecutesArrayAndObjectLiteralSpread()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let symbol = Symbol('copied');
            let array = [0, ...[1, 2], , 4];
            let object = {
                before: 1,
                ...{ before: 9, copied: 2, [symbol]: 4 },
                copied: 3
            };
            array.length + '|' + array[0] + array[1] + array[2] + '|'
                + (typeof array[3]) + '|' + array[4] + '|'
                + object.before + object.copied + object[symbol];
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("5|012|undefined|4|934"));
    }

    [Test]
    public void CompileString_EvaluatesLiteralSpreadInOrderWithoutArrayFunctionNamesOrSetters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Evaluate("globalThis.__flatLiteralSpreadOrder = [];");
        var iterable = realm.Evaluate(
            "(function* () { __flatLiteralSpreadOrder.push('i'); yield 1; __flatLiteralSpreadOrder.push('j'); yield 2; })()"
        );
        var source = realm.Evaluate(
            "({ get value() { __flatLiteralSpreadOrder.push('g'); return 4; } })"
        );
        var later = realm.Evaluate(
            "(function () { __flatLiteralSpreadOrder.push('l'); return 3; })"
        );
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function build(iterable, source, later) {
                let array = [...iterable, later(), function () {}];
                let object = { ...source, tail: later() };
                return [array, object];
            }
            build;
            """
        );
        realm.Execute(script);
        var build = (JsFunction)realm.Accumulator.Obj!;

        var result = realm.InvokeFunction(build, JsValue.Undefined, [iterable, source, later]);
        var inspect = (JsFunction)
            realm
                .Evaluate(
                    "(function (result) { return result[0].length + '|' + result[0][0] + result[0][1] + result[0][2] + '|' + result[0][3].name + '|' + result[1].value + result[1].tail; })"
                )
                .Obj!;
        var summary = realm.InvokeFunction(inspect, JsValue.Undefined, [result]);

        Assert.That(summary.AsString(), Is.EqualTo("4|123||43"));
        Assert.That(
            realm.Evaluate("__flatLiteralSpreadOrder.join('')").AsString(),
            Is.EqualTo("ijlgl")
        );

        var setterRealm = JsRuntime.Create().DefaultRealm;
        setterRealm.Evaluate(
            "Object.defineProperty(Array.prototype, '0', { set: function () { throw new Error('prototype setter'); }, configurable: true });"
        );
        try
        {
            setterRealm.Execute(
                new JsPlannedScriptCompiler(setterRealm).Compile("[...[], 42][0];")
            );
            Assert.That(setterRealm.Accumulator.Int32Value, Is.EqualTo(42));
        }
        finally
        {
            setterRealm.Evaluate("delete Array.prototype[0];");
        }
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

    [TestCase("new.target;")]
    [TestCase("let read = () => new.target;")]
    public void ParseScript_RejectsNewTargetWithoutReceiverFunction(string source)
    {
        var exception = Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript(source)
        );

        Assert.That(exception!.Message, Does.Contain("new.target"));
    }

    [Test]
    public void CompileString_ExecutesLexicalNewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function make(value = new.target) {
                return [value, () => new.target, () => new.target.name];
            }
            let direct = make();
            let constructed = new make();
            (direct[0] === undefined) + '|' + (direct[1]() === undefined)
                + '|' + (constructed[0] === make) + '|' + (constructed[1]().name === 'make')
                + '|' + (constructed[2]() === 'make');
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|true|true|true"));
        var make = script
            .ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(static function => function.Name == "make");
        var arrows = make
            .Script.ObjectConstants.OfType<JsBytecodeFunction>()
            .Where(static function => function.IsArrow)
            .ToArray();
        Assert.That(make.HasNewTarget, Is.True);
        Assert.That(arrows, Has.Length.EqualTo(2));
        Assert.That(arrows.All(static function => function.HasNewTarget), Is.True);
    }

    [Test]
    public void CompileAst_ExecutesNewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "function read() { return new.target; } new read() === read;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ParseScript_RejectsEscapedNewTargetMetaProperty() =>
        Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("function read() { return new.\\u0074arget; }")
        );

    [Test]
    public void CompileString_ExecutesObjectMethodsAndAccessors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let order = '';
            let bias = 1;
            function key() { order += 'k'; return 'computed'; }
            let object = {
                base: 2,
                method(value) { order += 'm'; return this.base + value + bias; },
                [key()](value) { order += 'c'; return this.base * value; },
                get value() { order += 'g'; return this.base; },
                set value(value) { order += 's'; this.base = value; }
            };
            object.value = 4;
            let total = object.method(1) + object.computed(2) + object.value;
            let descriptor = Object.getOwnPropertyDescriptor(object, 'value');
            let constructible = 1;
            try { new object.method(); } catch (error) { constructible = 0; }
            total + '|' + object.method.name + '|' + object.computed.name + '|'
                + descriptor.get.name + '|' + descriptor.set.name + '|'
                + (typeof object.method.prototype) + '|' + constructible + '|' + order;
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("18|method|computed|get value|set value|undefined|0|ksmcg")
        );
    }

    [Test]
    public void CompileString_ExecutesComputedAndIndexedObjectAccessorsInOrder()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let order = '';
            let stored = 0;
            function key() { order += 'k'; return 'item'; }
            let object = {
                get [key()]() { order += 'g'; return stored; },
                set [key()](value) { order += 's'; stored = value; },
                get 0() { return 7; }
            };
            object.item = 4;
            let descriptor = Object.getOwnPropertyDescriptor(object, 'item');
            object.item + object[0] + '|' + descriptor.get.name + '|'
                + descriptor.set.name + '|' + order;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("11|get item|set item|kksg"));
    }

    [Test]
    public void CompileString_ExecutesRegExpAndBigIntLiterals()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function make() { return /x/g; }
            let first = make();
            first.lastIndex = 1;
            let second = make();
            let expression = /a[b\/]+c/gi;
            let amount = 9007199254740993n + 7n;
            expression.test('xxaB/cyy') + '|' + (first !== second) + '|'
                + second.lastIndex + '|' + amount.toString();
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|0|9007199254741000"));
    }

    [Test]
    public void CompileString_ExecutesNestedTemplateLiteralsInSourceOrder()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let order = '';
            function value(text) {
                order += text;
                return { toString() { order += 't'; return text; } };
            }
            let result = `a${value('x')}b${value('y')}c`;
            let nested = `n${{ value: `i${2}` }.value}`;
            let tricky = `${"}"}|${/}/.test('}')}|${({ a: 1 }).a}|${(1, 2)}`;
            let comment = `${1 /* } */ + 1}`;
            let cooked = `line\n\u{1F600}`;
            result + '|' + nested + '|' + order + '|' + tricky + '|'
                + comment + '|' + (cooked === 'line\n😀');
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("axbyc|ni2|xtyt|}|true|1|2|2|true"));
    }

    [Test]
    public void ParseScript_RejectsInvalidUntaggedTemplateEscape() =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript("`bad\\8`;"));

    [Test]
    public void ParseScript_RejectsTaggedTemplateAfterOptionalChain() =>
        Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("({ tag() {} })?.tag`x`;")
        );

    [Test]
    public void CompileString_ExecutesTaggedTemplatesWithCachedSiteIdentityAndV8Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let order = '';
            function value(text) { order += text; return text; }
            let holder = {
                prefix: 'R',
                get tag() {
                    order += 'g';
                    return function(strings, first, second) {
                        order += 't';
                        return this.prefix + strings[0] + first + strings[1]
                            + second + strings[2];
                    };
                }
            };
            function run() { return holder.tag`a\n${value('x')}b${value('y')}c`; }
            let first = run();
            let second = run();
            function capture(strings) { return strings; }
            function sameSite() { return capture`same`; }
            let site1 = sameSite();
            let site2 = sameSite();
            let site3 = capture`same`;
            let invalid = capture`bad\8`;
            let escaped = capture`line\n`;
            let optionalHolder = { prefix: 'P', tag(strings) { return this.prefix + strings[0]; } };
            let optionalTagged = (optionalHolder?.tag)`z`;
            (first === 'Ra\nxbyc') + '|' + order + '|' + (first === second)
                + '|' + (site1 === site2) + '|' + Object.isFrozen(site1)
                + '|' + Object.isFrozen(site1.raw) + '|' + (site1 === site3)
                + '|' + (invalid[0] === undefined) + '|' + invalid.raw[0]
                + '|' + (escaped[0] === 'line\n') + '|' + (escaped.raw[0] === 'line\\n')
                + '|' + optionalTagged;
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("true|gxytgxyt|true|true|true|true|false|true|bad\\8|true|true|Pz")
        );
    }

    [Test]
    public void CompileAst_ExecutesTaggedTemplateBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "function tag(strings, value) { return strings[0] + value + strings.raw[1]; } tag`a${1}b`;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a1b"));
    }

    [Test]
    public void CompileString_ExecutesGeneratorsAndAbruptResumeModes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let log = '';
            function* sequence(value = (log += 'p', 2)) {
                let current = value;
                try {
                    current += yield current;
                    yield current;
                    return current + 1;
                } finally {
                    log += 'f';
                }
            }
            let iterator = sequence();
            let before = log;
            let first = iterator.next();
            let second = iterator.next(3);
            let third = iterator.next();

            function* close() {
                try { yield 1; } finally { log += 'r'; }
            }
            let closing = close();
            closing.next();
            let returned = closing.return(9);

            function* caught() {
                try { yield 1; } catch (error) { return error; } finally { log += 't'; }
            }
            let throwing = caught();
            throwing.next();
            let thrown = throwing.throw(7);

            function* nestedExpression() { return 10 + (yield 2) + (yield 3); }
            let nested = nestedExpression();
            nested.next();
            nested.next(4);
            let nestedResult = nested.next(5);

            function capturedFactory() {
                let captured = 1;
                return function* () { captured += yield captured; return captured; };
            }
            let captured = capturedFactory()();
            captured.next();
            let capturedResult = captured.next(4);

            function* source() {
                try { yield 1; yield 2; } finally { log += 'i'; }
            }
            function* loop() { for (const value of source()) yield value; }
            let looping = loop();
            looping.next();
            let loopReturned = looping.return(8);

            before + '|' + first.value + '|' + first.done + '|' + second.value + '|'
                + second.done + '|' + third.value + '|' + third.done + '|'
                + returned.value + '|' + returned.done + '|' + thrown.value + '|'
                + thrown.done + '|' + nestedResult.value + '|' + nestedResult.done + '|'
                + capturedResult.value + '|' + capturedResult.done + '|'
                + loopReturned.value + '|' + loopReturned.done + '|' + log;
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("p|2|false|5|false|6|true|9|true|7|true|19|true|5|true|8|true|pfrti")
        );
        var sequence = script
            .ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(static function => function.Name == "sequence");
        Assert.That(sequence.Kind, Is.EqualTo(JsBytecodeFunctionKind.Generator));
        Assert.That(sequence.HasEagerGeneratorParameterBinding, Is.True);
        Assert.That(sequence.Script.Bytecode, Does.Contain((byte)JsOpCode.SwitchOnGeneratorState));
        Assert.That(sequence.Script.Bytecode, Does.Contain((byte)JsOpCode.SuspendGenerator));
        Assert.That(sequence.Script.Bytecode, Does.Contain((byte)JsOpCode.ResumeGenerator));
        Assert.That(sequence.Script.GeneratorSwitchTargets, Has.Length.EqualTo(3));
    }

    [Test]
    public void CompileAst_ExecutesGeneratorBridge()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            JavaScriptParser.ParseScript(
                "let make = function* () { yield* [1, 2]; }; let iterator = make(); iterator.next().value + '|' + iterator.next().value + '|' + iterator.next().done;"
            )
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1|2|true"));
    }

    [Test]
    public void CompileString_ExecutesYieldDelegateResumeModes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let log = '';
            function* inner() {
                try {
                    let sent = yield 1;
                    yield sent;
                    return 3;
                } finally { log += 'f'; }
            }
            function* outer() { return (yield* inner()) + 1; }
            let normal = outer();
            let first = normal.next();
            let second = normal.next(2);
            let third = normal.next();

            function* innerReturn() {
                try { yield 1; } finally { log += 'r'; }
            }
            function* outerReturn() { return yield* innerReturn(); }
            let returning = outerReturn();
            returning.next();
            let returned = returning.return(9);

            function* innerThrow() {
                try { yield 1; } catch (error) { yield error; return 5; }
            }
            function* outerThrow() { return yield* innerThrow(); }
            let throwing = outerThrow();
            throwing.next();
            let thrown = throwing.throw(7);
            let throwDone = throwing.next();

            first.value + '|' + first.done + '|' + second.value + '|' + second.done
                + '|' + third.value + '|' + third.done + '|' + returned.value + '|'
                + returned.done + '|' + thrown.value + '|' + thrown.done + '|'
                + throwDone.value + '|' + throwDone.done + '|' + log;
            """
        );

        realm.Execute(script);

        Assert.That(
            realm.Accumulator.AsString(),
            Is.EqualTo("1|false|2|false|4|true|9|true|7|false|5|true|fr")
        );
    }

    [Test]
    public void ParseScript_RejectsYieldDelegateWithoutOperand() =>
        Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("function* invalid() { yield*; }")
        );

    [TestCase("function* invalid(value = yield 1) {}")]
    [TestCase("function* invalid() { let arrow = () => yield 1; }")]
    public void ParseScript_RejectsYieldOutsideGeneratorBody(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

    [Test]
    public void CompileString_ExecutesArrowsWithLexicalThisAndArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function outer(value) {
                let expression = add => this.base + value + add + arguments[0];
                let block = (left, right) => { return left * right; };
                let empty = () => 1;
                let defaulted = (amount = 7) => amount;
                let nested = left => right => left + right;
                let patterns = ({ item: [first = 1, ...rest] }, ...tail) =>
                    first + rest.length + tail.length;
                return expression.call({ base: 100 }, 2)
                    + block(3, 4) + empty() + defaulted() + nested(5)(6)
                    + patterns({ item: [3, 4, 5] }, 6, 7);
            }
            let arrow = value => value;
            let constructRejected = false;
            try { new arrow(1); }
            catch (error) { constructRejected = error instanceof TypeError; }
            outer.call({ base: 10 }, 20) + '|' + arrow.name + '|' + constructRejected;
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("90|arrow|true"));
    }

    [TestCase("let arrow = (value, value) => value;")]
    [TestCase("let arrow = value\n=> value;")]
    [TestCase("let arrow = (value, ...rest, tail) => value;")]
    [TestCase("let arrow = (...rest,) => rest;")]
    [TestCase("let arrow = ((value)) => value;")]
    [TestCase("let arrow = ([...rest, value]) => value;")]
    [TestCase("let arrow = ({ ...rest, value }) => value;")]
    [TestCase("let arrow = (value += 1) => value;")]
    public void ParseScript_RejectsUnsupportedOrInvalidArrowHeads(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

    [TestCase("let value = { get item(value) {} };")]
    [TestCase("let value = { set item() {} };")]
    [TestCase("let value = { set item(...value) {} };")]
    [TestCase("let value = { method(value, value) {} };")]
    public void ParseScript_RejectsInvalidObjectMethodParameters(string source) =>
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(source));

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

    [Test]
    public void CompileString_CreatesMappedAndUnmappedArgumentsObjects()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function read(a, b) { return arguments.length * 100 + arguments[0] * 10 + arguments[1]; }
            function mapped(a) { arguments[0] = 42; return a; }
            function unmapped(a) { "use strict"; arguments[0] = 42; return a; }
            function defaulted(a = arguments[1]) { return a; }
            function noFormal() { return arguments[0]; }
            read(3, 4) + mapped(1) + unmapped(1) + defaulted(undefined, 42) + noFormal(10);
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(329));
        Assert.That(
            script
                .ObjectConstants.OfType<JsBytecodeFunction>()
                .Count(static function =>
                    function.Script.Bytecode.Contains((byte)JsOpCode.CreateMappedArguments)
                ),
            Is.EqualTo(5)
        );
    }

    [Test]
    public void CompileString_RespectsArgumentsShadowingAndNestedFunctionOwnership()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            function parameter(arguments) { return arguments; }
            function lexical() { let arguments = 20; return arguments; }
            function variable() { var arguments; return arguments.length; }
            function outer() { return function inner(value) { return arguments[0]; }; }
            """
        );

        realm.Execute(script);
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("parameter(10);"));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("lexical();"));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(20));
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("variable(1, 2);"));
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
        realm.Execute(
            new JsPlannedScriptCompiler(realm).Compile("var inner = outer(); inner(10);")
        );
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void CompileString_DeletesPropertiesAndEvaluatesNonReferencesOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            let object = { value: 1 };
            let effects = 0;
            delete object[(effects += 1, 'value')]
                && delete object.missing
                && delete (effects += 10)
                && effects === 11
                && typeof object.value === 'undefined';
            """
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CompileString_AppliesIdentifierAndStrictDeleteSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Execute(
            new JsPlannedScriptCompiler(realm).Compile(
                "var __plannedDeleteVar = 1; let __plannedDeleteLexical = 1; globalThis.__plannedDeleteTemp = 1;"
            )
        );

        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("delete __plannedDeleteVar;"));
        Assert.That(realm.Accumulator.IsFalse, Is.True);
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("delete __plannedDeleteLexical;"));
        Assert.That(realm.Accumulator.IsFalse, Is.True);
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("delete __plannedDeleteMissing;"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);
        realm.Execute(new JsPlannedScriptCompiler(realm).Compile("delete __plannedDeleteTemp;"));
        Assert.That(realm.Accumulator.IsTrue, Is.True);

        Assert.Throws<JsParseException>(() =>
            FlatJavaScriptParser.ParseScript("'use strict'; delete identifier;")
        );
        Assert.Throws<JsRuntimeException>(() =>
            realm.Execute(
                new JsPlannedScriptCompiler(realm).Compile(
                    "function fail() { 'use strict'; let value = {}; Object.defineProperty(value, 'x', { configurable: false }); return delete value.x; } fail();"
                )
            )
        );
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
