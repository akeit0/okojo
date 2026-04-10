using Okojo.Bytecode;
using Okojo.Compiler.Experimental;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler.Tests;

public class JsPlannedFunctionCompilerTests
{
    [Test]
    public void CompileFunction_ProducesBytecodeForParametersAndReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript("""
                                                   function sum(x, y) {
                                                       return x + y;
                                                   }
                                                   """);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("sum", plan, function.Body);

        Assert.That(compiled.Script.Bytecode.Length, Is.GreaterThan(0));
        Assert.That(compiled.Script.RegisterCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(compiled.Name, Is.EqualTo("sum"));
    }

    [Test]
    public void CompileFunction_EmitsComparisonAndBranchBytecode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript("""
                                                   function choose(x) {
                                                       if (x < 2) {
                                                           x += 40;
                                                       } else {
                                                           x = 0;
                                                       }
                                                       return x;
                                                   }
                                                   """);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("choose", plan, function.Body);

        Assert.That(compiled.Script.Bytecode.Contains((byte)JsOpCode.TestLessThan), Is.True);
        Assert.That(
            compiled.Script.Bytecode.Contains((byte)JsOpCode.JumpIfFalse) ||
            compiled.Script.Bytecode.Contains((byte)JsOpCode.JumpIfToBooleanFalse),
            Is.True);
        Assert.That(compiled.Script.Bytecode.Contains((byte)JsOpCode.Return), Is.True);
    }

    [Test]
    public void CompileFunction_ExecutesInnerFunction_CapturingParameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsPlannedFunctionCompiler(realm);
        var program = JavaScriptParser.ParseScript("""
                                                   function makeAdder(x) {
                                                       function addOne() {
                                                           return x + 1;
                                                       }
                                                       return addOne;
                                                   }
                                                   """);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("makeAdder", plan, function.Body);
        var closureValue = realm.InvokeFunction(compiled, JsValue.Undefined, [JsValue.FromInt32(41)]);
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
        var program = JavaScriptParser.ParseScript("""
                                                   function run() {
                                                       let x = 1;
                                                       function bump() {
                                                           x += 41;
                                                           return x;
                                                       }
                                                       return bump;
                                                   }
                                                   """);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("run", plan, function.Body);
        var closureValue = realm.InvokeFunction(compiled, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
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
        var program = JavaScriptParser.ParseScript("""
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
                                                   """);
        var function = (JsFunctionDeclaration)program.Statements[0];
        var plan = FunctionParameterPlan.FromFunction(function);

        var compiled = compiler.CompileFunction("make", plan, function.Body);
        var closureValue = realm.InvokeFunction(compiled, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
        Assert.That(closureValue.Obj, Is.AssignableTo<JsFunction>());
        var closure = (JsFunction)closureValue.Obj!;
        var result = realm.InvokeFunction(closure, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);

        Assert.That(result.Int32Value, Is.EqualTo(42));
    }
}
