using System.Text;
using Microsoft.CodeAnalysis;
using Okojo.SourceGenerator;
using Okojo.SourceGenerator.GlobalGeneration;
using Okojo.SourceGenerator.ObjectGeneration;

namespace Okojo.DocGenerator.Cli;

internal static class TypeScriptDeclarationEmitter
{
    public static string Emit(IReadOnlyList<GlobalTypeModel> globalModels,
        IReadOnlyList<JsObjectTypeModel> objectModels)
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var model in globalModels)
        {
            if (!first)
                sb.AppendLine();
            AppendGlobalType(sb, model);
            first = false;
        }

        foreach (var model in objectModels)
        {
            if (!first)
                sb.AppendLine();
            AppendObjectType(sb, model);
            first = false;
        }

        return sb.ToString();
    }

    private static void AppendGlobalType(StringBuilder sb, GlobalTypeModel model)
    {
        var ns = GetDocNamespace(model.Symbol);
        if (ns is not null)
            sb.Append("declare namespace ").Append(ns).AppendLine(" {");

        var indent = ns is null ? string.Empty : "    ";
        var first = true;
        foreach (var function in model.Functions)
        {
            if (!first)
                sb.AppendLine();
            first = false;
            AppendDocComment(sb, indent, XmlDocCommentReader.Read(function.Symbol),
                function.Parameters.Select(static x => x.Name).ToArray());
            sb.Append(indent)
                .Append(ns is null ? "declare function " : "function ")
                .Append(function.Name)
                .Append('(')
                .Append(string.Join(", ", function.Parameters.Select(FormatParameter)))
                .Append("): ")
                .Append(MapType(function.Symbol.ReturnType))
                .AppendLine(";");
        }

        foreach (var property in model.Properties)
        {
            if (!first)
                sb.AppendLine();
            first = false;
            AppendDocComment(sb, indent, XmlDocCommentReader.Read(property.Symbol), Array.Empty<string>());
            sb.Append(indent)
                .Append(ns is null ? "declare " : string.Empty)
                .Append(property.Writable ? "let " : "const ")
                .Append(property.Name)
                .Append(": ")
                .Append(MapType(property.Type))
                .AppendLine(";");
        }

        if (ns is not null)
            sb.AppendLine("}");
    }

    private static void AppendObjectType(StringBuilder sb, JsObjectTypeModel model)
    {
        var ns = GetDocNamespace(model.Symbol);
        if (ns is not null)
            sb.Append("declare namespace ").Append(ns).AppendLine(" {");

        var indent = ns is null ? string.Empty : "    ";
        AppendDocComment(sb, indent, XmlDocCommentReader.Read(model.Symbol), Array.Empty<string>());
        sb.Append(indent)
            .Append(ns is null ? "declare class " : "class ")
            .Append(GetDeclaredTypeName(model.Symbol))
            .Append(BuildTypeParameters(model.Symbol))
            .AppendLine(" {");

        foreach (var member in model.InstanceMembers)
            AppendObjectMember(sb, member, indent + "    ");

        foreach (var member in model.StaticMembers)
            AppendObjectMember(sb, member, indent + "    ");

        sb.Append(indent).AppendLine("}");

        if (ns is not null)
            sb.AppendLine("}");
    }

    private static void AppendObjectMember(StringBuilder sb, JsObjectMemberModel member, string indent)
    {
        AppendDocComment(sb, indent, XmlDocCommentReader.Read(member.Symbol),
            member.Parameters.Select(static x => x.Name).ToArray());
        sb.Append(indent);
        if (member.IsStatic)
            sb.Append("static ");

        if (member.Kind == JsObjectMemberKind.Method)
        {
            sb.Append(member.Name)
                .Append('(')
                .Append(string.Join(", ", member.Parameters.Select(FormatParameter)))
                .Append("): ")
                .Append(MapType(member.Type))
                .AppendLine(";");
            return;
        }

        if (member.CanRead && !member.CanWrite)
            sb.Append("readonly ");

        sb.Append(member.Name)
            .Append(": ")
            .Append(MapType(member.Type))
            .AppendLine(";");
    }

    private static string FormatParameter(GlobalParameterModel parameter)
    {
        if (ParameterTypeSupport.TryGetReadOnlySpanElementType(parameter.Type, out var elementType))
            return $"...{parameter.Name}: {MapType(elementType)}[]";
        var name = parameter.HasDefaultValue ? $"{parameter.Name}?" : parameter.Name;
        return $"{name}: {MapType(parameter.Type)}";
    }

    private static string FormatParameter(JsObjectParameterModel parameter)
    {
        if (ParameterTypeSupport.TryGetReadOnlySpanElementType(parameter.Type, out var elementType))
            return $"...{parameter.Name}: {MapType(elementType)}[]";
        return $"{parameter.Name}: {MapType(parameter.Type)}";
    }

    private static string MapType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullableType &&
            nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nullableType.TypeArguments.Length == 1)
            return MapType(nullableType.TypeArguments[0]);

        if (type.SpecialType == SpecialType.System_Void)
            return "void";
        if (type.SpecialType == SpecialType.System_Boolean)
            return "boolean";
        if (type.SpecialType == SpecialType.System_String)
            return "string";
        if (type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or
            SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
            or SpecialType.System_Decimal)
            return "number";
        if (type is IArrayTypeSymbol arrayType)
            return $"{MapType(arrayType.ElementType)}[]";
        if (ParameterTypeSupport.TryGetReadOnlySpanElementType(type, out var spanElementType))
            return $"{MapType(spanElementType)}[]";
        if (type is INamedTypeSymbol namedType &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
            (namedType.Name == "Task" || namedType.Name == "ValueTask"))
        {
            if (namedType.TypeArguments.Length == 1)
                return $"Promise<{MapType(namedType.TypeArguments[0])}>";
            return "Promise<void>";
        }

        if (type is INamedTypeSymbol declaredType && HasGenerateJsObjectAttribute(declaredType))
        {
            var ns = GetDocNamespace(declaredType);
            var name = GetDeclaredTypeName(declaredType) + BuildTypeParameters(declaredType);
            return ns is null ? name : ns + "." + name;
        }

        return "any";
    }

    private static void AppendDocComment(StringBuilder sb, string indent, XmlDocComment docs,
        IReadOnlyCollection<string> parameterNames)
    {
        if (docs.Summary.Length == 0 && docs.Remarks.Length == 0 && docs.Returns.Length == 0 &&
            docs.Parameters.Count == 0)
            return;

        sb.Append(indent).AppendLine("/**");
        if (docs.Summary.Length != 0)
            sb.Append(indent).Append(" * ").AppendLine(docs.Summary);
        if (docs.Remarks.Length != 0)
            sb.Append(indent).Append(" * ").AppendLine(docs.Remarks);
        foreach (var parameter in docs.Parameters)
        {
            if (!parameterNames.Contains(parameter.Name, StringComparer.Ordinal))
                continue;
            sb.Append(indent).Append(" * @param ").Append(parameter.Name).Append(' ').AppendLine(parameter.Text);
        }

        if (docs.Returns.Length != 0)
            sb.Append(indent).Append(" * @returns ").AppendLine(docs.Returns);
        sb.Append(indent).AppendLine(" */");
    }

    private static string? GetDocNamespace(INamedTypeSymbol symbol)
    {
        return DocAttributeReader.ReadDeclarationInfo(symbol).Namespace;
    }

    private static string GetDeclaredTypeName(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingType is null)
            return symbol.Name;
        return GetDeclaredTypeName(symbol.ContainingType) + "_" + symbol.Name;
    }

    private static string BuildTypeParameters(INamedTypeSymbol symbol)
    {
        return symbol.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", symbol.TypeParameters.Select(static x => x.Name)) + ">";
    }

    private static bool HasGenerateJsObjectAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == "Okojo.Annotations.GenerateJsObjectAttribute")
                return true;

        return false;
    }
}
