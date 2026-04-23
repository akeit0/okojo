namespace Okojo.Objects;

public sealed class JsUserDataObject : JsObject
{
    public JsUserDataObject(JsRealm realm, bool assignDefaultPrototype = true, bool useDictionaryMode = false)
        : base(realm, useDictionaryMode)
    {
        if (assignDefaultPrototype)
            Prototype = realm.ObjectPrototype;
    }

    public JsUserDataObject(StaticNamedPropertyLayout shape, bool assignDefaultPrototype = true) : base(shape)
    {
        if (assignDefaultPrototype)
            Prototype = shape.Owner.ObjectPrototype;
    }

    public object? UserData;
}

public sealed class JsUserDataObject<T> : JsObject
{
    public JsUserDataObject(JsRealm realm, bool assignDefaultPrototype = true, bool useDictionaryMode = false)
        : base(realm, useDictionaryMode)
    {
        if (assignDefaultPrototype)
            Prototype = realm.ObjectPrototype;
    }

    public JsUserDataObject(StaticNamedPropertyLayout shape, bool assignDefaultPrototype = true) : base(shape)
    {
        if (assignDefaultPrototype)
            Prototype = shape.Owner.ObjectPrototype;
    }

    public T? UserData;
}
