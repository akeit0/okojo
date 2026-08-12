using Okojo.Globalization;

namespace Okojo.Runtime;

internal sealed class JsListFormatObject : JsObject
{
    private readonly ListFormatCore core;

    internal JsListFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string type,
        string style) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Type = type;
        Style = style;
        core = new(locale, type, style);
    }

    internal string Locale { get; }
    internal string Type { get; }
    internal string Style { get; }

    internal string Format(IReadOnlyList<string> items)
    {
        return core.Format(items);
    }

    internal JsArray FormatToParts(IReadOnlyList<string> items)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;
        foreach (var part in core.FormatToParts(items))
            result.SetElement(index++, JsValue.FromObject(CreatePartObject(part.Type, part.Value)));
        return result;
    }

    private JsPlainObject CreatePartObject(string type, string value)
    {
        var part = new JsPlainObject(Realm)
        {
            Prototype = Realm.ObjectPrototype
        };
        part.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("type"), JsValue.FromString(type),
            JsShapePropertyFlags.Open);
        part.DefineDataPropertyAtom(Realm, Realm.Atoms.InternNoCheck("value"), JsValue.FromString(value),
            JsShapePropertyFlags.Open);
        return part;
    }
}
