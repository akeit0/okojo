using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.GlobalGeneration;

internal static class GlobalExportCollector
{
    public static GlobalTypeModel? Collect(INamedTypeSymbol symbol)
    {
        var typeAttribute = GetAttribute(symbol, GlobalGenerationNames.GenerateJsGlobalsAttribute);
        if (typeAttribute is null)
            return null;

        var installerMethodName = GetNamedString(typeAttribute, "InstallerMethodName") ?? "InstallGeneratedGlobals";
        var propertySourceMethodName =
            GetNamedString(typeAttribute, "PropertySourceMethodName") ?? "GetGeneratedGlobalProperties";
        var functions = new List<GlobalFunctionModel>();
        var properties = new List<GlobalPropertyModel>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                var functionAttribute = GetAttribute(method, GlobalGenerationNames.JsGlobalFunctionAttribute);
                if (functionAttribute is null || !ShouldEmitMethod(method))
                    continue;

                var name = GetConstructorString(functionAttribute, 0) ?? method.Name;
                var isConstructor = GetNamedBool(functionAttribute, "IsConstructor");
                var length = GetNamedInt(functionAttribute, "Length") ?? ComputeFunctionLength(method);
                var parameters = method.Parameters
                    .Select(static p => new GlobalParameterModel(
                        p.Name,
                        p.Type,
                        p.HasExplicitDefaultValue,
                        p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null))
                    .ToArray();
                functions.Add(new(
                    name,
                    method,
                    length,
                    isConstructor,
                    parameters));
                continue;
            }

            if (member is IPropertySymbol property && property.Parameters.Length == 0)
            {
                var propertyAttribute = GetAttribute(property, GlobalGenerationNames.JsGlobalPropertyAttribute);
                if (propertyAttribute is null)
                    continue;

                var name = GetConstructorString(propertyAttribute, 0) ?? property.Name;
                var writable = GetNamedBool(propertyAttribute, "Writable") && property.SetMethod is not null;
                properties.Add(new(
                    name,
                    property,
                    property.Type,
                    writable,
                    GetNamedBool(propertyAttribute, "Enumerable", true),
                    GetNamedBool(propertyAttribute, "Configurable", true)));
                continue;
            }

            if (member is IFieldSymbol field)
            {
                var propertyAttribute = GetAttribute(field, GlobalGenerationNames.JsGlobalPropertyAttribute);
                if (propertyAttribute is null)
                    continue;

                var name = GetConstructorString(propertyAttribute, 0) ?? field.Name;
                var writable = GetNamedBool(propertyAttribute, "Writable") && !field.IsReadOnly;
                properties.Add(new(
                    name,
                    field,
                    field.Type,
                    writable,
                    GetNamedBool(propertyAttribute, "Enumerable", true),
                    GetNamedBool(propertyAttribute, "Configurable", true)));
            }
        }

        return new(symbol, installerMethodName, propertySourceMethodName, functions, properties);
    }

    private static bool ShouldEmitMethod(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary || method.IsGenericMethod)
            return false;
        if (!ParameterTypeSupport.HasSupportedReadOnlySpanShape(method.Parameters))
            return false;

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None || parameter.IsParams)
                return false;

        return true;
    }

    private static int ComputeFunctionLength(IMethodSymbol method)
    {
        return ParameterTypeSupport.ComputeFunctionLength(method.Parameters, true);
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == metadataName);
    }

    private static string? GetConstructorString(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length > index &&
               attribute.ConstructorArguments[index].Value is string value
            ? value
            : null;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        return attribute.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as string;
    }

    private static int? GetNamedInt(AttributeData attribute, string name)
    {
        return attribute.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as int?;
    }

    private static bool GetNamedBool(AttributeData attribute, string name, bool fallback = false)
    {
        return attribute.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as bool? ?? fallback;
    }
}
