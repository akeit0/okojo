using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public readonly struct CallFrame
{
    internal readonly JsValue Value0;
    internal readonly JsValue Value1;
    internal readonly JsValue ThisValue;

    internal CallFrame(JsFunction func, int callerFp, int argCount, int callerPc, JsContext? context,
        JsValue thisValue, CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        Value0 =
            new(
                ((ulong)callerFp << FrameLayout.BitOffsetCallerFp) |
                ((ulong)argCount << FrameLayout.BitOffsetArgCount) |
                (uint)callerPc, func);
        var meta = ((ulong)flags << FrameLayout.BitOffsetFrameFlags) | (uint)frameKind;
        Value1 = new(meta, context);
        ThisValue = thisValue;
    }

    public JsFunction Function => Unsafe.As<JsFunction>(Value0.Obj!);
    public JsContext? Context => Unsafe.As<JsContext>(Value1.Obj);
    public CallFrameKind FrameKind => (CallFrameKind)(Value1.U & 0xFFFFFFFFUL);
    public CallFrameFlag Flags => (CallFrameFlag)((Value1.U >> 32) & 0xFFFFFFFFUL);
    public int CallerFp => (int)(Value0.U >> FrameLayout.BitOffsetCallerFp);
    public int ArgCount => (int)((Value0.U >> FrameLayout.BitOffsetArgCount) & 0xFFFF);
    public int CallerPc => (int)(Value0.U & 0xFFFFFFFF);
}
