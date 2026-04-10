using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private int exceptionHandlerCount;

    private ExceptionHandlerEntry[] exceptionHandlerStack = new ExceptionHandlerEntry[16];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearExceptionHandlers()
    {
        exceptionHandlerCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushExceptionHandler(int frameFp, int catchPc, int savedSp)
    {
        if ((uint)exceptionHandlerCount >= (uint)exceptionHandlerStack.Length)
            GrowExceptionHandlerStack();
        exceptionHandlerStack[exceptionHandlerCount++] = new(frameFp, catchPc, savedSp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowExceptionHandlerStack()
    {
        Array.Resize(ref exceptionHandlerStack, exceptionHandlerStack.Length * 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPeekExceptionHandler(out ExceptionHandlerEntry entry)
    {
        if (exceptionHandlerCount == 0)
        {
            entry = default;
            return false;
        }

        entry = exceptionHandlerStack[exceptionHandlerCount - 1];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PopExceptionHandler()
    {
        if (exceptionHandlerCount != 0)
            exceptionHandlerCount--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PopCurrentExceptionHandlerForFrame(int frameFp)
    {
        if (exceptionHandlerCount != 0 && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp)
        {
            exceptionHandlerCount--;
            return;
        }

        PopCurrentExceptionHandlerForFrameSlow(frameFp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PopCurrentExceptionHandlerForFrameSlow(int frameFp)
    {
        for (var i = exceptionHandlerCount - 1; i >= 0; i--)
        {
            if (exceptionHandlerStack[i].FrameFp != frameFp)
                continue;

            exceptionHandlerCount--;
            if (i != exceptionHandlerCount)
                Array.Copy(exceptionHandlerStack, i + 1, exceptionHandlerStack, i, exceptionHandlerCount - i);

            return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveExceptionHandlersForFrame(int frameFp)
    {
        while (exceptionHandlerCount != 0 && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp)
            exceptionHandlerCount--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasActiveExceptionHandlersForFrame(int frameFp)
    {
        return exceptionHandlerCount != 0 && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GeneratorObjectCore.SuspendedExceptionHandler[]? CaptureExceptionHandlersForFrame(int frameFp)
    {
        var count = 0;
        for (var i = exceptionHandlerCount - 1; i >= 0 && exceptionHandlerStack[i].FrameFp == frameFp; i--)
            count++;

        if (count == 0)
            return null;

        var handlers = new GeneratorObjectCore.SuspendedExceptionHandler[count];
        var source = exceptionHandlerCount - 1;
        for (var dest = count - 1; dest >= 0; dest--, source--)
        {
            var entry = exceptionHandlerStack[source];
            handlers[dest] = new(entry.CatchPc,
                entry.SavedSp - frameFp);
        }

        return handlers;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RestoreExceptionHandlersForFrame(int frameFp,
        GeneratorObjectCore.SuspendedExceptionHandler[]? handlers)
    {
        if (handlers is null || handlers.Length == 0)
            return;

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i];
            PushExceptionHandler(frameFp, handler.CatchPc, frameFp + handler.SavedSpOffset);
        }
    }

    private bool TryHandleJsRuntimeException(
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        out int pc)
    {
        while (TryPeekExceptionHandler(out var topHandler) && topHandler.FrameFp > fp)
            PopExceptionHandler();

        for (var i = exceptionHandlerCount - 1; i >= 0; i--)
        {
            var handler = exceptionHandlerStack[i];
            if (stopAtCallerFp >= 0 && handler.FrameFp <= stopAtCallerFp)
                continue;

            while (fp > handler.FrameFp)
            {
                var poppedFp = fp;
                ref var poppedFrame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[poppedFp]);
                var top = StackTop;
                StackTop = poppedFp;
                fp = poppedFrame.CallerFp;
                pc = poppedFrame.CallerPc;
                fullStack[StackTop..top].Fill(JsValue.Undefined);
                RemoveExceptionHandlersForFrame(poppedFp);
                if (TryGetActiveGeneratorForFrame(poppedFp, out var poppedGenerator))
                {
                    FinalizeGenerator(poppedGenerator);
                    ClearActiveGeneratorForFrame(poppedFp);
                }
            }

            if (fp != handler.FrameFp)
                continue;

            exceptionHandlerCount = i;
            StackTop = handler.SavedSp;
            if (StackTop < fp + HeaderSize)
                StackTop = fp + HeaderSize;
            pc = handler.CatchPc;
            return true;
        }

        pc = 0;
        return false;
    }

    private readonly struct ExceptionHandlerEntry(int frameFp, int catchPc, int savedSp)
    {
        public readonly int FrameFp = frameFp;
        public readonly int CatchPc = catchPc;
        public readonly int SavedSp = savedSp;
    }
}
