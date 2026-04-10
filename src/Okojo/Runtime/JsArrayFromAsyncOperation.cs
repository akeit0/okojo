namespace Okojo.Runtime;

internal sealed class JsArrayFromAsyncOperation
{
    public JsObject? ArrayLike;
    public long ArrayLikeLength;
    public bool AwaitInputValue;
    public long Index;
    public JsObject? Iterator;
    public JsFunction? MapFunction;
    public JsFunction? NextMethod;
    public required JsPromiseObject Promise;
    public JsObject Target = null!;
    public JsValue ThisArg = JsValue.Undefined;

    public bool IsArrayLike => ArrayLike is not null;
}
