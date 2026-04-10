using System.Text;

namespace Okojo.Runtime;

internal sealed class JsListFormatObject : JsObject
{
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
    }

    internal string Locale { get; }
    internal string Type { get; }
    internal string Style { get; }

    internal string Format(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return string.Empty;
        if (items.Count == 1)
            return items[0];

        var pattern = GetPattern();
        if (items.Count == 2)
            return items[0] + pattern.Two + items[1];

        var builder = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                builder.Append(i == items.Count - 1 ? pattern.End : pattern.Middle);
            builder.Append(items[i]);
        }

        return builder.ToString();
    }

    internal JsArray FormatToParts(IReadOnlyList<string> items)
    {
        var result = Realm.CreateArrayObject();
        uint index = 0;
        if (items.Count == 0)
            return result;

        var pattern = GetPattern();
        for (var i = 0; i < items.Count; i++)
        {
            result.SetElement(index++, JsValue.FromObject(CreatePartObject("element", items[i])));
            if (i >= items.Count - 1)
                continue;

            var separator = items.Count == 2
                ? pattern.Two
                : i == items.Count - 2
                    ? pattern.End
                    : pattern.Middle;
            result.SetElement(index++, JsValue.FromObject(CreatePartObject("literal", separator)));
        }

        return result;
    }

    private (string Middle, string Two, string End) GetPattern()
    {
        var isSpanish = Locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        var isEnglish = Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        if (isSpanish)
        {
            if (string.Equals(Type, "unit", StringComparison.Ordinal))
                return Style switch
                {
                    "narrow" => (" ", " ", " "),
                    "short" => (", ", " y ", ", "),
                    _ => (", ", " y ", " y ")
                };

            if (string.Equals(Type, "disjunction", StringComparison.Ordinal))
                return (", ", " o ", " o ");

            return Style switch
            {
                "narrow" => (", ", " y ", " y "),
                "short" => (", ", " y ", " y "),
                _ => (", ", " y ", " y ")
            };
        }

        if (isEnglish)
        {
            if (string.Equals(Type, "disjunction", StringComparison.Ordinal))
                return (", ", " or ", ", or ");

            if (string.Equals(Type, "unit", StringComparison.Ordinal))
                return Style switch
                {
                    "narrow" => (" ", " ", " "),
                    _ => (", ", ", ", ", ")
                };

            return Style switch
            {
                "short" => (", ", " & ", ", & "),
                _ => (", ", " and ", ", and ")
            };
        }

        if (string.Equals(Type, "unit", StringComparison.Ordinal) &&
            string.Equals(Style, "narrow", StringComparison.Ordinal))
            return (" ", " ", " ");

        return string.Equals(Type, "disjunction", StringComparison.Ordinal)
            ? (", ", " or ", ", or ")
            : (", ", " and ", ", and ");
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
