using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal static class JsExportAttributeHelper
{
    public static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return GetAttribute(symbol, metadataName) is not null;
    }

    public static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == metadataName)
                return attribute;

        return null;
    }

    public static string GetMemberName(ISymbol symbol, params AttributeData?[] attributes)
    {
        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];
            if (attribute is null)
                continue;

            var explicitName = GetConstructorString(attribute, 0) ?? GetNamedString(attribute, "Name");
            if (explicitName is not null)
                return explicitName;
        }

        return ToDefaultMemberName(symbol.Name);
    }

    public static string? GetConstructorString(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index)
            return null;

        return NormalizeString(attribute.ConstructorArguments[index].Value as string);
    }

    public static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name)
                return NormalizeString(pair.Value.Value as string);

        return null;
    }

    public static int? GetNamedInt(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name && pair.Value.Value is int value)
                return value;

        return null;
    }

    public static bool GetNamedBool(AttributeData attribute, string name, bool fallback = false)
    {
        return TryGetNamedBool(attribute, name, out var value) ? value : fallback;
    }

    public static bool TryGetNamedBool(AttributeData attribute, string name, out bool value)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name && pair.Value.Value is bool boolValue)
            {
                value = boolValue;
                return true;
            }

        value = default;
        return false;
    }

    public static string ToDefaultMemberName(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return name;
        if (name.Length == 1)
            return char.ToLowerInvariant(name[0]).ToString();

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
