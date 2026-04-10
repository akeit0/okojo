namespace Okojo.Runtime;

public readonly ref struct CallFrameRef
{
    public readonly JsRealm Realm;
    private readonly ref CallFrame frame;
    private readonly int framePointer;

    internal CallFrameRef(JsRealm realm, ref CallFrame frame, int argStart)
    {
        Realm = realm;
        this.frame = ref frame;
        framePointer = argStart - FrameLayout.HeaderSize;
    }

    public JsFunction Function => frame.Function;
    public JsContext? Context => frame.Context;
    public CallFrameKind FrameKind => frame.FrameKind;
    public CallFrameFlag Flags => frame.Flags;
    public int CallerFp => frame.CallerFp;
    public int ArgCount => frame.ArgCount;
    public int CallerPc => frame.CallerPc;

    public readonly ref readonly JsValue ThisValue => ref frame.ThisValue;
    public JsValue NewTarget => Realm.GetFrameNewTarget(framePointer);

    public ReadOnlySpan<JsValue> Arguments => Realm.Stack.AsSpan(framePointer + FrameLayout.HeaderSize, ArgCount);
}
