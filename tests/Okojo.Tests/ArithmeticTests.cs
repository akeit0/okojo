using System.Text;
using System.Text.RegularExpressions;
using Okojo.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ArithmeticTests
{
    [Test]
    public void TestSubSmi()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("1 / (-0 - 0)");
        Assert.That(result.FastFloat64Value, Is.EqualTo(double.NegativeInfinity));
    }

    [Test]
    public void TestMixedNumberArithmeticAfterInt32Overflow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function t() {
                let s = 2147483647;
                let i = 0;
                while (i < 3) {
                    s = s + i;
                    i = i + 1;
                }
                return s;
            }
            t();
            """
        );

        Assert.That(result.IsFloat64, Is.True);
        Assert.That(result.NumberValue, Is.EqualTo(2147483650d));
    }

    [Test]
    public void Add_Encodes_Single_Register_Operand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(a, b) {
                    return a + b;
                }
                t(1, 2);
                """
            )
        );

        var t = script
            .ObjectConstants.OfType<JsScript>()
            .Select(static instance => instance.CreateClosure())
            .Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(
            Regex.IsMatch(disasm, @"Add r\d+\r?$", RegexOptions.Multiline),
            "Add must carry only its register operand."
        );
    }

    [Test]
    public void AddSmi_Encodes_Single_Immediate_Operand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t() {
                    let x = 40;
                    x += 2;
                    return x;
                }
                t();
                """
            )
        );

        var t = script
            .ObjectConstants.OfType<JsScript>()
            .Select(static instance => instance.CreateClosure())
            .Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("AddSmi imm:2"));
    }

    [Test]
    public void Add_WithWideRegister_Executes_And_Decodes()
    {
        var source = new StringBuilder();
        source.AppendLine("function t() {");
        for (var i = 0; i < 300; i++)
            source.AppendLine($"  let v{i} = {i};");
        source.AppendLine("  return v299 + v0;");
        source.AppendLine("}");
        source.AppendLine("t();");

        var realm = JsRuntime.Create().DefaultRealm;
        var script = realm.CompileScript(source.ToString());

        realm.Execute(script);
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(299d));

        var t = script
            .ObjectConstants.OfType<JsScript>()
            .Select(static instance => instance.CreateClosure())
            .Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });
        Assert.That(disasm, Does.Not.Contain("<truncated>"));
    }
}
