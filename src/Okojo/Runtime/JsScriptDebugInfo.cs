using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Runtime;

internal static class JsScriptDebugInfo
{
    internal static bool TryGetExactSourceLocation(JsScript script, int opcodePc, out int line, out int column)
    {
        line = 0;
        column = 0;

        if (script.SourceCode is not { Source: not null } sourceCode ||
            script.DebugPcOffsets is null || script.DebugSourceOffsets is null)
            return false;
        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;

        var index = Array.BinarySearch(script.DebugPcOffsets, opcodePc);
        if (index < 0)
            return false;

        var sourceOffset = script.DebugSourceOffsets[index];
        (line, column) = SourceLocation.GetLineColumn(sourceCode, sourceOffset);
        return true;
    }

    internal static bool TryGetSourceLocation(JsScript script, int opcodePc, out int line, out int column)
    {
        line = 0;
        column = 0;

        if (script.SourceCode is not { Source: not null } sourceCode ||
            script.DebugPcOffsets is null || script.DebugSourceOffsets is null)
            return false;
        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;

        var pcOffsets = script.DebugPcOffsets;
        var index = Array.BinarySearch(pcOffsets, opcodePc);
        if (index < 0)
            index = ~index - 1;
        if ((uint)index >= (uint)pcOffsets.Length)
            return false;

        var sourceOffset = script.DebugSourceOffsets[index];
        (line, column) = SourceLocation.GetLineColumn(sourceCode, sourceOffset);
        return true;
    }

    internal static bool TryFindFirstPcForSourceLine(JsScript script, int line, out int pc, out int column,
        out int actualLine)
    {
        pc = -1;
        column = 0;
        actualLine = 0;

        if (script.SourceCode is not { Source: not null } sourceCode ||
            script.DebugPcOffsets is null || script.DebugSourceOffsets is null)
            return false;
        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;
        if (!SourceLocation.TryGetLineOffsetRange(sourceCode, line, out var lineStartOffset,
                out var lineEndOffsetExclusive))
            return false;

        var fallbackPc = -1;
        var fallbackColumn = 0;
        var fallbackLine = 0;
        for (var i = 0; i < script.DebugPcOffsets.Length; i++)
        {
            var sourceOffset = script.DebugSourceOffsets[i];
            if (sourceOffset < lineStartOffset)
                continue;

            if (sourceOffset < lineEndOffsetExclusive)
            {
                pc = script.DebugPcOffsets[i];
                column = sourceOffset - lineStartOffset + 1;
                actualLine = line;
                return true;
            }

            if (fallbackPc < 0)
            {
                fallbackPc = script.DebugPcOffsets[i];
                (fallbackLine, fallbackColumn) = SourceLocation.GetLineColumn(sourceCode, sourceOffset);
            }
        }

        if (fallbackPc < 0)
            return false;

        pc = fallbackPc;
        column = fallbackColumn;
        actualLine = fallbackLine;
        return true;
    }

    internal static bool HasExactSourceLine(JsScript script, int line)
    {
        if (script.SourceCode is not { Source: not null } sourceCode ||
            script.DebugPcOffsets is null || script.DebugSourceOffsets is null)
            return false;
        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;
        if (!SourceLocation.TryGetLineOffsetRange(sourceCode, line, out var lineStartOffset,
                out var lineEndOffsetExclusive))
            return false;

        for (var i = 0; i < script.DebugPcOffsets.Length; i++)
        {
            var sourceOffset = script.DebugSourceOffsets[i];
            if (sourceOffset >= lineStartOffset && sourceOffset < lineEndOffsetExclusive)
                return true;
        }

        return false;
    }

    internal static bool TryFindFirstPcForExactSourceLine(JsScript script, int line, out int pc, out int column,
        out int actualLine)
    {
        pc = -1;
        column = 0;
        actualLine = 0;

        if (script.SourceCode is not { Source: not null } sourceCode ||
            script.DebugPcOffsets is null || script.DebugSourceOffsets is null)
            return false;
        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;
        if (!SourceLocation.TryGetLineOffsetRange(sourceCode, line, out var lineStartOffset,
                out var lineEndOffsetExclusive))
            return false;

        for (var i = 0; i < script.DebugPcOffsets.Length; i++)
        {
            var sourceOffset = script.DebugSourceOffsets[i];
            if (sourceOffset < lineStartOffset || sourceOffset >= lineEndOffsetExclusive)
                continue;

            pc = script.DebugPcOffsets[i];
            column = sourceOffset - lineStartOffset + 1;
            actualLine = line;
            return true;
        }

        return false;
    }

    internal static IReadOnlyList<JsLocalDebugInfo>? GetVisibleLocalInfos(JsScript script, int opcodePc)
    {
        if (script.LocalDebugInfos is not { Length: > 0 } localDebugInfos)
            return null;

        var visible = new List<JsLocalDebugInfo>(localDebugInfos.Length);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < localDebugInfos.Length; i++)
        {
            var info = localDebugInfos[i];
            if (!info.IsLiveAt(opcodePc))
                continue;
            if (!seenNames.Add(info.Name))
                continue;
            visible.Add(info);
        }

        return visible.Count == 0 ? null : visible;
    }

    internal static IReadOnlyList<int>? GetInstructionOperandRegisters(JsScript script, int opcodePc)
    {
        var bytecode = script.Bytecode;
        if ((uint)opcodePc >= (uint)bytecode.Length)
            return null;

        if (!BytecodeInfo.TryDecodeInstructionHeader(bytecode, opcodePc, out var op, out var scale,
                out var operandStart,
                out var operandByteCount, out _))
            return null;

        var operands = bytecode.AsSpan(operandStart, operandByteCount);
        var registers = new HashSet<int>();
        switch (op)
        {
            case JsOpCode.Ldar:
            case JsOpCode.LdaLexicalLocal:
            case JsOpCode.Star:
            case JsOpCode.StaLexicalLocal:
                registers.Add(bytecode[opcodePc + 1]);
                break;
            case JsOpCode.LdarWide:
            case JsOpCode.LdaLexicalLocalWide:
            case JsOpCode.StarWide:
            case JsOpCode.StaLexicalLocalWide:
                registers.Add(ReadU16(bytecode, opcodePc + 1));
                break;
            case JsOpCode.Mov:
                registers.Add(bytecode[opcodePc + 1]);
                registers.Add(bytecode[opcodePc + 2]);
                break;
            case JsOpCode.MovWide:
                registers.Add(ReadU16(bytecode, opcodePc + 1));
                registers.Add(ReadU16(bytecode, opcodePc + 3));
                break;
            case JsOpCode.CallUndefinedReceiver:
            case JsOpCode.Construct:
                AddCallRegisters(registers,
                    BytecodeInfo.ReadUnsignedOperand(operands, 0, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 1, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 2, scale));
                break;
            case JsOpCode.CallProperty:
                registers.Add(BytecodeInfo.ReadUnsignedOperand(operands, 1, scale));
                AddCallRegisters(registers,
                    BytecodeInfo.ReadUnsignedOperand(operands, 0, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 2, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 3, scale));
                break;
            case JsOpCode.CallRuntime:
                AddRegisterRange(registers,
                    BytecodeInfo.ReadUnsignedOperand(operands, 1, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 2, scale));
                break;
            case JsOpCode.CallAny:
                AddCallRegisters(registers,
                    BytecodeInfo.ReadUnsignedOperand(operands, 0, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 1, scale),
                    BytecodeInfo.ReadUnsignedOperand(operands, 2, scale));
                break;
            case JsOpCode.LdaNamedProperty:
            case JsOpCode.StaNamedProperty:
            case JsOpCode.LdaKeyedProperty:
            case JsOpCode.StaKeyedProperty:
            case JsOpCode.CreateFunctionContext:
            case JsOpCode.CreateFunctionContextWithCells:
                registers.Add(BytecodeInfo.ReadUnsignedOperand(operands, 0, scale));
                break;
            case JsOpCode.TestEqualStrict:
                registers.Add(BytecodeInfo.ReadUnsignedOperand(operands, 0, scale));
                break;
            case JsOpCode.LdaNamedPropertyWide:
            case JsOpCode.StaNamedPropertyWide:
            case JsOpCode.InitializeNamedProperty:
            case JsOpCode.CreateFunctionContextWithCellsWide:
                registers.Add(ReadU16(bytecode, opcodePc + 1));
                break;
            case JsOpCode.DefineOwnKeyedProperty:
                registers.Add(BytecodeInfo.ReadUnsignedOperand(operands, 0, scale));
                registers.Add(BytecodeInfo.ReadUnsignedOperand(operands, 1, scale));
                break;
            case JsOpCode.InitializeArrayElement:
                registers.Add(bytecode[opcodePc + 1] | (bytecode[opcodePc + 2] << 8));
                break;
        }

        if (registers.Count == 0)
            return null;

        var result = registers.ToArray();
        Array.Sort(result);
        return result;
    }

    private static void AddCallRegisters(HashSet<int> registers, int functionRegister, int argStart, int argCount)
    {
        registers.Add(functionRegister);
        AddRegisterRange(registers, argStart, argCount);
    }

    private static void AddRegisterRange(HashSet<int> registers, int startRegister, int count)
    {
        for (var i = 0; i < count; i++)
            registers.Add(startRegister + i);
    }

    private static int ReadU16(byte[] bytecode, int index)
    {
        return bytecode[index] | (bytecode[index + 1] << 8);
    }
}
