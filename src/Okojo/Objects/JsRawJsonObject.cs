namespace Okojo.Objects;

internal sealed class JsRawJsonObject : JsObject
{
    internal JsRawJsonObject(JsRealm realm, string rawJson)
        : base(realm)
    {
        RawJson = rawJson;
        Prototype = null;
        const int atomRawJson = IdRawJson;
        DefineDataPropertyAtom(realm, atomRawJson, JsValue.FromString(rawJson), JsShapePropertyFlags.Open);
        PreventExtensions();
    }

    internal string RawJson { get; }
}
