namespace Okojo.Objects;

public sealed class JsSymbolObject : JsObject
{
    public JsSymbolObject(JsRealm realm, Symbol value, JsObject? prototype = null) : base(realm)
    {
        Value = value;
        Prototype = prototype ?? realm.SymbolPrototype;
    }

    public Symbol Value { get; }
}
