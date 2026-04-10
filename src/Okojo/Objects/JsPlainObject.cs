namespace Okojo.Objects;

public sealed class JsPlainObject : JsObject
{
    public JsPlainObject(JsRealm realm, bool assignDefaultPrototype = true, bool useDictionaryMode = false)
        : base(realm, useDictionaryMode)
    {
        if (assignDefaultPrototype)
            Prototype = realm.ObjectPrototype;
    }

    public JsPlainObject(StaticNamedPropertyLayout shape, bool assignDefaultPrototype = true) : base(shape)
    {
        if (assignDefaultPrototype)
            Prototype = shape.Owner.ObjectPrototype;
    }

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
