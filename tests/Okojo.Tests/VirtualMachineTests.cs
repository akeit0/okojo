using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

// Dont add tests here any more, add them to the appropriate test class (e.g. OkojoObjectTests, OkojoContextTests, etc.) instead. This class should only
public class VirtualMachineTests
{
    [Test]
    public void TestBasicAddition()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        // Bytecode for:
        // LdaSmi 1
        // Star r0
        // LdaSmi 2
        // Add r0
        // Return
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmi, 1,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.LdaSmi, 2,
                (byte)JsOpCode.Add, 0, 0, // reg 0, slot 0
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            1,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Float64Value, Is.EqualTo(3.0));
    }

    [Test]
    public void Mov_Does_Not_Clobber_Accumulator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmi, 7,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.LdaSmi, 9,
                (byte)JsOpCode.Mov, 0, 1,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            2,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void TestCompilerIntegration()
    {
        var program = JavaScriptParser.ParseScript("let x = 10; x + 5;");

        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(15.0));
    }

    [Test]
    public void TestLoopIntegration()
    {
        var source = @"
            function test() {
                let sum = 0;
                let i = 1;
                while (i < 11) {
                    sum = sum + i;
                    i = i + 1;
                }
                return sum;
            }
            test();
        ";
        var program = JavaScriptParser.ParseScript(source);

        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(55.0));
    }

    [Test]
    public void TestClosureCounter()
    {
        var source = @"
            function makeCounter() {
                let count = 0;
                return function() {
                    count = count + 1;
                    return count;
                };
            }
            let c = makeCounter();
            c(); // 1
            c(); // 2
            c(); // 3
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3.0));
    }

    [Test]
    public void TestRecursionFib()
    {
        var source = @"
            function fib(n) {
                if (n < 2) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(10);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        // fib(10) = 55
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(55.0));
    }

    [Test]
    public void TestFunctionExpressionBasic()
    {
        var source = @"
            let f = function () { return 1 + 2; };
            f();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3.0));
    }

    [Test]
    public void TestClosureCounterInstancesAreIndependent()
    {
        var source = @"
            function makeCounter() {
                let count = 0;
                return function () {
                    count = count + 1;
                    return count;
                };
            }
            let a = makeCounter();
            let b = makeCounter();
            a(); // 1
            a(); // 2
            b(); // 1 (must not share with a)
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1.0));
    }

    [Test]
    public void TestThrowsOnCapturedLexicalReadBeforeInitialization()
    {
        var source = @"
            function test() {
                let y = (function read() { return x; })();
                let x = 1;
                return y;
            }
            test();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void TestDontThrowsOnCapturedLexicalAfterInitialization()
    {
        var source = @"
            function test() {
                let y = (function read() { return x; });
                let x = 1;
                return y();
            }
            test();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        Assert.DoesNotThrow(() => realm.Execute(script));
    }

    [Test]
    public void TestVarReadBeforeInitializationReturnsUndefined()
    {
        var source = @"
            function t() {
                return x;
                var x = 1;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void TestLetRegisterReadBeforeInitializationThrows()
    {
        var source = @"
            function t() {
                return x;
                let x = 1;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void TestLetRegisterWriteBeforeInitializationThrows()
    {
        var source = @"
            function t() {
                x = 1;
                let x;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void TestLetDeclarationWithoutInitializerBecomesUndefinedAfterDeclaration()
    {
        var source = @"
            function t() {
                let x;
                return x;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void TestBlockScopedLetShadowingDoesNotOverwriteOuter()
    {
        var source = @"
            function t() {
                let x = 1;
                {
                    let x = 2;
                }
                return x;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void TestBlockScopedLetShadowingHasOwnTdz()
    {
        var source = @"
            function t() {
                let x = 1;
                {
                    return x;
                    let x = 2;
                }
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void TestClosureCapturesInnerBlockLetShadow()
    {
        var source = @"
            function t() {
                let x = 1;
                {
                    let x = 2;
                    return function () { return x; };
                }
            }
            let f = t();
            f();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(2));
    }

    [Test]
    public void TestClosureCapturesInnerBlockLetShadowTdz()
    {
        var source = @"
            function t() {
                let x = 1;
                {
                    let f = function () { return x; };
                    return f();
                    let x = 2;
                }
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void TestChildFunctionUsesParentContextSlotWhenChildHasOwnContext()
    {
        var source = @"
            function outer() {
                let x = 1;
                return function child() {
                    let y = 10;
                    function inner() { return y; }
                    x = x + 1;
                    return x + inner();
                };
            }
            let f = outer();
            f();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(12));
    }

    [Test]
    public void TestNonImmediateParentCaptureMutation()
    {
        var source = @"
            function outer() {
                let x = 1;
                function mid() {
                    return function inner() {
                        x = x + 1;
                        return x;
                    };
                }
                return mid();
            }
            let f = outer();
            f();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(2));
    }

    [Test]
    public void TestNonImmediateAndImmediateParentCapturesTogether()
    {
        var source = @"
            function outer() {
                let x = 1;
                return function mid() {
                    let y = 10;
                    function inner() { return x + y; }
                    return inner();
                };
            }
            outer()();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(11));
    }

    [Test]
    public void TestBitwiseOperatorsLowerAndExecute()
    {
        var source = @"
            function t(a, b) {
                return (a & b) | (a ^ b);
            }
            t(6, 3);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo((6 & 3) | (6 ^ 3)));
    }

    [Test]
    public void TestShiftOperatorsIncludingUnsignedRightShift()
    {
        var source = @"
            function t(a) {
                return (a >>> 1) + (a << 2) + (a >> 1);
            }
            t(8);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);
        var a = 8;
        var expected = (int)((uint)a >> 1) + (a << 2) + (a >> 1);
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(expected));
    }

    [Test]
    public void TestForLoopBasicSum()
    {
        var source = @"
            function t() {
                let s = 0;
                for (let i = 0; i < 3; i = i + 1) {
                    s = s + i;
                }
                return s;
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3));
    }


    [Test]
    public void TestForLoopFuncBasicSum()
    {
        var source = """
                     function functionCall() {
                         let identity = function (x) {
                             return x;
                         }

                         var s = 0;
                         for (var i = 0; i < 1000; i=i+1) {
                             s = identity(i)+1;
                         }

                         return s;
                     }
                     functionCall();
                     """;
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1000));
    }

    [Test]
    public void TestForLoopWithoutInitAndUpdate()
    {
        var source = @"
            function t() {
                let i = 0;
                let s = 0;
                for (; i < 3;) {
                    s = s + 1;
                    i = i + 1;
                }
                return s;
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3));
    }

    [Test]
    public void TestForLoopBreakAndContinue()
    {
        var source = @"
            function t() {
                let s = 0;
                for (let i = 0; i < 6; i = i + 1) {
                    if (i > 1) {
                        if (i < 3) continue;
                    }
                    if (i > 4) break;
                    s = s + i;
                }
                return s;
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(8)); // 0 + 1 + 3 + 4
    }

    [Test]
    public void TestNestedLoopsContinueTargetsCurrentLoop()
    {
        var source = @"
            function t() {
                let s = 0;
                for (let i = 0; i < 3; i = i + 1) {
                    let j = 0;
                    while (j < 3) {
                        j = j + 1;
                        if (j > 1) {
                            if (j < 3) continue;
                            break;
                        }
                        s = s + 1;
                    }
                }
                return s;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3));
    }

    [Test]
    public void TestUpdateExpressionsPrefixPostfix()
    {
        var source = @"
            function t() {
                let i = 1;
                let a = i++;
                let b = ++i;
                let c = i--;
                let d = --i;
                return i * 10000 + a * 1000 + b * 100 + c * 10 + d;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(11331));
    }

    [Test]
    public void TestForLoopUpdateExpressionIpp()
    {
        var source = @"
            function t() {
                let s = 0;
                for (let i = 0; i < 4; i++) {
                    s = s + i;
                }
                return s;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(6));
    }

    [Test]
    public void TestUpdateExpressionOnConstThrows()
    {
        var source = @"
            function t() {
                const x = 1;
                x++;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Does.Contain("constant"));
        Assert.That(ex.Message, Does.Contain("x"));
    }

    [Test]
    public void TestEqualityOperatorsBasic()
    {
        var source = @"
            function t() {
                let a = (1 == ""1"");
                let b = (1 != 2);
                let c = (1 === 1);
                let d = (1 !== 1);
                let s = 0;
                if (a) s = s + 1000;
                if (b) s = s + 100;
                if (c) s = s + 10;
                if (d) s = s + 1;
                return s;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1110));
    }

    [Test]
    public void TestEqualityNullUndefinedAndStrictDifference()
    {
        var source = @"
            function t() {
                let a = (null == undefined);
                let b = (null === undefined);
                let s = 0;
                if (a) s = s + 10;
                if (b) s = s + 1;
                return s;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(10));
    }

    [Test]
    public void TestEqualityBoolLooseCoercion()
    {
        var source = @"
            function t() {
                let a = (true == 1);
                let b = (false == 0);
                let c = (true === 1);
                let s = 0;
                if (a) s = s + 100;
                if (b) s = s + 10;
                if (c) s = s + 1;
                return s;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(110));
    }

    [Test]
    public void TestUnaryNegateAndUnaryPlus()
    {
        var source = @"
            function t(a) {
                return -a + +a;
            }
            t(3);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(0));
    }

    [Test]
    public void TestUnaryLogicalNot()
    {
        var source = @"
            function t(a) { return !a; }
            t(0);
        ";
        var program = JavaScriptParser.ParseScript(source);

        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.U, Is.EqualTo(JsValue.True.U));
    }

    [Test]
    public void TestUnaryBitwiseNot()
    {
        var source = @"
            function t(a) { return ~a; }
            t(3);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(~3));
    }

    [Test]
    public void TestSmiArithmeticExtensionsMulModExp()
    {
        var source = @"
            function t(a) {
                return (a * 3) + (a % 5) + (a ** 2);
            }
            t(4);
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(4 * 3 + 4 % 5 + Math.Pow(4, 2)));
    }

    [Test]
    public void TestObjectLiteralNamedPropertyRead()
    {
        var source = @"
            function t() {
                let o = { x: 1 };
                return o.x;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void TestObjectNamedPropertyWriteThenRead()
    {
        var source = @"
            function t() {
                let o = {};
                o.x = 1;
                return o.x;
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void TestObjectNamedPropertyAssignmentExpressionReturnsValue()
    {
        var source = @"
            function t() {
                let o = {};
                return (o.x = 7);
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        realm.Eval(source);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(7));
    }

    [Test]
    public void TestObjectLiteralMultiplePropertiesAndDuplicateKeyLastWins()
    {
        var source = @"
            function t() {
                let o = { x: 1, y: 2, x: 7 };
                return o.x + o.y;
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(9));
    }

    [Test]
    public void TestKeyedPropertyUintIndexGetSet()
    {
        var source = @"
            function t() {
                let o = {};
                o[0] = 3;
                return o[0];
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(3));
    }

    [Test]
    public void TestKeyedPropertyStringKeyGetSet()
    {
        var source = @"
            function t() {
                let o = {};
                let k = ""x"";
                o[k] = 4;
                return o[""x""];
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(4));
    }

    [Test]
    public void TestKeyedPropertyAssignmentExpressionReturnsValue()
    {
        var source = @"
            function t() {
                let o = {};
                return (o[1] = 9);
            }
            t();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(9));
    }

    [Test]
    public void TestObjectLiteralNumericLikeKeysUseIndexedSemantics()
    {
        var source = @"
            function a(){ let o = { 0: 3 }; return o[0]; }
            function b(){ let o = { ""0"": 4 }; return o[0]; }
            function c(){ let o = { [""0""]: 5 }; return o[0]; }
            a() + b() + c();
        ";
        var program = JavaScriptParser.ParseScript(source);
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(12));
    }

    [Test]
    public void TestThrowsOnConstReassignment()
    {
        var source = @"
            function t() {
                const x = 1;
                x = 2;
            }
            t();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval(source));
        Assert.That(ex!.Message, Does.Contain("constant"));
        Assert.That(ex.Message, Does.Contain("x"));
    }

    [Test]
    public void TestThrowsOnCapturedConstReassignment()
    {
        var source = @"
            function outer() {
                const x = 1;
                return function () { x = 2; };
            }
            let f = outer();
            f();
        ";
        var realm = JsRuntime.Create().DefaultRealm;

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Eval(source));
        Assert.That(ex!.Message, Does.Contain("constant"));
        Assert.That(ex.Message, Does.Contain("x"));
    }

    // Dont add tests here any more, add them to the appropriate test class (e.g. OkojoObjectTests, OkojoContextTests, etc.) instead. This class should only
}
