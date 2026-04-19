using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Okojo.SourceGenerator.ObjectGeneration;

namespace Okojo.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class GenerateJsObjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataNames.GenerateJsObjectAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        context.RegisterSourceOutput(provider, static (spc, symbol) =>
        {
            var model = JsObjectExportCollector.Collect(symbol);
            if (model is null)
                return;
            var instanceOverloads = GroupMethodMembers(model.InstanceMembers);
            var staticOverloads = GroupMethodMembers(model.StaticMembers);
            var hasErrors = ReportDiagnostics(spc, instanceOverloads) | ReportDiagnostics(spc, staticOverloads);
            if (hasErrors)
                return;
            spc.AddSource(GetHintName(symbol), Emit(model));
        });
    }

    private static string GetHintName(INamedTypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('.', '_') + ".JsHost.g.cs";
    }

    private static string Emit(JsObjectTypeModel model)
    {
        var sb = new StringBuilder();
        var symbol = model.Symbol;
        var instanceMethodGroups = GroupMethodMembers(model.InstanceMembers);
        var staticMethodGroups = GroupMethodMembers(model.StaticMembers);
        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString();
        var typeName = symbol.Name + BuildTypeParameters(symbol);
        var fullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ").Append(typeName).Append(" : global::Okojo.Runtime.Interop.IHostBindable")
            .AppendLine();
        sb.AppendLine("{");
        sb.AppendLine(
            "    private static readonly global::Okojo.Runtime.Interop.HostBinding s_HostBinding = CreateHostBinding();");
        sb.AppendLine();
        sb.AppendLine(
            "    global::Okojo.Runtime.Interop.HostBinding global::Okojo.Runtime.Interop.IHostBindable.GetHostBinding()");
        sb.AppendLine("        => s_HostBinding;");
        sb.AppendLine();
        sb.Append(
                "    public static global::Okojo.Objects.JsHostObject ToJsObject(global::Okojo.Runtime.JsRealm realm, ")
            .Append(fullTypeName).AppendLine(" value)");
        sb.AppendLine("        => realm.WrapHostObject(value);");
        sb.AppendLine();
        sb.AppendLine(
            "    public static global::Okojo.Objects.JsHostFunction ToJsType(global::Okojo.Runtime.JsRealm realm)");
        sb.AppendLine("        => realm.WrapHostType(typeof(" + fullTypeName + "), s_HostBinding);");
        sb.AppendLine();
        sb.AppendLine("    private static global::Okojo.Runtime.Interop.HostBinding CreateHostBinding()");
        sb.AppendLine("    {");
        sb.Append("        return new global::Okojo.Runtime.Interop.HostBinding(typeof(").Append(fullTypeName)
            .AppendLine("),");
        sb.AppendLine("            instanceMembers: new global::Okojo.Runtime.Interop.HostMemberBinding[]");
        sb.AppendLine("            {");
        EmitMembers(sb, symbol, model.InstanceMembers, instanceMethodGroups, false);
        sb.AppendLine("            },");
        sb.AppendLine("            staticMembers: new global::Okojo.Runtime.Interop.HostMemberBinding[]");
        sb.AppendLine("            {");
        EmitMembers(sb, symbol, model.StaticMembers, staticMethodGroups, true);
        sb.AppendLine("            });");
        sb.AppendLine("    }");
        EmitMethodGroups(sb, symbol, instanceMethodGroups, false);
        EmitMethodGroups(sb, symbol, staticMethodGroups, true);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitMembers(
        StringBuilder sb,
        INamedTypeSymbol symbol,
        IReadOnlyList<JsObjectMemberModel> members,
        IReadOnlyList<AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel>> methodGroups,
        bool isStaticGroup)
    {
        foreach (var member in members)
            switch (member.Symbol)
            {
                case IFieldSymbol field when !field.IsConst:
                    EmitField(sb, symbol, field);
                    break;
                case IPropertySymbol property when property.Parameters.Length == 0:
                    EmitProperty(sb, symbol, property);
                    break;
            }

        foreach (var methodGroup in methodGroups)
            EmitMethodBinding(sb, methodGroup, isStaticGroup);
    }

    private static void EmitField(StringBuilder sb, INamedTypeSymbol containingType, IFieldSymbol field)
    {
        var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        sb.Append("                new global::Okojo.Runtime.Interop.HostMemberBinding(\"")
            .Append(field.Name)
            .Append("\", global::Okojo.Runtime.Interop.HostMemberBindingKind.Field, ")
            .Append(field.IsStatic ? "true" : "false");
        sb.Append(", getterBody: static (in global::Okojo.Runtime.CallInfo info) => ");
        EmitToJsValue(sb, field.Type, () =>
        {
            AppendCallTarget(sb, fullTypeName, field.IsStatic);
            sb.Append('.').Append(field.Name);
        });

        if (!field.IsReadOnly)
        {
            sb.Append(", setterBody: static (in global::Okojo.Runtime.CallInfo info) => { ");
            AppendCallTarget(sb, fullTypeName, field.IsStatic);
            sb.Append('.').Append(field.Name).Append(" = ");
            EmitGetArgument(sb, field.Type, 0);
            sb.Append("; return global::Okojo.JsValue.Undefined; }");
        }

        sb.AppendLine("),");
    }

    private static void EmitProperty(StringBuilder sb, INamedTypeSymbol containingType, IPropertySymbol property)
    {
        var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        sb.Append("                new global::Okojo.Runtime.Interop.HostMemberBinding(\"")
            .Append(property.Name)
            .Append("\", global::Okojo.Runtime.Interop.HostMemberBindingKind.Property, ")
            .Append(property.IsStatic ? "true" : "false");
        if (property.GetMethod is not null)
        {
            sb.Append(", getterBody: static (in global::Okojo.Runtime.CallInfo info) => ");
            EmitToJsValue(sb, property.Type, () =>
            {
                AppendCallTarget(sb, fullTypeName, property.IsStatic);
                sb.Append('.').Append(property.Name);
            });
        }

        if (property.SetMethod is not null)
        {
            sb.Append(", setterBody: static (in global::Okojo.Runtime.CallInfo info) => { ");
            AppendCallTarget(sb, fullTypeName, property.IsStatic);
            sb.Append('.').Append(property.Name).Append(" = ");
            EmitGetArgument(sb, property.Type, 0);
            sb.Append("; return global::Okojo.JsValue.Undefined; }");
        }

        sb.AppendLine("),");
    }

    private static void EmitMethodBinding(StringBuilder sb,
        AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel> methodGroup, bool isStaticGroup)
    {
        var methodName = GetGeneratedMethodGroupName(methodGroup.Name, isStaticGroup);
        sb.Append("                new global::Okojo.Runtime.Interop.HostMemberBinding(\"")
            .Append(methodGroup.Name)
            .Append("\", global::Okojo.Runtime.Interop.HostMemberBindingKind.Method, ")
            .Append(isStaticGroup ? "true" : "false")
            .Append(", methodBody: static (in global::Okojo.Runtime.CallInfo info) => ")
            .Append(methodName)
            .Append("(info), functionLength: ")
            .Append(methodGroup.Overloads.Min(static x =>
                ParameterTypeSupport.ComputeFunctionLength(x.Symbol.Parameters, false)))
            .Append("),");
        sb.AppendLine();
    }

    private static void EmitMethodGroups(
        StringBuilder sb,
        INamedTypeSymbol containingType,
        IReadOnlyList<AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel>> methodGroups,
        bool isStaticGroup)
    {
        foreach (var methodGroup in methodGroups)
        {
            sb.AppendLine();
            EmitMethodGroup(sb, containingType, methodGroup, isStaticGroup);
        }
    }

    private static void EmitMethodGroup(
        StringBuilder sb,
        INamedTypeSymbol containingType,
        AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel> methodGroup,
        bool isStaticGroup)
    {
        var dispatcherName = GetGeneratedMethodGroupName(methodGroup.Name, isStaticGroup);
        MethodOverloadDispatchEmitter.EmitDispatcher(
            sb,
            dispatcherName,
            "Host function argument type mismatch.",
            methodGroup,
            true,
            static x => (IMethodSymbol)x.Symbol,
            static x => x.Parameters,
            static x => x.Type,
            static _ => false,
            overloadIndex => dispatcherName + "__Overload" + overloadIndex);

        var fullTypeName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        for (var i = 0; i < methodGroup.Overloads.Count; i++)
        {
            sb.AppendLine();
            EmitMethodOverloadWrapper(
                sb,
                fullTypeName,
                methodGroup.Overloads[i].Symbol,
                dispatcherName + "__Overload" + i);
        }
    }

    private static void EmitMethodOverloadWrapper(StringBuilder sb, string fullTypeName, IMethodSymbol method,
        string methodName)
    {
        var hasTrailingSpan =
            ParameterTypeSupport.TryGetTrailingReadOnlySpanElementType(method.Parameters, out var spanIndex,
                out var spanElementType);
        sb.Append("    private static global::Okojo.JsValue ")
            .Append(methodName)
            .AppendLine("(scoped in global::Okojo.Runtime.CallInfo info)");
        sb.AppendLine("    {");
        if (hasTrailingSpan)
            EmitTrailingSpanSetup(sb, spanElementType, spanIndex, "        ");
        EmitMethodInvocationBody(
            sb,
            method,
            fullTypeName,
            "        ",
            hasTrailingSpan ? "__jsSpanArg" : null);
        sb.AppendLine("    }");
    }

    private static void EmitMethodInvocationBody(
        StringBuilder sb,
        IMethodSymbol method,
        string fullTypeName,
        string indent,
        string? spanArgumentName)
    {
        ITypeSymbol? spanElementType = null;
        var needsTryFinally = spanArgumentName is not null &&
                              ParameterTypeSupport.TryGetReadOnlySpanElementType(
                                  method.Parameters[method.Parameters.Length - 1].Type, out spanElementType) &&
                              ParameterTypeSupport.GetSpanElementKind(spanElementType) != SpanElementKind.JsValue;
        if (needsTryFinally)
        {
            sb.Append(indent).AppendLine("try");
            sb.Append(indent).AppendLine("{");
            indent += "    ";
        }

        if (!method.ReturnsVoid)
            sb.Append(indent).Append("var __jsResult = ");
        else
            sb.Append(indent);
        AppendCallTarget(sb, fullTypeName, method.IsStatic);
        sb.Append('.').Append(method.Name).Append('(');
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");
            if (spanArgumentName is not null && i == method.Parameters.Length - 1)
                sb.Append(spanArgumentName);
            else
                EmitGetArgument(sb, method.Parameters[i].Type, i);
        }

        sb.AppendLine(");");

        if (method.ReturnsVoid)
        {
            sb.Append(indent).AppendLine("return global::Okojo.JsValue.Undefined;");
        }
        else
        {
            sb.Append(indent).Append("return ");
            EmitToJsValue(sb, method.ReturnType, () => sb.Append("__jsResult"));
            sb.AppendLine(";");
        }

        if (needsTryFinally)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.Append(indent).AppendLine("}");
            sb.Append(indent).AppendLine("finally");
            sb.Append(indent).AppendLine("{");
            EmitTrailingSpanCleanup(sb, spanElementType!, indent + "    ");
            sb.Append(indent).AppendLine("}");
        }
    }

    private static void AppendCallTarget(StringBuilder sb, string fullTypeName, bool isStatic)
    {
        if (isStatic)
        {
            sb.Append(fullTypeName);
            return;
        }

        sb.Append("info.GetThis<").Append(fullTypeName).Append(">()");
    }

    private static void EmitGetArgument(StringBuilder sb, ITypeSymbol type, int index)
    {
        if (TryEmitTaskArgument(sb, type, index))
            return;

        if (type.SpecialType == SpecialType.System_Single)
        {
            sb.Append("info.GetArgumentSingle(").Append(index).Append(')');
            return;
        }

        if (type.SpecialType == SpecialType.System_Double)
        {
            sb.Append("info.GetArgumentDouble(").Append(index).Append(')');
            return;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            sb.Append("info.GetArgumentString(").Append(index).Append(')');
            return;
        }

        sb.Append("info.GetArgument<").Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Append(">(")
            .Append(index).Append(')');
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
            sb.Append(indent).AppendLine("{");
            sb.Append(indent)
                .Append("    __jsSpanPooled = global::System.Buffers.ArrayPool<")
                .Append(elementTypeName)
                .Append(">.Shared.Rent(__jsSpanCount);")
                .AppendLine();
            sb.Append(indent)
                .Append("    global::System.Span<")
                .Append(elementTypeName)
                .Append("> __jsSpanBuffer = __jsSpanPooled.AsSpan(0, __jsSpanCount);")
                .AppendLine();
            sb.Append(indent)
                .Append("    global::Okojo.Runtime.Interop.CallInfoSpanConverter.FillArgumentSpan(info, ")
                .Append(startIndex)
                .AppendLine(", __jsSpanBuffer);");
            sb.Append(indent)
                .Append("    __jsSpanArg = __jsSpanBuffer;")
                .AppendLine();
            sb.Append(indent).AppendLine("}");
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

    private static void EmitToJsValue(StringBuilder sb, ITypeSymbol type, Action emitValue)
    {
        if (type.SpecialType == SpecialType.System_Void)
            return;

        if (TryEmitTaskReturn(sb, type, emitValue))
            return;

        if (type.SpecialType == SpecialType.System_String)
        {
            sb.Append("global::Okojo.JsValue.FromString(");
            emitValue();
            sb.Append(')');
            return;
        }

        if (type.SpecialType == SpecialType.System_Boolean)
        {
            sb.Append("((");
            emitValue();
            sb.Append(") ? global::Okojo.JsValue.True : global::Okojo.JsValue.False)");
            return;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Int32:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                sb.Append("global::Okojo.JsValue.FromInt32((int)(");
                emitValue();
                sb.Append("))");
                return;
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                sb.Append("new global::Okojo.JsValue(");
                emitValue();
                sb.Append(')');
                return;
        }

        sb.Append("info.Realm.WrapHostValue(");
        emitValue();
        sb.Append(')');
    }

    private static bool TryEmitTaskArgument(StringBuilder sb, ITypeSymbol type, int index)
    {
        if (type is not INamedTypeSymbol namedType ||
            namedType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks")
            return false;

        if (namedType.Name == "Task")
        {
            if (namedType.TypeArguments.Length == 0)
            {
                sb.Append("info.Realm.ToTask(info.GetArgument(").Append(index).Append("))");
                return true;
            }

            sb.Append("info.Realm.ToTask<")
                .Append(namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">(info.GetArgument(").Append(index).Append("))");
            return true;
        }

        if (namedType.Name == "ValueTask")
        {
            if (namedType.TypeArguments.Length == 0)
            {
                sb.Append("info.Realm.ToValueTask(info.GetArgument(").Append(index).Append("))");
                return true;
            }

            sb.Append("info.Realm.ToValueTask<")
                .Append(namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">(info.GetArgument(").Append(index).Append("))");
            return true;
        }

        return false;
    }

    private static bool TryEmitTaskReturn(StringBuilder sb, ITypeSymbol type, Action emitValue)
    {
        if (type is not INamedTypeSymbol namedType ||
            namedType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks")
            return false;

        if (namedType.Name == "Task")
        {
            if (namedType.TypeArguments.Length == 1)
            {
                sb.Append("info.Realm.WrapTask<")
                    .Append(namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(");
                emitValue();
                sb.Append(')');
                return true;
            }

            sb.Append("info.Realm.WrapTask(");
            emitValue();
            sb.Append(')');
            return true;
        }

        if (namedType.Name == "ValueTask")
        {
            if (namedType.TypeArguments.Length == 1)
            {
                sb.Append("info.Realm.WrapTask<")
                    .Append(namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(");
                emitValue();
                sb.Append(')');
                return true;
            }

            sb.Append("info.Realm.WrapTask(");
            emitValue();
            sb.Append(')');
            return true;
        }

        return false;
    }

    private static string BuildTypeParameters(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0)
            return string.Empty;
        return "<" + string.Join(", ", symbol.TypeParameters.Select(static x => x.Name)) + ">";
    }

    private static IReadOnlyList<AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel>> GroupMethodMembers(
        IReadOnlyList<JsObjectMemberModel> members)
    {
        var methods = new List<JsObjectMemberModel>();
        for (var i = 0; i < members.Count; i++)
            if (members[i].Kind == JsObjectMemberKind.Method)
                methods.Add(members[i]);

        return OverloadDispatchAnalysis.AnalyzeByName(
            methods,
            static x => x.Name,
            static x => (IMethodSymbol)x.Symbol,
            static x => x.Parameters,
            static x => x.Type,
            static _ => false);
    }

    private static string GetGeneratedMethodGroupName(string methodName, bool isStaticGroup)
    {
        var sb = new StringBuilder("__OkojoGenerated");
        sb.Append(isStaticGroup ? "StaticMethod_" : "InstanceMethod_");
        foreach (var ch in methodName)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.Length == 0 ? "__OkojoGeneratedMethod_" : sb.ToString();
    }

    private static bool ReportDiagnostics(SourceProductionContext spc,
        IReadOnlyList<AnalyzedOverloadSet<JsObjectMemberModel, JsObjectParameterModel>> overloadSets)
    {
        var hasErrors = false;
        for (var i = 0; i < overloadSets.Count; i++)
        for (var j = 0; j < overloadSets[i].Diagnostics.Count; j++)
        {
            hasErrors = true;
            var diagnostic = overloadSets[i].Diagnostics[j];
            spc.ReportDiagnostic(Diagnostic.Create(
                SourceGeneratorDiagnostics.AmbiguousGeneratedOverload,
                diagnostic.Location,
                diagnostic.Message));
        }

        return hasErrors;
    }
}
