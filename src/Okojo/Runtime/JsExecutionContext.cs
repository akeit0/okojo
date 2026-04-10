namespace Okojo.Runtime;

public readonly struct JsExecutionContext(JsRealm realm, CallFrameKind frameKind, string functionName)
{
    public JsRealm Realm { get; } = realm;
    public CallFrameKind FrameKind { get; } = frameKind;
    public string FunctionName { get; } = functionName;
}
