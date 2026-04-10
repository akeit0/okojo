namespace Okojo.Objects;

public sealed class JsBooleanObject : JsObject
{
    public JsBooleanObject(JsRealm realm, bool value, JsObject? prototype = null) : base(realm)
    {
        Value = value;
        Prototype = prototype ?? realm.BooleanPrototype;
    }

    public bool Value { get; }
}
