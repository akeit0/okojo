namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private JsFunctionExpression ParseArrowFunctionExpressionCore(
        JsParsedFormalParameters parsedParameters,
        bool isAsync = false)
    {
        MarkNestedFunctionSyntaxSeen();
        var strictBeforeBody = strictMode;
        var asyncLevelBeforeBody = asyncFunctionLevel;
        if (isAsync)
            asyncFunctionLevel = asyncLevelBeforeBody + 1;

        JsBlockStatement body;
        try
        {
            if (current.Kind == JsTokenKind.LeftBrace)
            {
                body = ParseBlockStatement(true);
            }
            else
            {
                var expr = ParseAssignment(true);
                body = new(new JsStatement[]
                {
                    new JsReturnStatement(expr)
                }, false);
            }
        }
        finally
        {
            asyncFunctionLevel = asyncLevelBeforeBody;
        }

        if (!strictBeforeBody && strictMode)
            for (var i = 0; i < parsedParameters.Parameters.Count; i++)
                if (IsEvalOrArguments(parsedParameters.Parameters[i], parsedParameters.ParameterIds[i]))
                    throw Error("Unexpected eval or arguments in strict mode", parsedParameters.ParameterPositions[i]);

        strictMode = strictBeforeBody;

        var parameters = parsedParameters.Parameters;
        var positions = parsedParameters.ParameterPositions;
        var bindingKinds = parsedParameters.ParameterBindingKinds;
        var initializers = parsedParameters.Initializers;
        var patterns = parsedParameters.ParameterPatterns;
        return new(
            null,
            parameters,
            body,
            false,
            isAsync,
            true,
            initializers,
            patterns,
            positions,
            bindingKinds,
            parsedParameters.FunctionLength,
            parsedParameters.HasSimpleParameterList,
            allowSuperProperty,
            parsedParameters.HasDuplicateParameters,
            parsedParameters.RestParameterIndex,
            parameterIds: parsedParameters.ParameterIds);
    }

    private static bool TryExtractArrowParameters(JsExpression head, out IReadOnlyList<string> parameters)
    {
        switch (head)
        {
            case JsIdentifierExpression id:
                parameters = new[] { id.Name };
                return true;
            case JsSequenceExpression seq:
                {
                    var list = new string[seq.Expressions.Count];
                    for (var i = 0; i < seq.Expressions.Count; i++)
                    {
                        if (seq.Expressions[i] is not JsIdentifierExpression p)
                        {
                            parameters = Array.Empty<string>();
                            return false;
                        }

                        list[i] = p.Name;
                    }

                    parameters = list;
                    return true;
                }
            default:
                parameters = Array.Empty<string>();
                return false;
        }
    }

    private bool TryParseArrowFunctionExpression(int start, out JsExpression expr)
    {
        var snapshot = CaptureSnapshot();
        expr = default!;

        try
        {
            if (current.Kind == JsTokenKind.Identifier &&
                CurrentSourceTextEquals("async") &&
                !Peek().HasLineTerminatorBefore)
            {
                Next();
                if ((current.Kind == JsTokenKind.Identifier || current.Kind == JsTokenKind.Of) &&
                    Peek().Kind == JsTokenKind.Arrow)
                {
                    var paramToken = current;
                    var param = ParseCheckedIdentifierName(paramToken, true);
                    Next();
                    Expect(JsTokenKind.Arrow);
                    expr = At(ParseArrowFunctionExpressionCore(
                        CreateSimpleArrowParameters(new[] { param.Name }, new[] { param.NameId },
                            new[] { param.Position }),
                        true), start);
                    return true;
                }

                if (current.Kind == JsTokenKind.LeftParen)
                {
                    if (Peek().Kind == JsTokenKind.RightParen)
                    {
                        Next();
                        Expect(JsTokenKind.RightParen);
                        if (Match(JsTokenKind.Arrow))
                        {
                            expr = At(ParseArrowFunctionExpressionCore(JsParsedFormalParameters.Empty, true), start);
                            return true;
                        }

                        return false;
                    }

                    Expect(JsTokenKind.LeftParen);
                    var parsed = ParseFormalParameterList();
                    if (!Match(JsTokenKind.Arrow))
                        return false;

                    expr = At(ParseArrowFunctionExpressionCore(parsed, true), start);
                    return true;
                }

                return false;
            }

            if (current.Kind != JsTokenKind.LeftParen)
                return false;

            Next();
            if (current.Kind == JsTokenKind.RightParen)
            {
                Next();
                if (!Match(JsTokenKind.Arrow))
                    return false;

                expr = At(ParseArrowFunctionExpressionCore(JsParsedFormalParameters.Empty), start);
                return true;
            }

            var parsedParams = ParseFormalParameterList();
            if (!Match(JsTokenKind.Arrow))
                return false;

            expr = At(ParseArrowFunctionExpressionCore(parsedParams), start);
            return true;
        }
        catch (JsParseException ex) when (!IsDepthLimitExceeded(ex))
        {
            RestoreSnapshot(snapshot);
            return false;
        }
        finally
        {
            if (expr is null)
                RestoreSnapshot(snapshot);
        }
    }

    private static JsParsedFormalParameters CreateSimpleArrowParameters(
        IReadOnlyList<string> parameters,
        IReadOnlyList<int> parameterIds,
        IReadOnlyList<int>? positions = null)
    {
        var count = parameters.Count;
        if (count == 0 && positions is null)
            return JsParsedFormalParameters.Empty;

        var initializers = new List<JsExpression?>(count);
        var patterns = new List<JsExpression?>(count);
        var bindingKinds = new List<JsFormalParameterBindingKind>(count);
        for (var i = 0; i < count; i++)
        {
            initializers.Add(null);
            patterns.Add(null);
            bindingKinds.Add(JsFormalParameterBindingKind.Plain);
        }

        return new(
            count == 0 ? Array.Empty<string>() : new List<string>(parameters).ToArray(),
            count == 0 ? Array.Empty<int>() : new List<int>(parameterIds).ToArray(),
            count == 0 ? Array.Empty<JsExpression?>() : initializers.ToArray(),
            count == 0 ? Array.Empty<JsExpression?>() : patterns.ToArray(),
            positions is null
                ? count == 0
                    ? Array.Empty<int>()
                    : new List<int>(JsFunctionExpression.CreateDefaultParameterPositions(count)).ToArray()
                : count == 0
                    ? Array.Empty<int>()
                    : new List<int>(positions).ToArray(),
            count == 0 ? Array.Empty<JsFormalParameterBindingKind>() : bindingKinds.ToArray(),
            count,
            true,
            false,
            -1);
    }
}
