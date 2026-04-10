namespace Okojo.Objects;

internal sealed class JsDateObject : JsObject
{
    internal JsDateObject(JsRealm realm, double timeValue) : base(realm)
    {
        TimeValue = timeValue;
        Prototype = realm.DatePrototype;
    }

    internal double TimeValue { get; set; }
}
