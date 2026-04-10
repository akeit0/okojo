namespace Okojo.Objects;

public sealed class JsNumberObject : JsObject
{
    public JsNumberObject(JsRealm realm, double value, JsObject? prototype = null) : base(realm)
    {
        Value = value;
        Prototype = prototype ?? realm.NumberPrototype;
    }

    public double Value { get; }
}
