using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator;

internal static class MethodOverloadDispatchEmitter
{
    public static void EmitDispatcher<TMethod, TParameter>(
        StringBuilder sb,
        string dispatcherMethodName,
        string mismatchMessage,
        AnalyzedOverloadSet<TMethod, TParameter> overloadSet,
        bool isStaticDispatcher,
        Func<TMethod, IMethodSymbol> getMethod,
        Func<TMethod, IReadOnlyList<TParameter>> getParameters,
        Func<TParameter, ITypeSymbol> getParameterType,
        Func<TParameter, bool> hasDefaultValue,
        Func<int, string> getOverloadMethodName)
    {
        sb.Append("    private ")
            .Append(isStaticDispatcher ? "static " : string.Empty)
            .Append("global::Okojo.JsValue ")
            .Append(dispatcherMethodName)
            .AppendLine("(scoped in global::Okojo.Runtime.CallInfo info)");
        sb.AppendLine("    {");
        sb.AppendLine("        int __jsArgCount = info.ArgumentCount;");
        sb.AppendLine("        scoped global::System.ReadOnlySpan<global::Okojo.JsValue> __jsArgs = info.Arguments;");
        sb.AppendLine("        int __jsBestIndex = -1;");
        sb.AppendLine("        int __jsBestScore = int.MaxValue;");
        sb.AppendLine();
        sb.AppendLine("        switch (__jsArgCount)");
        sb.AppendLine("        {");

        for (var bucketIndex = 0; bucketIndex < overloadSet.ExactCountBuckets.Count; bucketIndex++)
        {
            var bucket = overloadSet.ExactCountBuckets[bucketIndex];
            sb.Append("            case ").Append(bucket.ExactCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine(":");
            sb.AppendLine("            {");
            var hoistedCount = 0;
            for (var candidateIndex = 0; candidateIndex < bucket.Candidates.Count; candidateIndex++)
                if (bucket.Candidates[candidateIndex].FixedCount > hoistedCount)
                    hoistedCount = bucket.Candidates[candidateIndex].FixedCount;
            EmitHoistedArgs(sb, hoistedCount, "                ");
            EmitBucketCandidateChecks(
                sb,
                bucket.Candidates,
                overloadSet.Overloads,
                "                ",
                false);
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine("            {");
        if (overloadSet.OpenEndedCandidates.Count != 0)
        {
            sb.AppendLine("                if (__jsArgCount != 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    var __jsArg0 = __jsArgs[0];");
            sb.AppendLine("                    _ = __jsArg0;");
            sb.AppendLine("                }");
            EmitBucketCandidateChecks(
                sb,
                overloadSet.OpenEndedCandidates,
                overloadSet.Overloads,
                "                ",
                true);
        }

        sb.AppendLine("                break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        switch (__jsBestIndex)");
        sb.AppendLine("        {");
        for (var i = 0; i < overloadSet.Overloads.Count; i++)
            sb.Append("            case ")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(": return ")
                .Append(getOverloadMethodName(i))
                .AppendLine("(info);");

        sb.AppendLine("        }");
        sb.Append(
                "        throw new global::Okojo.Runtime.JsRuntimeException(global::Okojo.Runtime.JsErrorKind.TypeError, \"")
            .Append(EscapeString(mismatchMessage))
            .AppendLine("\");");
        sb.AppendLine("    }");
    }

    private static void EmitHoistedArgs(StringBuilder sb, int count, string indent)
    {
        for (var i = 0; i < count; i++)
            sb.Append(indent)
                .Append("var __jsArg")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(" = __jsArgCount > ")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(" ? __jsArgs[")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append("] : global::Okojo.JsValue.Undefined;")
                .AppendLine();
    }

    private static void EmitBucketCandidateChecks<TMethod, TParameter>(
        StringBuilder sb,
        IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> candidates,
        IReadOnlyList<AnalyzedOverload<TMethod, TParameter>> allOverloads,
        string indent,
        bool useCountCondition)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var overload = candidates[i];
            if (useCountCondition)
            {
                sb.Append(indent)
                    .Append("if (__jsArgCount < ")
                    .Append(overload.RequiredCount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(")");
                sb.Append(indent).AppendLine("    break;");
            }

            sb.Append(indent).AppendLine("{");
            sb.Append(indent).AppendLine("    int __jsScore = 0;");
            sb.Append(indent).AppendLine("    bool __jsMatched = true;");
            EmitOverloadMatcherBody(sb, overload, indent + "    ", !useCountCondition);
            var overloadIndex = -1;
            for (var overloadCursor = 0; overloadCursor < allOverloads.Count; overloadCursor++)
                if (ReferenceEquals(allOverloads[overloadCursor], overload))
                {
                    overloadIndex = overloadCursor;
                    break;
                }

            sb.Append(indent).AppendLine("    if (__jsMatched && __jsScore < __jsBestScore)");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).Append("        __jsBestIndex = ")
                .Append(overloadIndex.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            sb.Append(indent).AppendLine("        __jsBestScore = __jsScore;");
            sb.Append(indent).AppendLine("    }");

            sb.Append(indent).AppendLine("}");
        }
    }

    private static void EmitOverloadMatcherBody<TMethod, TParameter>(
        StringBuilder sb,
        AnalyzedOverload<TMethod, TParameter> overload,
        string indent,
        bool useHoistedArgs)
    {
        var fixedCount = overload.FixedCount;
        var tempIndex = 0;
        for (var i = 0; i < fixedCount; i++)
            if (overload.ParameterSpecs[i].HasDefaultValue)
            {
                sb.Append(indent)
                    .Append("if (__jsMatched && __jsArgCount > ")
                    .Append(i.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(")");
                sb.Append(indent).AppendLine("{");
                EmitParameterScoreCheck(sb, overload.ParameterSpecs[i], i, indent + "    ", useHoistedArgs,
                    ref tempIndex);
                sb.Append(indent).AppendLine("}");
            }
            else
            {
                sb.Append(indent).AppendLine("if (__jsMatched)");
                sb.Append(indent).AppendLine("{");
                EmitParameterScoreCheck(sb, overload.ParameterSpecs[i], i, indent + "    ", useHoistedArgs,
                    ref tempIndex);
                sb.Append(indent).AppendLine("}");
            }

        if (!overload.HasOpenEndedCount)
            return;

        sb.Append(indent)
            .Append("for (int __jsIndex = ")
            .Append(overload.FixedCount.ToString(CultureInfo.InvariantCulture))
            .AppendLine("; __jsIndex < __jsArgCount; __jsIndex++)");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    if (!__jsMatched)");
        sb.Append(indent).AppendLine("        break;");
        EmitParameterScoreCheck(sb, overload.ParameterSpecs[overload.ParameterSpecs.Count - 1], -1, indent + "    ",
            false, ref tempIndex, 2);
        sb.Append(indent).AppendLine("}");
    }

    private static void EmitParameterScoreCheck(
        StringBuilder sb,
        OverloadParameterSpec parameter,
        int argumentIndex,
        string indent,
        bool useHoistedArgs,
        ref int tempIndex,
        int additionalScore = 0)
    {
        var argExpr = argumentIndex >= 0
            ? useHoistedArgs
                ? "__jsArg" + argumentIndex.ToString(CultureInfo.InvariantCulture)
                : "__jsArgs[" + argumentIndex.ToString(CultureInfo.InvariantCulture) + "]"
            : "__jsArgs[__jsIndex]";

        switch (parameter.MatchKind)
        {
            case OverloadParameterMatchKind.JsValue:
            case OverloadParameterMatchKind.SpanJsValue:
                AppendScoreAdd(sb, indent, "0", additionalScore);
                return;
            case OverloadParameterMatchKind.String:
            case OverloadParameterMatchKind.SpanString:
                sb.Append(indent).Append("if (").Append(argExpr).AppendLine(".IsString)");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", "0", additionalScore);
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).Append("else if (").Append(argExpr).AppendLine(".IsNull)");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", "1", additionalScore);
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", "30", additionalScore);
                sb.Append(indent).AppendLine("}");
                return;
            case OverloadParameterMatchKind.Boolean:
            case OverloadParameterMatchKind.SpanBoolean:
                sb.Append(indent).Append("if (").Append(argExpr).AppendLine(".IsBool)");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", "0", additionalScore);
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", "30", additionalScore);
                sb.Append(indent).AppendLine("}");
                return;
            case OverloadParameterMatchKind.TaskLike:
                AppendScoreAdd(sb, indent, "0", additionalScore);
                return;
            case OverloadParameterMatchKind.Numeric:
            case OverloadParameterMatchKind.SpanNumeric:
            case OverloadParameterMatchKind.JsObject:
            case OverloadParameterMatchKind.SpanJsObject:
            case OverloadParameterMatchKind.Object:
            case OverloadParameterMatchKind.SpanObject:
            case OverloadParameterMatchKind.Reference:
            case OverloadParameterMatchKind.SpanReference:
            case OverloadParameterMatchKind.Other:
            case OverloadParameterMatchKind.SpanOther:
                var scoreName = "__jsArgScore" + tempIndex.ToString(CultureInfo.InvariantCulture);
                tempIndex++;
                sb.Append(indent)
                    .Append("if (!info.TryGetConversionScore<")
                    .Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(in ")
                    .Append(argExpr)
                    .Append(", out var ")
                    .Append(scoreName)
                    .AppendLine("))");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).AppendLine("    __jsMatched = false;");
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                AppendScoreAdd(sb, indent + "    ", scoreName, additionalScore);
                sb.Append(indent).AppendLine("}");
                return;
            default:
                sb.Append(indent).AppendLine("__jsMatched = false;");
                return;
        }
    }

    private static void AppendScoreAdd(StringBuilder sb, string indent, string scoreExpression, int additionalScore)
    {
        sb.Append(indent).Append("__jsScore += ").Append(scoreExpression);
        if (additionalScore != 0)
            sb.Append(" + ").Append(additionalScore.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(";");
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
