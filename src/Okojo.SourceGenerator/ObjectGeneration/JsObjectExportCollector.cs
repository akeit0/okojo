using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.ObjectGeneration;

internal static class JsObjectExportCollector
{
    public static JsObjectTypeModel? Collect(INamedTypeSymbol symbol)
    {
        var typeAttribute = JsExportAttributeHelper.GetAttribute(symbol, AttributeMetadataNames.GenerateJsObjectAttribute);
        if (typeAttribute is null)
            return null;
        var memberNaming = JsExportAttributeHelper.GetMemberNaming(typeAttribute);

        var instanceMembers = new List<JsObjectMemberModel>();
        var staticMembers = new List<JsObjectMemberModel>();

        foreach (var member in symbol.GetMembers())
        {
            if (IsGeneratedSourceMember(member))
                continue;
            if (JsExportAttributeHelper.HasAttribute(member, AttributeMetadataNames.JsIgnoreFromObjectAttribute))
                continue;

            var jsMemberAttribute = JsExportAttributeHelper.GetAttribute(member, AttributeMetadataNames.JsMemberAttribute);
            if (jsMemberAttribute is null)
                continue;

            var jsName = JsExportAttributeHelper.GetMemberName(member, memberNaming, jsMemberAttribute);

            var model = member switch
            {
                IFieldSymbol field when !field.IsConst => new(
                    jsName,
                    JsObjectMemberKind.Field,
                    field.IsStatic,
                    field,
                    field.Type,
                    true,
                    !field.IsReadOnly),
                IPropertySymbol property when property.Parameters.Length == 0 => new(
                    jsName,
                    JsObjectMemberKind.Property,
                    property.IsStatic,
                    property,
                    property.Type,
                    property.GetMethod is not null,
                    property.SetMethod is not null),
                IMethodSymbol method when ShouldEmitMethod(method) => new JsObjectMemberModel(
                    jsName,
                    JsObjectMemberKind.Method,
                    method.IsStatic,
                    method,
                    method.ReturnType,
                    false,
                    false,
                    method.Parameters.Select(static x => new JsObjectParameterModel(x.Name, x.Type)).ToArray()),
                _ => null
            };

            if (model is null)
                continue;

            if (model.IsStatic)
                staticMembers.Add(model);
            else
                instanceMembers.Add(model);
        }

        return new(symbol, instanceMembers, staticMembers);
    }

    public static bool ShouldEmitMethod(IMethodSymbol method)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.UserDefinedOperator))
            return false;
        if (method.IsGenericMethod)
            return false;
        if (!ParameterTypeSupport.HasSupportedReadOnlySpanShape(method.Parameters))
            return false;

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.HasExplicitDefaultValue)
                return false;

        return true;
    }

    private static bool IsGeneratedSourceMember(ISymbol member)
    {
        var sawSourceLocation = false;
        foreach (var location in member.Locations)
        {
            if (!location.IsInSource)
                continue;

            sawSourceLocation = true;
            if (location.SourceTree?.FilePath is not string filePath || filePath.Length == 0)
                return false;
            if (!filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return sawSourceLocation;
    }
}
