using System.Text;
using Okojo.Diagnostics;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

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
                (byte)JsOpCode.JumpIfTrue,
                2,
                0,
                (byte)JsOpCode.LdaSmi,
                9,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
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
                (byte)JsOpCode.JumpIfToBooleanFalse,
                2,
                0,
                (byte)JsOpCode.LdaSmi,
                9,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
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
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(a) { let b = a; return b; }
                t(7);
                """
            )
        );

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

        Assert.That(
            script.Bytecode,
            Is.EqualTo(new[] { (byte)JsOpCode.LdaZero, (byte)JsOpCode.Return })
        );
    }

    [Test]
    public void BytecodeBuilder_ToScript_PreservesCompilerMetadataWithoutPostBuildClone()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);
        builder.SetStrictDeclared(true);
        builder.Emit(JsOpCode.Return);
        const string source = "function named() {}";
        var sourceCode = new SourceCode(source, "metadata.js");
        var functionSource = FunctionSourceTextSegment.FromWholeString(source);

        var script = builder.ToScript(
            sourceCode,
            functionSource,
            topLevelLexicalAtoms: [11, 12],
            topLevelLexicalSlots: [1, 2],
            topLevelLexicalConstFlags: [false, true],
            suppressTopLevelLexicalRegistration: true
        );

        Assert.Multiple(() =>
        {
            Assert.That(script.SourceCode, Is.SameAs(sourceCode));
            Assert.That(script.StrictDeclared, Is.True);
            Assert.That(script.FunctionSourceText, Is.EqualTo(functionSource));
            Assert.That(script.TopLevelLexicalAtoms, Is.EqualTo(new[] { 11, 12 }));
            Assert.That(script.TopLevelLexicalSlots, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(script.TopLevelLexicalConstFlags, Is.EqualTo(new[] { false, true }));
            Assert.That(script.SuppressTopLevelLexicalRegistration, Is.True);
        });
    }

    [Test]
    public void Compiler_SharesSourceCodeAcrossNestedScriptUnits()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsScriptCompiler(realm).Compile(
            "function outer() { function inner() {} return inner; } function sibling() {}",
            "shared-source.js"
        );
        var scripts = new HashSet<JsScript>(ReferenceEqualityComparer.Instance);
        AddScriptTree(script);

        Assert.Multiple(() =>
        {
            Assert.That(scripts, Has.Count.EqualTo(4));
            Assert.That(script.SourcePath, Is.EqualTo("shared-source.js"));
            Assert.That(
                scripts.All(candidate => ReferenceEquals(candidate.SourceCode, script.SourceCode)),
                Is.True
            );
        });

        void AddScriptTree(JsScript candidate)
        {
            if (!scripts.Add(candidate))
                return;
            foreach (var function in candidate.ObjectConstants.OfType<JsBytecodeFunction>())
                AddScriptTree(function.Script);
        }
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

        Assert.That(
            script.Bytecode,
            Is.EqualTo(
                new byte[]
                {
                    (byte)JsOpCode.LdaSmi,
                    7,
                    (byte)JsOpCode.Star,
                    0,
                    (byte)JsOpCode.Return,
                }
            )
        );
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

        Assert.That(
            script.Bytecode,
            Is.EqualTo(
                new[] { (byte)JsOpCode.LdaTheHole, (byte)JsOpCode.LdaZero, (byte)JsOpCode.Return }
            )
        );
    }

    [Test]
    public void BytecodeBuilder_ConstantDedup_UsesExactBitsAndScales()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);

        var positiveZero = builder.AddNumericConstant(0d);
        var negativeZero = builder.AddNumericConstant(-0d);
        Assert.That(negativeZero, Is.Not.EqualTo(positiveZero));
        Assert.That(builder.AddNumericConstant(0d), Is.EqualTo(positiveZero));

        for (var i = 0; i < 40; i++)
            builder.AddNumericConstant(i + 1000d);
        var numericIndex = builder.AddNumericConstant(1010d);
        Assert.That(numericIndex, Is.EqualTo(12));
        var nanIndex = builder.AddNumericConstant(
            BitConverter.UInt64BitsToDouble(0x7FF8000000000001)
        );
        Assert.That(
            builder.AddNumericConstant(BitConverter.UInt64BitsToDouble(0x7FF8000000000001)),
            Is.EqualTo(nanIndex)
        );
        Assert.That(
            builder.AddNumericConstant(BitConverter.UInt64BitsToDouble(0x7FF8000000000002)),
            // NaN payloads are canonicalized to JsNan at emission (raw bit
            // table), so distinct payload NaNs dedup to the same slot.
            Is.EqualTo(nanIndex)
        );

        var sharedObject = new object();
        var sharedObjectIndex = builder.AddObjectConstant(sharedObject);
        Assert.That(builder.AddObjectConstant(sharedObject), Is.EqualTo(sharedObjectIndex));
        for (var i = 0; i < 40; i++)
            builder.AddObjectConstant(new object());
        Assert.That(builder.AddObjectConstant(sharedObject), Is.EqualTo(sharedObjectIndex));

        var sharedAtomIndex = builder.AddAtomizedStringConstant("dedup-shared");
        Assert.That(builder.AddAtomizedStringConstant("dedup-shared"), Is.EqualTo(sharedAtomIndex));
        for (var i = 0; i < 40; i++)
            builder.AddAtomizedStringConstant($"dedup-{i}");
        Assert.That(builder.AddAtomizedStringConstant("dedup-shared"), Is.EqualTo(sharedAtomIndex));
    }

    [Test]
    public void BytecodeBuilder_AllocateRegisterBlock_ReusesFragmentedFreeRange()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        using var builder = new BytecodeBuilder(realm);
        var r0 = builder.AllocateTemporaryRegister();
        var r1 = builder.AllocateTemporaryRegister();
        var r2 = builder.AllocateTemporaryRegister();
        var r3 = builder.AllocateTemporaryRegister();
        builder.ReleaseTemporaryRegister(r0);
        builder.ReleaseTemporaryRegister(r1);
        builder.ReleaseTemporaryRegister(r3);

        var block = builder.AllocateTemporaryRegisterBlock(2);

        Assert.That(block, Is.EqualTo(r0));
        Assert.That(builder.RegisterCount, Is.EqualTo(4));
        builder.ReleaseTemporaryRegister(block);
        builder.ReleaseTemporaryRegister(block + 1);
        builder.ReleaseTemporaryRegister(r2);
    }

    [Test]
    public void Compiler_Uses_Mov_For_ArrayDestructuring_Source_Copy()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(a) { let b; [b] = a; return b; }
                t([7]);
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.Mov), Is.True);
    }

    [Test]
    public void Compiler_Direct_Local_Call_Reuses_Local_Register()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(identity, x) {
                    return identity(x);
                }
                """
            )
        );

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
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(obj, x) {
                    return obj.f(x);
                }
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Contain("CallProperty func:r2, obj:r0, args:r1.., argc:1"));
        Assert.That(t.Script.RegisterCount, Is.EqualTo(3));
    }

    [Test]
    public void Compiler_Chained_Require_Declarators_Store_String_Arguments_Before_Call()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(require) {
                    var React = require("react"),
                        Scheduler = require("scheduler");
                    return [React, Scheduler];
                }
                """
            )
        );

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
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t(x) {
                    let s = 0;
                    s = x + 1;
                    return s;
                }
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Not.Contain("Star r2"));
        Assert.That(t.Script.RegisterCount, Is.EqualTo(2));
    }

    [Test]
    public void Compiler_For_Let_Init_Does_Not_Leave_Dead_Empty_Completion_Load()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t() {
                    for (let i = 0; i < 10000; i = i + 1) {
                    }
                }
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var bytecode = t.Script.Bytecode;

        var hasDeadForInitCompletionLoad = false;
        for (var i = 0; i < bytecode.Length - 1; i++)
            if (
                bytecode[i] == (byte)JsOpCode.LdaTheHole
                && bytecode[i + 1] == (byte)JsOpCode.LdaSmiWide
            )
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
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t() {
                    let identity = function (x) { return x; };
                    let s = 0;
                    for (let i = 0; i < 10000; i = i + 1) {
                        s = identity(i) + 1;
                    }
                    return s;
                }
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });

        Assert.That(disasm, Does.Not.Contain("LdaTheHole\n0010  LdaZero"));
        Assert.That(disasm, Does.Not.Contain("LdaTheHole\n0014  LdaZero"));
    }

    [Test]
    public void PlannedCompiler_Elides_Unused_ForUpdate_Result_And_Fallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = realm.CompileScript(
            """
            function t() {
                var f = function (x) { return x; };
                var x = 0;
                for (; x < 3; x++) {
                }
                return f(x);
            }
            """
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var disasm = Disassembler.Dump(t.Script, new() { UnitKind = "function", UnitName = "t" });
        var codeLines = disasm
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && char.IsDigit(line[0]))
            .ToArray();
        var jumpIndex = Array.FindIndex(
            codeLines,
            line => line.Contains("  Jump ", StringComparison.Ordinal)
        );

        Assert.That(disasm, Does.Not.Contain("Mov r0 ->"));
        Assert.That(disasm, Does.Contain("CallUndefinedReceiver func:r0"));
        Assert.That(jumpIndex, Is.GreaterThan(0));
        Assert.That(codeLines[jumpIndex - 1], Does.Not.Contain("Ldar "));
        Assert.That(codeLines[^2], Does.Contain("CallUndefinedReceiver"));
        Assert.That(codeLines[^1], Does.Contain("Return"));
    }

    [Test]
    public void Disassembler_Dumps_Header_Constants_And_Code()
    {
        var script = new JsScript(
            [(byte)JsOpCode.LdaSmi, 1, (byte)JsOpCode.Star, 0, (byte)JsOpCode.Return],
            Array.Empty<ulong>(),
            ["x"],
            1,
            new[] { 1 }
        );

        var text = Disassembler.Dump(script, new() { UnitKind = "function", UnitName = "test" });

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
                (byte)JsOpCode.LdaSmiWide,
                0x2C,
                0x01, // 300
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.LdaSmiExtraWide,
                0x70,
                0x11,
                0x01,
                0x00, // 70000
                (byte)JsOpCode.Add,
                0,
                0,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
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
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function t() {
                    let a = 300;
                    return a + 70000;
                }
                t();
                """
            )
        );

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.LdaSmiWide), Is.True);
        Assert.That(t.Script.Bytecode.Contains((byte)JsOpCode.LdaSmiExtraWide), Is.True);
    }

    [Test]
    public void Disassembler_Formats_LdaSmiWide_And_ExtraWide()
    {
        var script = new JsScript(
            [
                (byte)JsOpCode.LdaSmiWide,
                0x2C,
                0x01, // 300
                (byte)JsOpCode.LdaSmiExtraWide,
                0xFF,
                0xFF,
                0xFF,
                0xFF, // -1
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
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
                (byte)JsOpCode.CreateClosureWide,
                0x18,
                0x03,
                0x00,
                (byte)JsOpCode.StaNamedPropertyWide,
                0xF1,
                0x01,
                0xF5,
                0x00,
                0x13,
                0x01,
                (byte)JsOpCode.LdaCurrentContextSlotWide,
                0xEB,
                0x01,
                (byte)JsOpCode.LdaNull,
                (byte)JsOpCode.MovWide,
                0x01,
                0x00,
                0x10,
                0x00,
                (byte)JsOpCode.Return,
            ],
            Array.Empty<ulong>(),
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
        source.AppendLine("var o = { targetWide: 7 };");
        for (var i = 0; i < 300; i++)
        {
            source.Append("o.pad");
            source.Append(i);
            source.AppendLine(";");
        }
        source.AppendLine("return o.targetWide;");
        source.AppendLine("}");
        source.AppendLine("f();");

        var realm = JsRuntime.Create().DefaultRealm;
        var script = realm.CompileScript(source.ToString());

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "f");
        Assert.That(f.Script.Bytecode.Contains((byte)JsOpCode.LdaNamedPropertyWide), Is.True);

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }
}
