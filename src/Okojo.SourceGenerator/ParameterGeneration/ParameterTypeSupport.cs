using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal enum SpanElementKind : byte
{
    Other = 0,
    JsValue,
    Boolean,
    String,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal
}

internal static class ParameterTypeSupport
{
    public static bool HasSupportedReadOnlySpanShape(ImmutableArray<IParameterSymbol> parameters)
    {
        var sawSpan = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!TryGetReadOnlySpanElementType(parameters[i].Type, out _))
                continue;

            if (sawSpan || i != parameters.Length - 1)
                return false;

            sawSpan = true;
        }

        return true;
    }

    public static bool TryGetTrailingReadOnlySpanElementType(
        IReadOnlyList<IParameterSymbol> parameters,
        out int spanIndex,
        out ITypeSymbol elementType)
    {
        if (parameters.Count != 0 &&
            TryGetReadOnlySpanElementType(parameters[parameters.Count - 1].Type, out elementType))
        {
            spanIndex = parameters.Count - 1;
            return true;
        }

        spanIndex = -1;
        elementType = null!;
        return false;
    }

    public static bool TryGetReadOnlySpanElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.Name == "ReadOnlySpan" &&
            namedType.TypeArguments.Length == 1 &&
            namedType.ContainingNamespace.ToDisplayString() == "System")
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    public static int ComputeFunctionLength(ImmutableArray<IParameterSymbol> parameters, bool stopAtDefaultValue)
    {
        var length = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (TryGetReadOnlySpanElementType(parameter.Type, out _))
                break;
            if (stopAtDefaultValue && parameter.HasExplicitDefaultValue)
                break;
            length++;
        }

        return length;
    }

    public static SpanElementKind GetSpanElementKind(ITypeSymbol type)
    {
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Okojo.JsValue")
            return SpanElementKind.JsValue;

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => SpanElementKind.Boolean,
            SpecialType.System_String => SpanElementKind.String,
            SpecialType.System_Byte => SpanElementKind.Byte,
            SpecialType.System_SByte => SpanElementKind.SByte,
            SpecialType.System_Int16 => SpanElementKind.Int16,
            SpecialType.System_UInt16 => SpanElementKind.UInt16,
            SpecialType.System_Int32 => SpanElementKind.Int32,
            SpecialType.System_UInt32 => SpanElementKind.UInt32,
            SpecialType.System_Int64 => SpanElementKind.Int64,
            SpecialType.System_UInt64 => SpanElementKind.UInt64,
            SpecialType.System_Single => SpanElementKind.Single,
            SpecialType.System_Double => SpanElementKind.Double,
            SpecialType.System_Decimal => SpanElementKind.Decimal,
            _ => SpanElementKind.Other
        };
    }

    public static bool IsTaskLike(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
               namedType.Name is "Task" or "ValueTask";
    }
}
