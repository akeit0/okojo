namespace Okojo.Objects;

public sealed class JsNativeErrorObject : JsObject
{
    public JsNativeErrorObject(JsRealm realm, Exception nativeException, bool assignDefaultPrototype = true)
        : base(realm)
    {
        NativeException = nativeException ?? throw new ArgumentNullException(nameof(nativeException));
        if (assignDefaultPrototype)
            Prototype = realm.ErrorPrototype;
    }

    public Exception NativeException { get; }

    public override string ToString()
    {
        return FormatOwnEnumerablePropertiesForDisplay();
    }

    internal override string FormatForDisplay(int? indentSize, int depth, HashSet<JsObject> visited)
    {
        if (indentSize is null || indentSize <= 0)
            return FormatOwnEnumerablePropertiesForDisplay();
        return FormatOwnEnumerablePropertiesForDisplay(indentSize, depth, visited);
    }
}
