namespace Okojo.Runtime;

public sealed class JsFatalRuntimeException(
    JsErrorKind kind,
    string message,
    string? detailCode = null,
    JsValue? thrownValue = null,
    Exception? innerException = null)
    : JsRuntimeException(kind, message, detailCode, thrownValue, innerException);
