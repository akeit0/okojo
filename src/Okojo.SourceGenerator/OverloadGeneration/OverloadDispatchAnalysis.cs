using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal enum OverloadParameterMatchKind : byte
{
    Other = 0,
    JsValue,
    String,
    Boolean,
    Numeric,
    TaskLike,
    JsObject,
    Object,
    Reference,
    SpanOther,
    SpanJsValue,
    SpanString,
    SpanBoolean,
    SpanNumeric,
    SpanJsObject,
    SpanObject,
    SpanReference
}

internal sealed class OverloadParameterSpec(
    ITypeSymbol type,
    bool hasDefaultValue,
    bool isTrailingSpan,
    OverloadParameterMatchKind matchKind)
{
    public ITypeSymbol Type { get; } = type;
    public bool HasDefaultValue { get; } = hasDefaultValue;
    public bool IsTrailingSpan { get; } = isTrailingSpan;
    public OverloadParameterMatchKind MatchKind { get; } = matchKind;
}

internal sealed class AnalyzedOverload<TMethod, TParameter>(
    int index,
    TMethod method,
    IMethodSymbol symbol,
    IReadOnlyList<TParameter> parameters,
    IReadOnlyList<OverloadParameterSpec> parameterSpecs,
    int requiredCount,
    int maxCount)
{
    public int Index { get; } = index;
    public TMethod Method { get; } = method;
    public IMethodSymbol Symbol { get; } = symbol;
    public IReadOnlyList<TParameter> Parameters { get; } = parameters;
    public IReadOnlyList<OverloadParameterSpec> ParameterSpecs { get; } = parameterSpecs;
    public int RequiredCount { get; } = requiredCount;
    public int MaxCount { get; } = maxCount;
    public bool HasOpenEndedCount => MaxCount == int.MaxValue;
    public int FixedCount => HasOpenEndedCount ? ParameterSpecs.Count - 1 : ParameterSpecs.Count;

    public bool AcceptsCount(int count)
    {
        return count >= RequiredCount && (HasOpenEndedCount || count <= MaxCount);
    }

    public OverloadParameterSpec GetParameterAtArgumentIndex(int argumentIndex)
    {
        if (argumentIndex < FixedCount)
            return ParameterSpecs[argumentIndex];
        return ParameterSpecs[ParameterSpecs.Count - 1];
    }
}

internal sealed class OverloadCountBucket<TMethod, TParameter>(
    int exactCount,
    IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> candidates)
{
    public int ExactCount { get; } = exactCount;
    public IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> Candidates { get; } = candidates;
}

internal sealed class OverloadDiagnosticInfo(string message, Location? location)
{
    public string Message { get; } = message;
    public Location? Location { get; } = location;
}

internal sealed class AnalyzedOverloadSet<TMethod, TParameter>(
    string name,
    IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> overloads,
    IReadOnlyList<OverloadCountBucket<TMethod, TParameter>> exactCountBuckets,
    IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> openEndedCandidates,
    IReadOnlyList<OverloadDiagnosticInfo> diagnostics)
{
    public string Name { get; } = name;
    public IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> Overloads { get; } = overloads;
    public IReadOnlyList<OverloadCountBucket<TMethod, TParameter>> ExactCountBuckets { get; } = exactCountBuckets;
    public IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> OpenEndedCandidates { get; } = openEndedCandidates;
    public IReadOnlyList<OverloadDiagnosticInfo> Diagnostics { get; } = diagnostics;
}

internal static class OverloadDispatchAnalysis
{
    public static IReadOnlyList<AnalyzedOverloadSet<TMethod, TParameter>> AnalyzeByName<TMethod, TParameter>(
        IReadOnlyList<TMethod> methods,
        Func<TMethod, string> getName,
        Func<TMethod, IMethodSymbol> getMethod,
        Func<TMethod, IReadOnlyList<TParameter>> getParameters,
        Func<TParameter, ITypeSymbol> getParameterType,
        Func<TParameter, bool> hasDefaultValue)
    {
        var names = new List<string>();
        var groups = new List<List<TMethod>>();
        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            var name = getName(method);
            var index = names.FindIndex(existing => string.Equals(existing, name, StringComparison.Ordinal));
            if (index < 0)
            {
                names.Add(name);
                groups.Add(new() { method });
            }
            else
            {
                groups[index].Add(method);
            }
        }

        var result = new List<AnalyzedOverloadSet<TMethod, TParameter>>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
            result.Add(AnalyzeSet(names[i], groups[i], getMethod, getParameters, getParameterType, hasDefaultValue));
        return result;
    }

    private static AnalyzedOverloadSet<TMethod, TParameter> AnalyzeSet<TMethod, TParameter>(
        string name,
        IReadOnlyList<TMethod> methods,
        Func<TMethod, IMethodSymbol> getMethod,
        Func<TMethod, IReadOnlyList<TParameter>> getParameters,
        Func<TParameter, ITypeSymbol> getParameterType,
        Func<TParameter, bool> hasDefaultValue)
    {
        var overloads = new List<AnalyzedOverload<TMethod, TParameter>>(methods.Count);
        var openEnded = new List<AnalyzedOverload<TMethod, TParameter>>();
        var diagnostics = new List<OverloadDiagnosticInfo>();
        var maxFiniteCount = -1;

        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            var symbol = getMethod(method);
            var parameters = getParameters(method);
            var specs = new List<OverloadParameterSpec>(parameters.Count);
            var hasTrailingSpan = false;
            for (var p = 0; p < parameters.Count; p++)
            {
                var parameterType = getParameterType(parameters[p]);
                ITypeSymbol? spanElementType = null;
                var isTrailingSpan = p == parameters.Count - 1 &&
                                     ParameterTypeSupport.TryGetReadOnlySpanElementType(parameterType,
                                         out spanElementType);
                if (isTrailingSpan)
                    parameterType = spanElementType!;
                hasTrailingSpan |= isTrailingSpan;
                specs.Add(new(
                    parameterType,
                    hasDefaultValue(parameters[p]),
                    isTrailingSpan,
                    GetMatchKind(parameterType, isTrailingSpan)));
            }

            var requiredCount = ParameterTypeSupport.ComputeFunctionLength(symbol.Parameters, true);
            var maxCount = hasTrailingSpan ? int.MaxValue : parameters.Count;
            var overload =
                new AnalyzedOverload<TMethod, TParameter>(i, method, symbol, parameters, specs, requiredCount, maxCount);
            overloads.Add(overload);
            if (overload.HasOpenEndedCount)
                openEnded.Add(overload);
            else if (overload.MaxCount > maxFiniteCount)
                maxFiniteCount = overload.MaxCount;
        }

        var exactBuckets = new List<OverloadCountBucket<TMethod, TParameter>>();
        for (var count = 0; count <= maxFiniteCount; count++)
        {
            var candidates = new List<AnalyzedOverload<TMethod, TParameter>>();
            for (var i = 0; i < overloads.Count; i++)
                if (overloads[i].AcceptsCount(count))
                    candidates.Add(overloads[i]);

            if (candidates.Count != 0)
                exactBuckets.Add(new(count, candidates));
        }

        for (var i = 0; i < overloads.Count; i++)
        for (var j = i + 1; j < overloads.Count; j++)
            if (TryCreateAmbiguityDiagnostic(name, overloads[i], overloads[j], out var diagnostic))
                diagnostics.Add(diagnostic);

        return new(name, overloads, exactBuckets, openEnded, diagnostics);
    }

    private static bool TryCreateAmbiguityDiagnostic<TMethod, TParameter>(
        string name,
        AnalyzedOverload<TMethod, TParameter> left,
        AnalyzedOverload<TMethod, TParameter> right,
        out OverloadDiagnosticInfo diagnostic)
    {
        var minCount = Math.Max(left.RequiredCount, right.RequiredCount);
        var maxCount = Math.Min(left.MaxCount, right.MaxCount);
        if (maxCount < minCount)
        {
            diagnostic = null!;
            return false;
        }

        if (maxCount == int.MaxValue)
            maxCount = Math.Max(minCount, Math.Max(left.FixedCount, right.FixedCount));

        for (var count = minCount; count <= maxCount; count++)
        {
            if (!CouldTieAtCount(left, right, count))
                continue;

            diagnostic = new(
                $"Ambiguous generated overloads for export '{name}'. '{left.Symbol.ToDisplayString()}' and '{right.Symbol.ToDisplayString()}' can tie for {count} argument(s).",
                right.Symbol.Locations.FirstOrDefault(static x => x.IsInSource));
            return true;
        }

        diagnostic = null!;
        return false;
    }

    private static bool CouldTieAtCount<TMethod, TParameter>(
        AnalyzedOverload<TMethod, TParameter> left,
        AnalyzedOverload<TMethod, TParameter> right,
        int count)
    {
        for (var i = 0; i < count; i++)
            if (!CanKindsTie(left.GetParameterAtArgumentIndex(i).MatchKind,
                    right.GetParameterAtArgumentIndex(i).MatchKind))
                return false;

        return true;
    }

    private static bool CanKindsTie(OverloadParameterMatchKind left, OverloadParameterMatchKind right)
    {
        if (left == right)
            return true;

        if (left is OverloadParameterMatchKind.JsValue or OverloadParameterMatchKind.SpanJsValue ||
            right is OverloadParameterMatchKind.JsValue or OverloadParameterMatchKind.SpanJsValue)
            return true;

        if (IsNumeric(left) && IsNumeric(right))
            return true;

        return false;
    }

    private static OverloadParameterMatchKind GetMatchKind(ITypeSymbol type, bool isSpanElement)
    {
        OverloadParameterMatchKind kind;
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Okojo.JsValue")
            kind = OverloadParameterMatchKind.JsValue;
        else if (type.SpecialType == SpecialType.System_String)
            kind = OverloadParameterMatchKind.String;
        else if (type.SpecialType == SpecialType.System_Boolean)
            kind = OverloadParameterMatchKind.Boolean;
        else if (type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
                 SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                 or SpecialType.System_Int64 or
                 SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
                 or SpecialType.System_Decimal)
            kind = OverloadParameterMatchKind.Numeric;
        else if (ParameterTypeSupport.IsTaskLike(type))
            kind = OverloadParameterMatchKind.TaskLike;
        else if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Okojo.Objects.JsObject")
            kind = OverloadParameterMatchKind.JsObject;
        else if (type.SpecialType == SpecialType.System_Object)
            kind = OverloadParameterMatchKind.Object;
        else if (!type.IsValueType)
            kind = OverloadParameterMatchKind.Reference;
        else
            kind = OverloadParameterMatchKind.Other;

        if (!isSpanElement)
            return kind;

        return kind switch
        {
            OverloadParameterMatchKind.JsValue => OverloadParameterMatchKind.SpanJsValue,
            OverloadParameterMatchKind.String => OverloadParameterMatchKind.SpanString,
            OverloadParameterMatchKind.Boolean => OverloadParameterMatchKind.SpanBoolean,
            OverloadParameterMatchKind.Numeric => OverloadParameterMatchKind.SpanNumeric,
            OverloadParameterMatchKind.JsObject => OverloadParameterMatchKind.SpanJsObject,
            OverloadParameterMatchKind.Object => OverloadParameterMatchKind.SpanObject,
            OverloadParameterMatchKind.Reference => OverloadParameterMatchKind.SpanReference,
            _ => OverloadParameterMatchKind.SpanOther
        };
    }

    private static bool IsNumeric(OverloadParameterMatchKind kind)
    {
        return kind is OverloadParameterMatchKind.Numeric or OverloadParameterMatchKind.SpanNumeric;
    }
}
