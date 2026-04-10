using System.Globalization;
using System.Text;
using Okojo.Bytecode;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Diagnostics;

public sealed class DisassemblerOptions
{
    public string UnitKind { get; init; } = "script";
    public string UnitName { get; init; } = "<anonymous>";
    public int ContextSlots { get; init; }
    public bool IncludeConstants { get; init; } = true;
    public int HighlightedProgramCounter { get; init; } = -1;
}

public static class Disassembler
{
    public static string Dump(
        JsScript script,
        DisassemblerOptions? options = null,
        Func<JsScript, int, (JsOpCode OpCode, byte[] Operands)?>? instructionOverrideResolver = null)
    {
        options ??= new();

        var sb = new StringBuilder();
        sb.AppendLine("; okojo-disasm v1");
        sb.AppendLine($"; unit-kind: {options.UnitKind}");
        sb.AppendLine($"; unit-name: {options.UnitName}");
        sb.AppendLine($"; registers: {script.RegisterCount}");
        sb.AppendLine($"; constants: {script.NumericConstants.Length + script.ObjectConstants.Length}");
        sb.AppendLine($"; context-slots: {options.ContextSlots}");

        if (options.IncludeConstants)
        {
            sb.AppendLine(".constants");
            var idx = 0;
            foreach (var n in script.NumericConstants)
                sb.AppendLine($"  [{idx++}] Number({n})");

            foreach (var o in script.ObjectConstants)
                sb.AppendLine($"  [{idx++}] {FormatConstant(o)}");
        }

        sb.AppendLine(".code");

        var code = script.Bytecode;
        var pc = 0;
        while (pc < code.Length)
        {
            var instructionPc = pc;
            if (!BytecodeInfo.TryDecodeInstructionHeader(code, pc, out var op, out var scale, out var operandStart,
                    out var operandByteCount, out var instructionLength))
            {
                sb.AppendLine($"{instructionPc:D4}  <truncated>");
                break;
            }

            pc += instructionLength;
            var operandArray = code.AsSpan(operandStart, operandByteCount).ToArray();
            ReadOnlySpan<byte> operands = operandArray;

            if (options.HighlightedProgramCounter == instructionPc &&
                instructionOverrideResolver?.Invoke(script, instructionPc) is { } overrideInstruction)
            {
                op = overrideInstruction.OpCode;
                scale = BytecodeInfo.OperandScale.Single;
                operandArray = new byte[BytecodeInfo.GetOperandByteCount(op, scale)];
                operands = operandArray;
                if (overrideInstruction.Operands is { } originalOperands)
                {
                    var copyLength = Math.Min(operandArray.Length, originalOperands.Length);
                    originalOperands.AsSpan(0, copyLength).CopyTo(operandArray.AsSpan());
                }
            }

            sb.Append(options.HighlightedProgramCounter == instructionPc ? "=> " : "   ");
            sb.Append($"{instructionPc:D4}  {op}");
            if (operandByteCount > 0)
            {
                sb.Append(' ');
                sb.Append(FormatOperands(op, operands, scale));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatConstant(object value)
    {
        return value switch
        {
            string s => $"String(\"{EscapeAndMaybeTruncate(s)}\")",
            JsValue jv => JsValueDebugString.FormatValue(jv),
            JsFunction fn => $"Function({fn.Name ?? "<anonymous>"})",
            JsObject _ => $"Object({value.GetType().Name})",
            _ => value.ToString() ?? "<null>"
        };
    }

    private static string EscapeAndMaybeTruncate(string s)
    {
        const int max = 40;
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        return escaped.Length <= max ? escaped : escaped[..max] + "...<truncated>";
    }

    private static string FormatOperands(JsOpCode op, ReadOnlySpan<byte> operands, BytecodeInfo.OperandScale scale)
    {
        return op switch
        {
            JsOpCode.Ldar or JsOpCode.LdaLexicalLocal or JsOpCode.Star or JsOpCode.StaLexicalLocal => $"r{operands[0]}",
            JsOpCode.LdarWide or JsOpCode.LdaLexicalLocalWide or JsOpCode.StarWide or JsOpCode.StaLexicalLocalWide
                => $"r{operands[0] | (operands[1] << 8)}",
            JsOpCode.LdaModuleVariable or JsOpCode.StaModuleVariable =>
                $"cell_index:{(sbyte)operands[0]}, depth:{operands[1]}",
            JsOpCode.LdaSmi => ((sbyte)operands[0]).ToString(),
            JsOpCode.LdaSmiWide => ((short)(operands[0] | (operands[1] << 8))).ToString(),
            JsOpCode.LdaSmiExtraWide => (operands[0] | (operands[1] << 8) | (operands[2] << 16) | (operands[3] << 24))
                .ToString(),
            JsOpCode.LdaNumericConstant => $"num:{operands[0]}",
            JsOpCode.LdaNumericConstantWide => $"num:{operands[0] | (operands[1] << 8)}",
            JsOpCode.LdaStringConstant => $"str:{operands[0]}",
            JsOpCode.LdaTypedConst => $"tag:{(Tag)operands[0]}, const:{operands[1]}",
            JsOpCode.LdaTypedConstWide => $"tag:{(Tag)operands[0]}, const:{operands[1] | (operands[2] << 8)}",
            JsOpCode.Jump or JsOpCode.JumpIfTrue or JsOpCode.JumpIfFalse or JsOpCode.JumpIfToBooleanTrue
                or JsOpCode.JumpIfToBooleanFalse or JsOpCode.JumpIfNull or JsOpCode.JumpIfUndefined
                or JsOpCode.JumpIfNotUndefined
                or JsOpCode.JumpIfJsReceiver => ((short)(operands[0] | (operands[1] << 8))).ToString(),
            JsOpCode.PushTry => $"catch:{(short)(operands[0] | (operands[1] << 8))}",
            JsOpCode.SwitchOnSmi => $"table_start:{operands[0]}, table_len:{operands[1]}",
            JsOpCode.Add or JsOpCode.Sub or JsOpCode.Mul or JsOpCode.Div or JsOpCode.Mod or JsOpCode.Exp
                or JsOpCode.BitwiseOr or JsOpCode.BitwiseXor or JsOpCode.BitwiseAnd or JsOpCode.ShiftLeft
                or JsOpCode.ShiftRight or JsOpCode.ShiftRightLogical or JsOpCode.TestEqual or JsOpCode.TestNotEqual
                or JsOpCode.TestEqualStrict or JsOpCode.TestLessThan or JsOpCode.TestGreaterThan
                or JsOpCode.TestLessThanOrEqual or JsOpCode.TestGreaterThanOrEqual or JsOpCode.TestInstanceOf
                or JsOpCode.TestIn => $"r{operands[0]}, slot:{operands[1]}",
            JsOpCode.AddSmi or JsOpCode.SubSmi or JsOpCode.MulSmi or JsOpCode.ModSmi or JsOpCode.ExpSmi
                or JsOpCode.TestLessThanSmi or JsOpCode.TestGreaterThanSmi or JsOpCode.TestLessThanOrEqualSmi
                or JsOpCode.TestGreaterThanOrEqualSmi => $"imm:{(sbyte)operands[0]}, slot:{operands[1]}",
            JsOpCode.LdaGlobal or JsOpCode.StaGlobal or JsOpCode.StaGlobalInit or JsOpCode.StaGlobalFuncDecl
                or JsOpCode.TypeOfGlobal => $"name:{operands[0]}, slot:{operands[1]}",
            JsOpCode.LdaGlobalWide or JsOpCode.StaGlobalWide or JsOpCode.StaGlobalInitWide
                or JsOpCode.StaGlobalFuncDeclWide
                or JsOpCode.TypeOfGlobalWide =>
                $"name:{operands[0] | (operands[1] << 8)}, slot:{operands[2] | (operands[3] << 8)}",
            JsOpCode.CallUndefinedReceiver =>
                $"func:r{BytecodeInfo.ReadUnsignedOperand(operands, 0, scale)}, args:r{BytecodeInfo.ReadUnsignedOperand(operands, 1, scale)}.., argc:{BytecodeInfo.ReadUnsignedOperand(operands, 2, scale)}",
            JsOpCode.CallAny =>
                $"func:r{BytecodeInfo.ReadUnsignedOperand(operands, 0, scale)}, args:r{BytecodeInfo.ReadUnsignedOperand(operands, 1, scale)}.., argc:{BytecodeInfo.ReadUnsignedOperand(operands, 2, scale)}",
            JsOpCode.CallProperty =>
                $"func:r{BytecodeInfo.ReadUnsignedOperand(operands, 0, scale)}, obj:r{BytecodeInfo.ReadUnsignedOperand(operands, 1, scale)}, args:r{BytecodeInfo.ReadUnsignedOperand(operands, 2, scale)}.., argc:{BytecodeInfo.ReadUnsignedOperand(operands, 3, scale)}",
            JsOpCode.Construct =>
                $"func:r{BytecodeInfo.ReadUnsignedOperand(operands, 0, scale)}, args:r{BytecodeInfo.ReadUnsignedOperand(operands, 1, scale)}.., argc:{BytecodeInfo.ReadUnsignedOperand(operands, 2, scale)}",
            JsOpCode.LdaNamedProperty or JsOpCode.StaNamedProperty =>
                $"obj:r{operands[0]}, name:{operands[1]}, slot:{operands[2]}",
            JsOpCode.LdaNamedPropertyWide or JsOpCode.StaNamedPropertyWide =>
                $"obj:r{operands[0] | (operands[1] << 8)}, name:{operands[2] | (operands[3] << 8)}, slot:{operands[4] | (operands[5] << 8)}",
            JsOpCode.GetNamedPropertyFromSuper => $"name:{operands[0]}",
            JsOpCode.GetNamedPropertyFromSuperWide => $"name:{operands[0] | (operands[1] << 8)}",
            JsOpCode.LdaKeyedProperty => $"obj:r{FormatUnsignedOperand(operands, 0, scale)}",
            JsOpCode.StaKeyedProperty =>
                $"obj:r{FormatUnsignedOperand(operands, 0, scale)}, key:r{FormatUnsignedOperand(operands, 1, scale)}",
            JsOpCode.InitializeArrayElement =>
                $"obj:r{operands[0] | (operands[1] << 8)}, index:{operands[2] | (operands[3] << 8)}",
            JsOpCode.DefineOwnKeyedProperty =>
                $"obj:r{FormatUnsignedOperand(operands, 0, scale)}, key:r{FormatUnsignedOperand(operands, 1, scale)}",
            JsOpCode.InitializeNamedProperty =>
                $"obj:r{operands[0] | (operands[1] << 8)}, slot:{operands[2] + (operands[3] << 8)}",
            JsOpCode.PushContext => $"r{operands[0]}",
            JsOpCode.LdaContextSlot or JsOpCode.StaContextSlot or JsOpCode.LdaContextSlotNoTdz =>
                $"slot:{operands[0]}, depth:{operands[1]}",
            JsOpCode.LdaContextSlotWide or JsOpCode.StaContextSlotWide or JsOpCode.LdaContextSlotNoTdzWide =>
                $"slot:{operands[0] | (operands[1] << 8)}, depth:{operands[2]}",
            JsOpCode.LdaCurrentContextSlot or JsOpCode.StaCurrentContextSlot or JsOpCode.LdaCurrentContextSlotNoTdz =>
                $"slot:{operands[0]}",
            JsOpCode.LdaCurrentContextSlotWide or JsOpCode.StaCurrentContextSlotWide
                or JsOpCode.LdaCurrentContextSlotNoTdzWide => $"slot:{operands[0] | (operands[1] << 8)}",
            JsOpCode.JumpLoop => $"{(sbyte)operands[0]}, depth:{operands[1]}",
            JsOpCode.CallRuntime =>
                $"runtime:{FormatRuntimeId((byte)BytecodeInfo.ReadUnsignedOperand(operands, 0, scale))}, args:r{BytecodeInfo.ReadUnsignedOperand(operands, 1, scale)}.., argc:{BytecodeInfo.ReadUnsignedOperand(operands, 2, scale)}",
            JsOpCode.InvokeIntrinsic =>
                $"intrinsic:{FormatIntrinsicId(operands[0])}, args:r{operands[1]}.., argc:{operands[2]}",
            JsOpCode.CreateArrayLiteral => $"idx:{operands[0] | (operands[1] << 8)}",
            JsOpCode.CreateObjectLiteral or JsOpCode.CreateClosure => $"idx:{operands[0]}, flags:{operands[1]}",
            JsOpCode.CreateObjectLiteralWide or JsOpCode.CreateClosureWide =>
                $"idx:{operands[0] | (operands[1] << 8)}, flags:{operands[2]}",
            JsOpCode.CreateEmptyArrayLiteral => string.Empty,
            JsOpCode.CreateBlockContext => $"idx:{operands[0]}",
            JsOpCode.CreateFunctionContext => $"idx:{operands[0]}",
            JsOpCode.CreateFunctionContextWithCells => $"slots:{operands[0]}",
            JsOpCode.CreateFunctionContextWithCellsWide => $"slots:{operands[0] | (operands[1] << 8)}",
            JsOpCode.CreateRestParameter => $"start:{operands[0]}",
            JsOpCode.ForInEnumerate => $"obj:r{operands[0]}",
            JsOpCode.ForInNext => $"enumerator:r{operands[0]}",
            JsOpCode.ForInStep => $"enumerator:r{operands[0]}",
            JsOpCode.SwitchOnGeneratorState =>
                $"gen:r{operands[0]}, table_start:{operands[1]}, table_len:{operands[2]}",
            JsOpCode.SuspendGenerator =>
                $"gen:r{operands[0]}, regs:r{operands[1]}.., count:{operands[2]}, suspend_id:{operands[3]}",
            JsOpCode.ResumeGenerator => $"gen:r{operands[0]}, regs:r{operands[1]}.., count:{operands[2]}",
            JsOpCode.InitPrivateField =>
                $"obj:r{operands[0]}, value:r{operands[1]}, brand:{operands[2] | (operands[3] << 8)}, slot:{operands[4] | (operands[5] << 8)}",
            JsOpCode.InitPrivateAccessor =>
                $"obj:r{operands[0]}, getter:r{operands[1]}, setter:r{operands[2]}, brand:{operands[3] | (operands[4] << 8)}, slot:{operands[5] | (operands[6] << 8)}",
            JsOpCode.InitPrivateMethod =>
                $"obj:r{operands[0]}, method:r{operands[1]}, brand:{operands[2] | (operands[3] << 8)}, slot:{operands[4] | (operands[5] << 8)}",
            JsOpCode.GetPrivateField =>
                $"obj:r{operands[0]}, brand:{operands[1] | (operands[2] << 8)}, slot:{operands[3] | (operands[4] << 8)}",
            JsOpCode.SetPrivateField =>
                $"obj:r{operands[0]}, value:r{operands[1]}, brand:{operands[2] | (operands[3] << 8)}, slot:{operands[4] | (operands[5] << 8)}",
            JsOpCode.Mov => $"r{operands[0]} -> r{operands[1]}",
            JsOpCode.MovWide => $"r{operands[0] | (operands[1] << 8)} -> r{operands[2] | (operands[3] << 8)}",
            _ => string.Join(", ", operands.ToArray().Select(static b => b.ToString()))
        };
    }

    private static string FormatRuntimeId(byte runtimeId)
    {
        return Enum.IsDefined(typeof(RuntimeId), runtimeId)
            ? ((RuntimeId)runtimeId).ToString()
            : runtimeId.ToString();
    }

    private static string FormatIntrinsicId(byte intrinsicId)
    {
        return string.Empty;
    }

    private static string FormatUnsignedOperand(ReadOnlySpan<byte> operands, int operandIndex,
        BytecodeInfo.OperandScale scale)
    {
        var width = (int)scale;
        var offset = operandIndex * width;
        return offset + width <= operands.Length
            ? BytecodeInfo.ReadUnsignedOperand(operands, operandIndex, scale).ToString(CultureInfo.InvariantCulture)
            : "?";
    }
}
