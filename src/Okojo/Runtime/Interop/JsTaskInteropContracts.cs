namespace Okojo.Runtime.Interop;

public sealed class PromiseRejectedException(JsValue reason) : Exception("JavaScript promise rejected.")
{
    public JsValue Reason { get; } = reason;
}

public interface IJsCancelReasonProvider
{
    bool TryGetCancelReason(out JsValue reason);
}

internal enum JsTaskCancellationPolicy : byte
{
    RejectWithHostException = 0,
    RejectWithProviderReasonOrHostException = 1
}
