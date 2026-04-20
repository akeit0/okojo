using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.GlobalGeneration;

internal static class GlobalExportCollector
{
    public static GlobalTypeModel? Collect(INamedTypeSymbol symbol)
    {
        var typeAttribute = JsExportAttributeHelper.GetAttribute(symbol, AttributeMetadataNames.GenerateJsGlobalsAttribute);
        if (typeAttribute is null)
            return null;

        var installerMethodName =
            JsExportAttributeHelper.GetNamedString(typeAttribute, "InstallerMethodName") ?? "InstallGeneratedGlobals";
        var propertySourceMethodName =
            JsExportAttributeHelper.GetNamedString(typeAttribute, "PropertySourceMethodName") ??
            "GetGeneratedGlobalProperties";
        var memberNaming = JsExportAttributeHelper.GetMemberNaming(typeAttribute);
        var functions = new List<GlobalFunctionModel>();
        var properties = new List<GlobalPropertyModel>();

        foreach (var member in symbol.GetMembers())
        {
            if (JsExportAttributeHelper.HasAttribute(member, AttributeMetadataNames.JsIgnoreFromGlobalsAttribute))
                continue;

            var jsMemberAttribute = JsExportAttributeHelper.GetAttribute(member, AttributeMetadataNames.JsMemberAttribute);
            if (member is IMethodSymbol method)
            {
                var functionAttribute =
                    JsExportAttributeHelper.GetAttribute(method, AttributeMetadataNames.JsGlobalFunctionAttribute);
                if ((functionAttribute is null && jsMemberAttribute is null) || !ShouldEmitMethod(method))
                    continue;

                var name = JsExportAttributeHelper.GetMemberName(method, memberNaming, functionAttribute, jsMemberAttribute);
                var isConstructor = functionAttribute is not null &&
                                    JsExportAttributeHelper.GetNamedBool(functionAttribute, "IsConstructor");
                var length = functionAttribute is not null
                    ? JsExportAttributeHelper.GetNamedInt(functionAttribute, "Length") ?? ComputeFunctionLength(method)
                    : ComputeFunctionLength(method);
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
                var propertyAttribute =
                    JsExportAttributeHelper.GetAttribute(property, AttributeMetadataNames.JsGlobalPropertyAttribute);
                if (propertyAttribute is null && jsMemberAttribute is null)
                    continue;

                var name = JsExportAttributeHelper.GetMemberName(property, memberNaming, propertyAttribute, jsMemberAttribute);
                var writable = GetWritable(propertyAttribute, jsMemberAttribute is not null, property.SetMethod is not null);
                properties.Add(new(
                    name,
                    property,
                    property.Type,
                    writable,
                    propertyAttribute is not null
                        ? JsExportAttributeHelper.GetNamedBool(propertyAttribute, "Enumerable", true)
                        : true,
                    propertyAttribute is not null
                        ? JsExportAttributeHelper.GetNamedBool(propertyAttribute, "Configurable", true)
                        : true));
                continue;
            }

            if (member is IFieldSymbol field)
            {
                var propertyAttribute =
                    JsExportAttributeHelper.GetAttribute(field, AttributeMetadataNames.JsGlobalPropertyAttribute);
                if (propertyAttribute is null && jsMemberAttribute is null)
                    continue;

                var name = JsExportAttributeHelper.GetMemberName(field, memberNaming, propertyAttribute, jsMemberAttribute);
                var writable = GetWritable(propertyAttribute, jsMemberAttribute is not null, !field.IsReadOnly);
                properties.Add(new(
                    name,
                    field,
                    field.Type,
                    writable,
                    propertyAttribute is not null
                        ? JsExportAttributeHelper.GetNamedBool(propertyAttribute, "Enumerable", true)
                        : true,
                    propertyAttribute is not null
                        ? JsExportAttributeHelper.GetNamedBool(propertyAttribute, "Configurable", true)
                        : true));
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

    private static bool GetWritable(AttributeData? propertyAttribute, bool wasAnnotatedWithJsMember, bool canWrite)
    {
        if (propertyAttribute is not null &&
            JsExportAttributeHelper.TryGetNamedBool(propertyAttribute, "Writable", out var writable))
            return writable && canWrite;

        return wasAnnotatedWithJsMember && canWrite;
    }
}
