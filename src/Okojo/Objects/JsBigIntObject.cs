namespace Okojo.Objects;

public sealed class JsBigIntObject : JsObject
{
    public JsBigIntObject(JsRealm realm, JsBigInt value, JsObject? prototype = null) : base(realm)
    {
        Value = value;
        Prototype = prototype ?? realm.BigIntPrototype;
    }

    public JsBigInt Value { get; }
}
