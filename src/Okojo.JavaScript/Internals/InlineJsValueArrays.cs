using System.Runtime.InteropServices;

namespace Okojo.Internals;

internal struct InlineJsValueArray2
{
    public JsValue Item0;
    public JsValue Item1;

    public Span<JsValue> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref Item0, 2);
    }
}

internal struct InlineJsValueArray3
{
    public JsValue Item0;
    public JsValue Item1;
    public JsValue Item2;

    public Span<JsValue> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref Item0, 3);
    }
}

internal struct InlineJsValueArray4
{
    public JsValue Item0;
    public JsValue Item1;
    public JsValue Item2;
    public JsValue Item3;

    public Span<JsValue> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref Item0, 4);
    }
}

internal struct InlineJsValueArray5
{
    public JsValue Item0;
    public JsValue Item1;
    public JsValue Item2;
    public JsValue Item3;
    public JsValue Item4;

    public Span<JsValue> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref Item0, 5);
    }
}

internal struct InlineJsValueArray1
{
    public JsValue Item0;

    public Span<JsValue> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref Item0, 1);
    }
}
