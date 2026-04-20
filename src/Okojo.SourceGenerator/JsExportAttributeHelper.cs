using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal enum JsMemberNamingPolicy
{
    LowerCamelCase = 0,
    PascalCase = 1,
    AsDeclared = 2
}

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

    public static string GetMemberName(ISymbol symbol, JsMemberNamingPolicy naming, params AttributeData?[] attributes)
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

        return ApplyMemberNaming(symbol.Name, naming);
    }

    public static JsMemberNamingPolicy GetMemberNaming(AttributeData? attribute)
    {
        if (attribute is null)
            return JsMemberNamingPolicy.LowerCamelCase;

        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == "MemberNaming" && TryGetEnumValue(pair.Value.Value, out var value))
                return value switch
                {
                    1 => JsMemberNamingPolicy.PascalCase,
                    2 => JsMemberNamingPolicy.AsDeclared,
                    _ => JsMemberNamingPolicy.LowerCamelCase
                };

        return JsMemberNamingPolicy.LowerCamelCase;
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

    public static string ApplyMemberNaming(string name, JsMemberNamingPolicy naming)
    {
        return naming switch
        {
            JsMemberNamingPolicy.PascalCase => ToPascalCase(name),
            JsMemberNamingPolicy.AsDeclared => name,
            _ => ToLowerCamelCase(name)
        };
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ToLowerCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return name;
        if (name.Length == 1)
            return char.ToLowerInvariant(name[0]).ToString();

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string ToPascalCase(string name)
    {
        if (name.Length == 0 || !char.IsLower(name[0]))
            return name;
        if (name.Length == 1)
            return char.ToUpperInvariant(name[0]).ToString();

        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static bool TryGetEnumValue(object? value, out int enumValue)
    {
        switch (value)
        {
            case byte byteValue:
                enumValue = byteValue;
                return true;
            case sbyte sbyteValue:
                enumValue = sbyteValue;
                return true;
            case short shortValue:
                enumValue = shortValue;
                return true;
            case ushort ushortValue:
                enumValue = ushortValue;
                return true;
            case int intValue:
                enumValue = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                enumValue = (int)uintValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                enumValue = (int)longValue;
                return true;
            case ulong ulongValue when ulongValue <= int.MaxValue:
                enumValue = (int)ulongValue;
                return true;
        }

        enumValue = default;
        return false;
    }
}
