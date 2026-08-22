using System.Globalization;
using Okojo.Globalization;

namespace Okojo.Runtime;

internal sealed class JsRelativeTimeFormatObject : JsObject
{
    private readonly RelativeTimeFormat core;

    internal JsRelativeTimeFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string numberingSystem,
        string style,
        string numeric,
        CultureInfo cultureInfo
    )
        : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        NumberingSystem = numberingSystem;
        Style = style;
        Numeric = numeric;
        CultureInfo = cultureInfo;
        core = new(locale, numberingSystem, style, numeric, cultureInfo);
    }

    internal string Locale { get; }
    internal string NumberingSystem { get; }
    internal string Style { get; }
    internal string Numeric { get; }
    internal CultureInfo CultureInfo { get; }

    internal string Format(double value, string unit)
    {
        return core.Format(value, unit);
    }

    internal JsArray FormatToParts(double value, string unit)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;
        foreach (var part in core.FormatToParts(value, unit))
        {
            var obj = new JsPlainObject(Realm) { Prototype = Realm.ObjectPrototype };
            obj.DefineDataPropertyAtom(
                Realm,
                Realm.Atoms.InternNoCheck("type"),
                JsValue.FromString(part.Type),
                JsShapePropertyFlags.Open
            );
            obj.DefineDataPropertyAtom(
                Realm,
                Realm.Atoms.InternNoCheck("value"),
                JsValue.FromString(part.Value),
                JsShapePropertyFlags.Open
            );
            if (part.Unit is not null)
                obj.DefineDataPropertyAtom(
                    Realm,
                    Realm.Atoms.InternNoCheck("unit"),
                    JsValue.FromString(part.Unit),
                    JsShapePropertyFlags.Open
                );
            result.SetElement(index++, JsValue.FromObject(obj));
        }

        return result;
    }
}
