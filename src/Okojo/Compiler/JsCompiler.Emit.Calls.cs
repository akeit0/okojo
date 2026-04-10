using Okojo.Bytecode;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private static BytecodeInfo.OperandScale GetOperandScale(int operand0, int operand1, int operand2)
    {
        var max = Math.Max(operand0, Math.Max(operand1, operand2));
        return GetOperandScaleFromMax(max);
    }

    private static BytecodeInfo.OperandScale GetOperandScale(int operand0, int operand1, int operand2, int operand3)
    {
        var max = Math.Max(Math.Max(operand0, operand1), Math.Max(operand2, operand3));
        return GetOperandScaleFromMax(max);
    }

    private static BytecodeInfo.OperandScale GetOperandScaleFromMax(int max)
    {
        return max <= byte.MaxValue
            ? BytecodeInfo.OperandScale.Single
            : BytecodeInfo.OperandScale.Wide;
    }

    private void EmitPrefixedOperand(JsOpCode op, BytecodeInfo.OperandScale scale, params int[] operands)
    {
        if (scale != BytecodeInfo.OperandScale.Single)
            builder.Emit(BytecodeInfo.GetOperandScalePrefix(scale));

        Span<byte> buffer = stackalloc byte[Math.Max(4, operands.Length * (int)scale)];
        var cursor = 0;
        for (var i = 0; i < operands.Length; i++)
        {
            var value = operands[i];
            switch (scale)
            {
                case BytecodeInfo.OperandScale.Single:
                    if ((uint)value > byte.MaxValue)
                        throw new InvalidOperationException($"{op} operand exceeds byte operand capacity.");
                    buffer[cursor++] = (byte)value;
                    break;
                case BytecodeInfo.OperandScale.Wide:
                    if ((uint)value > ushort.MaxValue)
                        throw new InvalidOperationException($"{op} operand exceeds ushort operand capacity.");
                    buffer[cursor++] = (byte)value;
                    buffer[cursor++] = (byte)(value >> 8);
                    break;
                case BytecodeInfo.OperandScale.ExtraWide:
                    buffer[cursor++] = (byte)value;
                    buffer[cursor++] = (byte)(value >> 8);
                    buffer[cursor++] = (byte)(value >> 16);
                    buffer[cursor++] = (byte)(value >> 24);
                    break;
            }
        }

        builder.Emit(op, buffer[..cursor]);
    }

    private void EmitCallUndefinedReceiver(int functionRegister, int argumentStart, int argumentCount)
    {
        EmitPrefixedOperand(JsOpCode.CallUndefinedReceiver,
            GetOperandScale(functionRegister, argumentStart, argumentCount),
            functionRegister, argumentStart, argumentCount);
    }

    private void EmitCallProperty(int functionRegister, int objectRegister, int argumentStart, int argumentCount)
    {
        EmitPrefixedOperand(JsOpCode.CallProperty,
            GetOperandScale(functionRegister, objectRegister, argumentStart, argumentCount),
            functionRegister, objectRegister, argumentStart, argumentCount);
    }

    private void EmitCallRuntime(RuntimeId runtimeId, int argumentStart, int argumentCount)
    {
        EmitPrefixedOperand(JsOpCode.CallRuntime,
            GetOperandScale((byte)runtimeId, argumentStart, argumentCount),
            (byte)runtimeId, argumentStart, argumentCount);
    }

    private void EmitConstruct(int functionRegister, int argumentStart, int argumentCount)
    {
        EmitPrefixedOperand(JsOpCode.Construct,
            GetOperandScale(functionRegister, argumentStart, argumentCount),
            functionRegister, argumentStart, argumentCount);
    }
}
