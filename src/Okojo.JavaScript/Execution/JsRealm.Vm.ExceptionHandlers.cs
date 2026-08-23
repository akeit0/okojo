using System.Runtime.CompilerServices;

namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    private int exceptionHandlerCount;

    private ExceptionHandlerEntry[] exceptionHandlerStack = new ExceptionHandlerEntry[16];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearExceptionHandlers()
    {
        Array.Clear(exceptionHandlerStack, 0, exceptionHandlerCount);
        exceptionHandlerCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushExceptionHandler(
        int frameFp,
        int catchPc,
        int savedSp,
        JsContext? savedContext
    )
    {
        if ((uint)exceptionHandlerCount >= (uint)exceptionHandlerStack.Length)
            GrowExceptionHandlerStack();
        exceptionHandlerStack[exceptionHandlerCount++] = new(
            frameFp,
            catchPc,
            savedSp,
            savedContext
        );
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
            exceptionHandlerStack[--exceptionHandlerCount] = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PopCurrentExceptionHandlerForFrame(int frameFp)
    {
        if (
            exceptionHandlerCount != 0
            && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp
        )
        {
            exceptionHandlerStack[--exceptionHandlerCount] = default;
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
                Array.Copy(
                    exceptionHandlerStack,
                    i + 1,
                    exceptionHandlerStack,
                    i,
                    exceptionHandlerCount - i
                );
            exceptionHandlerStack[exceptionHandlerCount] = default;

            return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveExceptionHandlersForFrame(int frameFp)
    {
        while (
            exceptionHandlerCount != 0
            && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp
        )
            exceptionHandlerStack[--exceptionHandlerCount] = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasActiveExceptionHandlersForFrame(int frameFp)
    {
        return exceptionHandlerCount != 0
            && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp == frameFp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GeneratorObjectCore.SuspendedExceptionHandler[]? CaptureExceptionHandlersForFrame(
        int frameFp
    )
    {
        var count = 0;
        for (
            var i = exceptionHandlerCount - 1;
            i >= 0 && exceptionHandlerStack[i].FrameFp == frameFp;
            i--
        )
            count++;

        if (count == 0)
            return null;

        var handlers = new GeneratorObjectCore.SuspendedExceptionHandler[count];
        var source = exceptionHandlerCount - 1;
        for (var dest = count - 1; dest >= 0; dest--, source--)
        {
            var entry = exceptionHandlerStack[source];
            handlers[dest] = new(entry.CatchPc, entry.SavedSp - frameFp, entry.SavedContext);
        }

        return handlers;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RestoreExceptionHandlersForFrame(
        int frameFp,
        GeneratorObjectCore.SuspendedExceptionHandler[]? handlers
    )
    {
        if (handlers is null || handlers.Length == 0)
            return;

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i];
            PushExceptionHandler(
                frameFp,
                handler.CatchPc,
                frameFp + handler.SavedSpOffset,
                handler.SavedContext
            );
        }
    }

    private bool TryHandleJsRuntimeException(
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        out int pc
    )
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

            Array.Clear(exceptionHandlerStack, i, exceptionHandlerCount - i);
            exceptionHandlerCount = i;
            StackTop = handler.SavedSp;
            if (StackTop < fp + HeaderSize)
                StackTop = fp + HeaderSize;
            SetFrameContext(fullStack, fp, handler.SavedContext);
            pc = handler.CatchPc;
            return true;
        }

        pc = 0;
        return false;
    }

    private readonly struct ExceptionHandlerEntry(
        int frameFp,
        int catchPc,
        int savedSp,
        JsContext? savedContext
    )
    {
        public readonly int FrameFp = frameFp;
        public readonly int CatchPc = catchPc;
        public readonly int SavedSp = savedSp;
        public readonly JsContext? SavedContext = savedContext;
    }
}
