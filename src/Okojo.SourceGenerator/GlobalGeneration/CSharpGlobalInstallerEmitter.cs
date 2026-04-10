using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.GlobalGeneration;

internal static class CSharpGlobalInstallerEmitter
{
    public static string GetHintName(INamedTypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('.', '_') + ".JsGlobals.g.cs";
    }

    public static string Emit(GlobalTypeModel model)
    {
        var symbol = model.Symbol;
        var functionGroups = OverloadDispatchAnalysis.AnalyzeByName(
            model.Functions,
            static x => x.Name,
            static x => x.Symbol,
            static x => x.Parameters,
            static x => x.Type,
            static x => x.HasDefaultValue);
        var sb = new StringBuilder();
        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString();
        var typeName = symbol.Name + BuildTypeParameters(symbol);

        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ").Append(typeName).AppendLine();
        sb.AppendLine("{");
        EmitInstaller(sb, model, functionGroups);
        sb.AppendLine();
        EmitPropertySource(sb, model);
        foreach (var functionGroup in functionGroups)
        {
            sb.AppendLine();
            EmitFunctionGroup(sb, model.Symbol, functionGroup);
        }

        foreach (var property in model.Properties)
        {
            sb.AppendLine();
            EmitPropertyGetterWrapper(sb, model.Symbol, property);
            if (property.Writable)
            {
                sb.AppendLine();
                EmitPropertySetterWrapper(sb, model.Symbol, property);
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitInstaller(StringBuilder sb, GlobalTypeModel model,
        IReadOnlyList<AnalyzedOverloadSet<GlobalFunctionModel, GlobalParameterModel>> functionGroups)
    {
        sb.Append("    public void ").Append(model.InstallerMethodName)
            .AppendLine("(global::Okojo.Runtime.JsGlobalInstaller globals)");
        sb.AppendLine("    {");
        foreach (var functionGroup in functionGroups)
        {
            var function = functionGroup.Overloads[0].Method;
            var functionLength = functionGroup.Overloads.Min(static x => x.Method.Length);
            sb.Append("        globals.Function(\"")
                .Append(EscapeString(functionGroup.Name))
                .Append("\", ")
                .Append(functionLength.ToString(CultureInfo.InvariantCulture))
                .Append(", __OkojoGeneratedGlobalFunction_")
                .Append(SanitizeIdentifier(functionGroup.Name))
                .Append(", isConstructor: ")
                .Append(function.IsConstructor ? "true" : "false")
                .AppendLine(");");
        }

        if (model.Properties.Count != 0)
            sb.Append("        globals.Properties(").Append(model.PropertySourceMethodName)
                .AppendLine("(globals.Realm));");
        sb.AppendLine("    }");
    }

    private static void EmitPropertySource(StringBuilder sb, GlobalTypeModel model)
    {
        sb.Append(
                "    public global::System.Collections.Generic.IEnumerable<global::Okojo.Runtime.PropertyDefinition> ")
            .Append(model.PropertySourceMethodName)
            .AppendLine("(global::Okojo.Runtime.JsRealm realm)");
        sb.AppendLine("    {");
        if (model.Properties.Count == 0)
        {
            sb.AppendLine("        yield break;");
            sb.AppendLine("    }");
            return;
        }

        foreach (var property in model.Properties)
        {
            var sanitizedName = SanitizeIdentifier(property.Name);
            sb.Append("        var __jsAtom_")
                .Append(sanitizedName)
                .Append(" = realm.Atoms.InternNoCheck(\"")
                .Append(EscapeString(property.Name))
                .AppendLine("\");");
            sb.Append("        var __jsGetter_")
                .Append(sanitizedName)
                .Append(" = new global::Okojo.Objects.JsHostFunction(realm, __OkojoGeneratedGlobalGetter_")
                .Append(sanitizedName)
                .Append(", \"get ")
                .Append(EscapeString(property.Name))
                .AppendLine("\", 0);");
            if (property.Writable)
            {
                sb.Append("        var __jsSetter_")
                    .Append(sanitizedName)
                    .Append(" = new global::Okojo.Objects.JsHostFunction(realm, __OkojoGeneratedGlobalSetter_")
                    .Append(sanitizedName)
                    .Append(", \"set ")
                    .Append(EscapeString(property.Name))
                    .AppendLine("\", 1);");
                sb.Append("        yield return global::Okojo.Runtime.PropertyDefinition.GetterSetterData(__jsAtom_")
                    .Append(sanitizedName)
                    .Append(", __jsGetter_")
                    .Append(sanitizedName)
                    .Append(", __jsSetter_")
                    .Append(sanitizedName)
                    .Append(", enumerable: ")
                    .Append(property.Enumerable ? "true" : "false")
                    .Append(", configurable: ")
                    .Append(property.Configurable ? "true" : "false")
                    .AppendLine(");");
            }
            else
            {
                sb.Append("        yield return global::Okojo.Runtime.PropertyDefinition.GetterData(__jsAtom_")
                    .Append(sanitizedName)
                    .Append(", __jsGetter_")
                    .Append(sanitizedName)
                    .Append(", enumerable: ")
                    .Append(property.Enumerable ? "true" : "false")
                    .Append(", configurable: ")
                    .Append(property.Configurable ? "true" : "false")
                    .AppendLine(");");
            }
        }

        sb.AppendLine("    }");
    }

    private static void EmitFunctionGroup(StringBuilder sb, INamedTypeSymbol containingType,
        AnalyzedOverloadSet<GlobalFunctionModel, GlobalParameterModel> functionGroup)
    {
        var dispatcherName = "__OkojoGeneratedGlobalFunction_" + SanitizeIdentifier(functionGroup.Name);
        MethodOverloadDispatchEmitter.EmitDispatcher(
            sb,
            dispatcherName,
            "Host function argument type mismatch.",
            functionGroup,
            false,
            static x => x.Symbol,
            static x => x.Parameters,
            static x => x.Type,
            static x => x.HasDefaultValue,
            overloadIndex => dispatcherName + "__Overload" + overloadIndex.ToString(CultureInfo.InvariantCulture));

        for (var i = 0; i < functionGroup.Overloads.Count; i++)
        {
            sb.AppendLine();
            EmitFunctionOverloadWrapper(sb, containingType, functionGroup.Overloads[i].Method,
                dispatcherName + "__Overload" + i.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void EmitFunctionOverloadWrapper(StringBuilder sb, INamedTypeSymbol containingType,
        GlobalFunctionModel function, string methodName)
    {
        var parameters = function.Symbol.Parameters;
        var hasTrailingSpan =
            ParameterTypeSupport.TryGetTrailingReadOnlySpanElementType(parameters, out var spanIndex,
                out var spanElementType);
        sb.Append("    private global::Okojo.JsValue ")
            .Append(methodName)
            .AppendLine("(scoped in global::Okojo.Runtime.CallInfo info)");
        sb.AppendLine("    {");
        if (hasTrailingSpan)
            EmitTrailingSpanSetup(sb, spanElementType, spanIndex, "        ");
        EmitMethodInvocation(sb, containingType, function.Symbol, function.Parameters,
            hasTrailingSpan ? "__jsSpanArg" : null);
        sb.AppendLine("    }");
    }

    private static void EmitPropertyGetterWrapper(StringBuilder sb, INamedTypeSymbol containingType,
        GlobalPropertyModel property)
    {
        sb.Append("    private global::Okojo.JsValue __OkojoGeneratedGlobalGetter_")
            .Append(SanitizeIdentifier(property.Name))
            .AppendLine("(scoped in global::Okojo.Runtime.CallInfo info)");
        sb.AppendLine("    {");
        sb.Append("        return info.Realm.WrapHostValue(");
        AppendMemberAccess(sb, containingType, property.Symbol);
        sb.AppendLine(");");
        sb.AppendLine("    }");
    }

    private static void EmitPropertySetterWrapper(StringBuilder sb, INamedTypeSymbol containingType,
        GlobalPropertyModel property)
    {
        sb.Append("    private global::Okojo.JsValue __OkojoGeneratedGlobalSetter_")
            .Append(SanitizeIdentifier(property.Name))
            .AppendLine("(scoped in global::Okojo.Runtime.CallInfo info)");
        sb.AppendLine("    {");
        sb.Append("        ");
        AppendMemberAccess(sb, containingType, property.Symbol);
        sb.Append(" = ");
        AppendArgumentRead(sb, property.Type, 0, false, null);
        sb.AppendLine(";");
        sb.AppendLine("        return global::Okojo.JsValue.Undefined;");
        sb.AppendLine("    }");
    }

    private static void EmitMethodInvocation(
        StringBuilder sb,
        INamedTypeSymbol containingType,
        IMethodSymbol method,
        IReadOnlyList<GlobalParameterModel> parameters,
        string? spanArgumentName)
    {
        ITypeSymbol? spanElementType = null;
        var needsTryFinally = spanArgumentName is not null &&
                              ParameterTypeSupport.TryGetReadOnlySpanElementType(
                                  method.Parameters[method.Parameters.Length - 1].Type, out spanElementType) &&
                              ParameterTypeSupport.GetSpanElementKind(spanElementType) != SpanElementKind.JsValue;
        var indent = "        ";
        if (needsTryFinally)
        {
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            indent = "            ";
        }

        if (!method.ReturnsVoid)
            sb.Append(indent).Append("var __jsResult = ");
        else
            sb.Append(indent);

        AppendMethodTarget(sb, containingType, method.IsStatic);
        sb.Append('.').Append(method.Name).Append('(');
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i != 0)
                sb.Append(", ");
            var parameter = parameters[i];
            if (spanArgumentName is not null && i == parameters.Count - 1)
                sb.Append(spanArgumentName);
            else
                AppendArgumentRead(sb, parameter.Type, i, parameter.HasDefaultValue, parameter.DefaultValue);
        }

        sb.AppendLine(");");
        if (method.ReturnsVoid)
            sb.Append(indent).AppendLine("return global::Okojo.JsValue.Undefined;");
        else
            sb.Append(indent).AppendLine("return info.Realm.WrapHostValue(__jsResult);");

        if (needsTryFinally)
        {
            sb.AppendLine("        }");
            sb.AppendLine("        finally");
            sb.AppendLine("        {");
            EmitTrailingSpanCleanup(sb, spanElementType!, "            ");
            sb.AppendLine("        }");
        }
    }

    private static void AppendMethodTarget(StringBuilder sb, INamedTypeSymbol containingType, bool isStatic)
    {
        if (isStatic)
        {
            sb.Append(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            return;
        }

        sb.Append("this");
    }

    private static void AppendMemberAccess(StringBuilder sb, INamedTypeSymbol containingType, ISymbol member)
    {
        if (member.IsStatic)
            sb.Append(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        else
            sb.Append("this");
        sb.Append('.').Append(member.Name);
    }

    private static void AppendArgumentRead(
        StringBuilder sb,
        ITypeSymbol type,
        int index,
        bool hasDefaultValue,
        object? defaultValue)
    {
        var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!hasDefaultValue)
        {
            sb.Append("info.GetArgument<").Append(fullTypeName).Append(">(")
                .Append(index.ToString(CultureInfo.InvariantCulture)).Append(')');
            return;
        }

        sb.Append("info.GetArgumentOrDefault<").Append(fullTypeName).Append(">(")
            .Append(index.ToString(CultureInfo.InvariantCulture)).Append(", ");
        AppendDefaultValueLiteral(sb, type, defaultValue);
        sb.Append(')');
    }

    private static void AppendDefaultValueLiteral(StringBuilder sb, ITypeSymbol type, object? value)
    {
        if (value is null)
        {
            sb.Append("default!");
            return;
        }

        switch (value)
        {
            case string str:
                sb.Append("@\"").Append(str.Replace("\"", "\"\"")).Append('"');
                return;
            case bool boolean:
                sb.Append(boolean ? "true" : "false");
                return;
            case char ch:
                sb.Append('\'').Append(ch == '\'' ? "\\'" : ch.ToString()).Append('\'');
                return;
            case float single:
                sb.Append(single.ToString("R", CultureInfo.InvariantCulture)).Append('F');
                return;
            case double dbl:
                sb.Append(dbl.ToString("R", CultureInfo.InvariantCulture)).Append('D');
                return;
            case decimal dec:
                sb.Append(dec.ToString(CultureInfo.InvariantCulture)).Append('M');
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                if (type.SpecialType == SpecialType.System_UInt32)
                    sb.Append('U');
                else if (type.SpecialType == SpecialType.System_Int64)
                    sb.Append('L');
                else if (type.SpecialType == SpecialType.System_UInt64)
                    sb.Append("UL");
                return;
        }

        sb.Append("default!");
    }

    private static void EmitTrailingSpanSetup(StringBuilder sb, ITypeSymbol elementType, int startIndex, string indent)
    {
        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var kind = ParameterTypeSupport.GetSpanElementKind(elementType);
        sb.Append(indent)
            .Append("int __jsSpanCount = info.ArgumentCount > ")
            .Append(startIndex)
            .Append(" ? info.ArgumentCount - ")
            .Append(startIndex)
            .AppendLine(" : 0;");

        if (kind == SpanElementKind.JsValue)
        {
            sb.Append(indent)
                .Append("global::System.ReadOnlySpan<global::Okojo.JsValue> __jsSpanArg = info.Arguments.Slice(")
                .Append(startIndex)
                .AppendLine(", __jsSpanCount);");
            return;
        }

        var usesStackalloc = kind != SpanElementKind.Other && kind != SpanElementKind.String;
        sb.Append(indent)
            .Append(elementTypeName)
            .AppendLine("[]? __jsSpanPooled = null;");
        if (usesStackalloc)
        {
            sb.Append(indent)
                .Append("global::System.Span<")
                .Append(elementTypeName)
                .Append("> __jsSpanArg = __jsSpanCount <= 16 ? stackalloc ")
                .Append(elementTypeName)
                .AppendLine("[__jsSpanCount] : (__jsSpanPooled = global::System.Buffers.ArrayPool<" + elementTypeName +
                            ">.Shared.Rent(__jsSpanCount));");
            sb.Append(indent)
                .Append("global::Okojo.Runtime.Interop.CallInfoSpanConverter.FillArgumentSpan(info, ")
                .Append(startIndex)
                .AppendLine(", __jsSpanArg);");
        }
        else
        {
            sb.Append(indent)
                .Append("global::System.ReadOnlySpan<")
                .Append(elementTypeName)
                .Append("> __jsSpanArg = global::System.ReadOnlySpan<")
                .Append(elementTypeName)
                .AppendLine(">.Empty;");
            sb.Append(indent).AppendLine("if (__jsSpanCount != 0)");
            sb.Append(indent).AppendLine("        {");
            sb.Append(indent)
                .Append("            __jsSpanPooled = global::System.Buffers.ArrayPool<")
                .Append(elementTypeName)
                .Append(">.Shared.Rent(__jsSpanCount);")
                .AppendLine();
            sb.Append(indent)
                .Append("            global::System.Span<")
                .Append(elementTypeName)
                .Append("> __jsSpanBuffer = __jsSpanPooled.AsSpan(0, __jsSpanCount);")
                .AppendLine();
            sb.Append(indent)
                .Append("            global::Okojo.Runtime.Interop.CallInfoSpanConverter.FillArgumentSpan(info, ")
                .Append(startIndex)
                .AppendLine(", __jsSpanBuffer);");
            sb.Append(indent)
                .AppendLine("            __jsSpanArg = __jsSpanBuffer;");
            sb.Append(indent).AppendLine("        }");
        }
    }

    private static void EmitTrailingSpanCleanup(StringBuilder sb, ITypeSymbol elementType, string indent)
    {
        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var clearArray =
            ParameterTypeSupport.GetSpanElementKind(elementType) is SpanElementKind.Other or SpanElementKind.String;
        sb.Append(indent).AppendLine("if (__jsSpanPooled is not null)");
        sb.Append(indent)
            .Append("    global::System.Buffers.ArrayPool<")
            .Append(elementTypeName)
            .Append(">.Shared.Return(__jsSpanPooled");
        if (clearArray)
            sb.Append(", clearArray: true");
        sb.AppendLine(");");
    }

    private static string BuildTypeParameters(INamedTypeSymbol symbol)
    {
        return symbol.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", symbol.TypeParameters.Select(static x => x.Name)) + ">";
    }

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
