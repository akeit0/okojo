namespace Okojo.Runtime;

public sealed class WorkerHostBinding
{
    public required JsAgent Agent { get; init; }
    public required JsRealm Realm { get; init; }
    public required Func<string, JsValue> Eval { get; init; }
    public required Func<JsRealm, string, JsValue> LoadModule { get; init; }
    public required Action<JsRealm> Pump { get; init; }
    public required Action Terminate { get; init; }
}
