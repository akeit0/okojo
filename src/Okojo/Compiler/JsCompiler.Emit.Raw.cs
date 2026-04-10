using Okojo.Bytecode;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private static byte EmitOperand(int value)
    {
        if ((uint)value > byte.MaxValue)
            throw new InvalidOperationException("Bytecode operand exceeds byte capacity.");
        return (byte)value;
    }

    private void EmitRaw(JsOpCode opcode)
    {
        builder.Emit(opcode);
    }

    private void EmitRaw(JsOpCode opcode, int operand0)
    {
        builder.Emit(opcode, EmitOperand(operand0));
    }

    private void EmitRaw(JsOpCode opcode, int operand0, int operand1)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1));
    }

    private void EmitRaw(JsOpCode opcode, int operand0, int operand1, int operand2)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1), EmitOperand(operand2));
    }

    private void EmitRaw(JsOpCode opcode, int operand0, int operand1, int operand2, int operand3)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1), EmitOperand(operand2),
            EmitOperand(operand3));
    }

    private void EmitRaw(JsOpCode opcode, int operand0, int operand1, int operand2, int operand3, int operand4)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1), EmitOperand(operand2), EmitOperand(operand3),
            EmitOperand(operand4));
    }

    private void EmitRaw(JsOpCode opcode, int operand0, int operand1, int operand2, int operand3, int operand4,
        int operand5)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1), EmitOperand(operand2), EmitOperand(operand3),
            EmitOperand(operand4), EmitOperand(operand5));
    }

    private void EmitRaw(
        JsOpCode opcode,
        int operand0,
        int operand1,
        int operand2,
        int operand3,
        int operand4,
        int operand5,
        int operand6)
    {
        builder.Emit(opcode, EmitOperand(operand0), EmitOperand(operand1), EmitOperand(operand2), EmitOperand(operand3),
            EmitOperand(operand4), EmitOperand(operand5), EmitOperand(operand6));
    }
}
