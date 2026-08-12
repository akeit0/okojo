using System.Text;

namespace Okojo.Globalization;

/// <summary>
///     Portable ECMA-402 list format: pattern selection and string formatting.
/// </summary>
public sealed class ListFormat
{
    public ListFormat(string locale, ListFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        options ??= new();
        Locale = locale;
        Type = options.Type;
        Style = options.Style;
    }

    public ListFormat(string locale, string type, string style)
        : this(locale, new ListFormatOptions { Type = type, Style = style })
    {
    }

    public string Locale { get; }
    public string Type { get; }
    public string Style { get; }

    public ListPattern GetPattern()
    {
        var isSpanish = Locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        var isEnglish = Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        if (isSpanish)
        {
            if (string.Equals(Type, "unit", StringComparison.Ordinal))
                return Style switch
                {
                    "narrow" => new(" ", " ", " "),
                    "short" => new(", ", " y ", ", "),
                    _ => new(", ", " y ", " y ")
                };

            if (string.Equals(Type, "disjunction", StringComparison.Ordinal))
                return new(", ", " o ", " o ");

            return Style switch
            {
                "narrow" => new(", ", " y ", " y "),
                "short" => new(", ", " y ", " y "),
                _ => new(", ", " y ", " y ")
            };
        }

        if (isEnglish)
        {
            if (string.Equals(Type, "disjunction", StringComparison.Ordinal))
                return new(", ", " or ", ", or ");

            if (string.Equals(Type, "unit", StringComparison.Ordinal))
                return Style switch
                {
                    "narrow" => new(" ", " ", " "),
                    _ => new(", ", ", ", ", ")
                };

            return Style switch
            {
                "short" => new(", ", " & ", ", & "),
                _ => new(", ", " and ", ", and ")
            };
        }

        if (string.Equals(Type, "unit", StringComparison.Ordinal) &&
            string.Equals(Style, "narrow", StringComparison.Ordinal))
            return new(" ", " ", " ");

        return string.Equals(Type, "disjunction", StringComparison.Ordinal)
            ? new(", ", " or ", ", or ")
            : new(", ", " and ", ", and ");
    }

    public string Format(IReadOnlyList<string> items)
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

    /// <summary>Produces the element/literal part stream without allocating JS objects.</summary>
    public List<IntlPart> FormatToParts(IReadOnlyList<string> items)
    {
        var result = new List<IntlPart>();
        if (items.Count == 0)
            return result;

        var pattern = GetPattern();
        for (var i = 0; i < items.Count; i++)
        {
            result.Add(new IntlPart("element", items[i]));
            if (i >= items.Count - 1)
                continue;

            var separator = items.Count == 2
                ? pattern.Two
                : i == items.Count - 2
                    ? pattern.End
                    : pattern.Middle;
            result.Add(new IntlPart("literal", separator));
        }

        return result;
    }

    public readonly record struct ListPattern(string Middle, string Two, string End);
}
