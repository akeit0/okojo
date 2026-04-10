using Microsoft.CodeAnalysis;
using Okojo.SourceGenerator.GlobalGeneration;
using Okojo.SourceGenerator.ObjectGeneration;

namespace Okojo.DocGenerator.Cli;

internal static class DocAttributeReader
{
    public static DocDeclarationInfo ReadDeclarationInfo(ISymbol symbol)
    {
        var attribute = GetAttribute(symbol, DocGenerationNames.DocDeclarationAttribute);
        if (attribute is null)
            return DocDeclarationInfo.Empty;

        return new()
        {
            FileName = GetConstructorString(attribute, 0) ?? GetNamedString(attribute, "FileName"),
            Namespace = GetConstructorString(attribute, 1) ?? GetNamedString(attribute, "Namespace")
        };
    }

    public static bool HasDocIgnore(ISymbol symbol)
    {
        return GetAttribute(symbol, DocGenerationNames.DocIgnoreAttribute) is not null;
    }

    public static GlobalTypeModel? Filter(GlobalTypeModel? model)
    {
        if (model is null || HasDocIgnore(model.Symbol))
            return null;

        var functions = model.Functions
            .Where(static x => !HasDocIgnore(x.Symbol))
            .ToArray();
        var properties = model.Properties
            .Where(static x => !HasDocIgnore(x.Symbol))
            .ToArray();

        return new(model.Symbol, model.InstallerMethodName, model.PropertySourceMethodName, functions, properties);
    }

    public static JsObjectTypeModel? Filter(JsObjectTypeModel? model)
    {
        if (model is null || HasDocIgnore(model.Symbol))
            return null;

        var instanceMembers = model.InstanceMembers
            .Where(static x => !HasDocIgnore(x.Symbol))
            .ToArray();
        var staticMembers = model.StaticMembers
            .Where(static x => !HasDocIgnore(x.Symbol))
            .ToArray();

        return new(model.Symbol, instanceMembers, staticMembers);
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string fullName)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == fullName)
                return attribute;

        return null;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name && pair.Value.Value is string value && value.Length != 0)
                return value;

        return null;
    }

    private static string? GetConstructorString(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index)
            return null;
        return attribute.ConstructorArguments[index].Value as string;
    }
}
