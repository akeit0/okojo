using System.Text;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ToolingTests
{
    [Test]
    public void Vm_Executes_JumpIfTrue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaTrue,
                (byte)JsOpCode.JumpIfTrue, 2, 0,
                (byte)JsOpCode.LdaSmi, 9,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Vm_Executes_JumpIfToBooleanFalse16()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaUndefined,
                (byte)JsOpCode.JumpIfToBooleanFalse, 2, 0,
                (byte)JsOpCode.LdaSmi, 9,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Compiler_Peephole_Folds_LdarStar_ToMov()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(a) { let b = a; return b; }
                                                                   t(7);
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.Mov), Is.True);
    }

    [Test]
    public void BytecodeBuilder_EmitTime_Peephole_Replaces_Consecutive_Loads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);
        builder.EmitLda(JsOpCode.LdaTheHole);
        builder.EmitLda(JsOpCode.LdaZero);
        builder.Emit(JsOpCode.Return);

        var script = builder.ToScript();

        Assert.That(script.Bytecode, Is.EqualTo(new[]
        {
            (byte)JsOpCode.LdaZero,
            (byte)JsOpCode.Return
        }));
    }

    [Test]
    public void BytecodeBuilder_EmitTime_Peephole_Omits_Star_Ldar_Same_Register()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);
        builder.EmitLda(JsOpCode.LdaSmi, 7);
        builder.Emit(JsOpCode.Star, 0);
        builder.EmitLda(JsOpCode.Ldar, 0);
        builder.Emit(JsOpCode.Return);

        var script = builder.ToScript();

        Assert.That(script.Bytecode, Is.EqualTo(new byte[]
        {
            (byte)JsOpCode.LdaSmi, 7,
            (byte)JsOpCode.Star, 0,
            (byte)JsOpCode.Return
        }));
    }

    [Test]
    public void BytecodeBuilder_EmitTime_Peephole_Preserves_Load_At_Anchored_Position()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);
        builder.EmitLda(JsOpCode.LdaTheHole);
        var label = builder.CreateLabel();
        builder.BindLabel(label);
        builder.EmitLda(JsOpCode.LdaZero);
        builder.Emit(JsOpCode.Return);

        var script = builder.ToScript();

        Assert.That(script.Bytecode, Is.EqualTo(new[]
        {
            (byte)JsOpCode.LdaTheHole,
            (byte)JsOpCode.LdaZero,
            (byte)JsOpCode.Return
        }));
    }

    [Test]
    public void Compiler_Uses_Mov_For_ArrayDestructuring_Source_Copy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(a) { let b; [b] = a; return b; }
                                                                   t([7]);
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.Mov), Is.True);
    }

    [Test]
    public void Compiler_Direct_Local_Call_Reuses_Local_Register()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(identity, x) {
                                                                       return identity(x);
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r0"));
        Assert.That(disasm, Does.Not.Contain("Ldar r0"));
        Assert.That(disasm, Does.Not.Contain("Star r2"));
        Assert.That(t.Script.RegisterCount, Is.EqualTo(2));
    }

    [Test]
    public void Compiler_Member_Call_Reuses_Contiguous_Local_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(obj, x) {
                                                                       return obj.f(x);
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("CallProperty func:r2, obj:r0, args:r1.., argc:1"));
        Assert.That(t.Script.RegisterCount, Is.EqualTo(3));
    }

    [Test]
    public void Compiler_Chained_Require_Declarators_Store_String_Arguments_Before_Call()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(require) {
                                                                       var React = require("react"),
                                                                           Scheduler = require("scheduler");
                                                                       return [React, Scheduler];
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("LdaStringConstant str:0"));
        Assert.That(disasm, Does.Contain("Star r3"));
        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r0, args:r3.., argc:1"));
        Assert.That(disasm, Does.Contain("LdaStringConstant str:1"));
        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r0, args:r3.., argc:1"));
    }

    [Test]
    public void Compiler_Local_Assignment_Reloads_From_Target_Without_Preserve_Temp()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(x) {
                                                                       let s = 0;
                                                                       s = x + 1;
                                                                       return s;
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Not.Contain("Star r2"));
        Assert.That(t.Script.RegisterCount, Is.EqualTo(2));
    }

    [Test]
    public void Compiler_For_Let_Init_Does_Not_Leave_Dead_Empty_Completion_Load()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       for (let i = 0; i < 10000; i = i + 1) {
                                                                       }
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var bytecode = t.Script.Bytecode;

        var hasDeadForInitCompletionLoad = false;
        for (var i = 0; i < bytecode.Length - 1; i++)
            if (bytecode[i] == (byte)JsOpCode.LdaTheHole &&
                bytecode[i + 1] == (byte)JsOpCode.LdaSmiWide)
            {
                hasDeadForInitCompletionLoad = true;
                break;
            }

        Assert.That(hasDeadForInitCompletionLoad, Is.False);
    }

    [Test]
    public void Compiler_Function_Body_Let_Declarations_Do_Not_Leave_Dead_Empty_Completion_Loads()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let identity = function (x) { return x; };
                                                                       let s = 0;
                                                                       for (let i = 0; i < 10000; i = i + 1) {
                                                                           s = identity(i) + 1;
                                                                       }
                                                                       return s;
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Not.Contain("LdaTheHole\n0010  LdaZero"));
        Assert.That(disasm, Does.Not.Contain("LdaTheHole\n0014  LdaZero"));
    }

    [Test]
    public void Disassembler_Dumps_Header_Constants_And_Code()
    {
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmi, 1,
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            ["x"],
            1,
            new[] { 1 }
        );

        var text = Disassembler.Dump(script, new()
        {
            UnitKind = "function",
            UnitName = "test"
        });

        Assert.That(text, Does.Contain("; okojo-disasm v1"));
        Assert.That(text, Does.Contain("; unit-name: test"));
        Assert.That(text, Does.Contain(".constants"));
        Assert.That(text, Does.Contain("String(\"x\")"));
        Assert.That(text, Does.Contain(".code"));
        Assert.That(text, Does.Contain("0000  LdaSmi 1"));
        Assert.That(text, Does.Contain("0002  Star r0"));
    }

    [Test]
    public void Vm_Executes_LdaSmiWide_And_ExtraWide()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmiWide, 0x2C, 0x01, // 300
                (byte)JsOpCode.Star, 0,
                (byte)JsOpCode.LdaSmiExtraWide, 0x70, 0x11, 0x01, 0x00, // 70000
                (byte)JsOpCode.Add, 0, 0,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            1,
            Array.Empty<int>()
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(70300));
    }

    [Test]
    public void Compiler_Uses_LdaSmiWide_And_ExtraWide_For_IntegerLiterals()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                       let a = 300;
                                                                       return a + 70000;
                                                                   }
                                                                   t();
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.LdaSmiWide), Is.True);
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.LdaSmiExtraWide), Is.True);
    }

    [Test]
    public void Disassembler_Formats_LdaSmiWide_And_ExtraWide()
    {
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmiWide, 0x2C, 0x01, // 300
                (byte)JsOpCode.LdaSmiExtraWide, 0xFF, 0xFF, 0xFF, 0xFF, // -1
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        var text = Disassembler.Dump(script);
        Assert.That(text, Does.Contain("LdaSmiWide 300"));
        Assert.That(text, Does.Contain("LdaSmiExtraWide -1"));
    }

    [Test]
    public void Disassembler_Does_Not_Emit_Orphan_Bytes_After_Explicit_Wide_Instructions()
    {
        var script = new JsScript(
            [
                (byte)JsOpCode.CreateClosureWide, 0x18, 0x03, 0x00,
                (byte)JsOpCode.StaNamedPropertyWide, 0xF1, 0x01, 0xF5, 0x00, 0x13, 0x01,
                (byte)JsOpCode.LdaCurrentContextSlotWide, 0xEB, 0x01,
                (byte)JsOpCode.LdaNull,
                (byte)JsOpCode.MovWide, 0x01, 0x00, 0x10, 0x00,
                (byte)JsOpCode.Return
            ],
            Array.Empty<double>(),
            Array.Empty<object>(),
            0,
            Array.Empty<int>()
        );

        var text = Disassembler.Dump(script);

        Assert.That(text, Does.Contain("CreateClosureWide idx:792, flags:0"));
        Assert.That(text, Does.Contain("StaNamedPropertyWide obj:r497, name:245, slot:275"));
        Assert.That(text, Does.Contain("LdaCurrentContextSlotWide slot:491"));
        Assert.That(text, Does.Contain("MovWide r1 -> r16"));
        Assert.That(text, Does.Not.Contain("\n   0007  248"));
        Assert.That(text, Does.Not.Contain("\n   0019  248"));
        Assert.That(text, Does.Not.Contain("\n   0024  241"));
    }

    [Test]
    public void Compiler_Uses_Wide_NamedProperty_Opcodes_When_ObjectPool_Exceeds_Byte_Range()
    {
        var source = new StringBuilder();
        source.AppendLine("function f() {");
        for (var i = 0; i < 300; i++)
        {
            source.Append("('");
            source.Append("pad");
            source.Append(i);
            source.AppendLine("');");
        }

        source.AppendLine("var o = { targetWide: 7 };");
        source.AppendLine("return o.targetWide;");
        source.AppendLine("}");
        source.AppendLine("f();");

        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript(source.ToString()));

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "f");
        Assert.That(f.Script.Bytecode.Contains((byte)JsOpCode.LdaNamedPropertyWide), Is.True);

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }
}
