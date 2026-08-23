using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Compiler;

public sealed partial class JsCompiler
{
    private void EmitCallUndefinedReceiver(
        int functionRegister,
        int argumentStart,
        int argumentCount
    )
    {
        builder.EmitCallUndefinedReceiver(functionRegister, argumentStart, argumentCount);
    }

    private void EmitCallProperty(
        int functionRegister,
        int objectRegister,
        int argumentStart,
        int argumentCount
    )
    {
        builder.EmitCallProperty(functionRegister, objectRegister, argumentStart, argumentCount);
    }

    private void EmitCallRuntime(RuntimeId runtimeId, int argumentStart, int argumentCount)
    {
        builder.EmitCallRuntime((byte)runtimeId, argumentStart, argumentCount);
    }

    private void EmitConstruct(int functionRegister, int argumentStart, int argumentCount)
    {
        builder.EmitConstruct(functionRegister, argumentStart, argumentCount);
    }
}
