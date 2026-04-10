namespace Okojo.Runtime.Interop;

internal sealed class HostIndexerDescriptor
{
    internal HostIndexerDescriptor(
        Func<JsRealm, object, uint, (bool Success, JsValue Value)> getter,
        Func<JsRealm, object, uint, JsValue, bool>? setter,
        Action<object, List<uint>>? collectOwnIndices)
    {
        Getter = getter;
        Setter = setter;
        CollectOwnIndices = collectOwnIndices;
    }

    internal Func<JsRealm, object, uint, (bool Success, JsValue Value)> Getter { get; }
    internal Func<JsRealm, object, uint, JsValue, bool>? Setter { get; }
    internal Action<object, List<uint>>? CollectOwnIndices { get; }
}
