namespace Okojo.Runtime.Interop;

public sealed class HostIndexerBinding(
    Func<JsRealm, object, uint, (bool Success, JsValue Value)> getter,
    Func<JsRealm, object, uint, JsValue, bool>? setter,
    Action<object, List<uint>>? collectOwnIndices)
{
    public Func<JsRealm, object, uint, (bool Success, JsValue Value)> Getter { get; } = getter;
    public Func<JsRealm, object, uint, JsValue, bool>? Setter { get; } = setter;
    public Action<object, List<uint>>? CollectOwnIndices { get; } = collectOwnIndices;
}
