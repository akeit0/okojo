namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private JsFunctionExpression ParseFunctionExpression(bool isAsyncPrefix = false)
    {
        using var depthCounter = AddDepth();
        MarkNestedFunctionSyntaxSeen();
        var start = current.Position;
        var isAsync = false;
        if (isAsyncPrefix)
        {
            isAsync = true;
            Expect(JsTokenKind.Identifier); // async
        }

        Expect(JsTokenKind.Function);
        var isGenerator = Match(JsTokenKind.Star);

        string? name = null;
        var nameId = -1;
        if (current.Kind == JsTokenKind.Identifier)
        {
            var identifier = ParseCheckedIdentifierName(current);
            name = identifier.Name;
            nameId = identifier.NameId;
            Next();
        }

        Expect(JsTokenKind.LeftParen);
        var generatorLevelBeforeParams = generatorFunctionLevel;
        if (!isGenerator)
            // In non-generator function parameter initializers, `yield` is an identifier.
            generatorFunctionLevel = 0;

        var parsedParams = ParseFormalParameterList();
        generatorFunctionLevel = generatorLevelBeforeParams;
        var parameters = parsedParams.Parameters;
        var parameterIds = parsedParams.ParameterIds;
        var parameterInitializers = parsedParams.Initializers;
        var parameterPatterns = parsedParams.ParameterPatterns;
        var parameterPositions = parsedParams.ParameterPositions;
        var parameterBindingKinds = parsedParams.ParameterBindingKinds;
        var functionLength = parsedParams.FunctionLength;
        var hasSimpleParameterList = parsedParams.HasSimpleParameterList;
        var hasDuplicateParameters = parsedParams.HasDuplicateParameters;
        var restParameterIndex = parsedParams.RestParameterIndex;

        var strictBeforeBody = strictMode;
        var generatorLevelBeforeBody = generatorFunctionLevel;
        var asyncLevelBeforeBody = asyncFunctionLevel;
        var allowSuperPropertyBeforeBody = allowSuperProperty;
        var allowSuperCallBeforeBody = allowSuperCall;
        generatorFunctionLevel = isGenerator ? generatorLevelBeforeBody + 1 : 0;
        asyncFunctionLevel = isAsync ? asyncLevelBeforeBody + 1 : 0;

        allowSuperProperty = false;
        allowSuperCall = false;
        var body = ParseBlockStatement(true);
        if (!strictBeforeBody && strictMode)
            for (var i = 0; i < parameters.Count; i++)
                if (IsEvalOrArguments(parameters[i], parameterIds[i]))
                    throw Error("Unexpected eval or arguments in strict mode", parameterPositions[i]);

        strictMode = strictBeforeBody;
        generatorFunctionLevel = generatorLevelBeforeBody;
        asyncFunctionLevel = asyncLevelBeforeBody;
        allowSuperProperty = allowSuperPropertyBeforeBody;
        allowSuperCall = allowSuperCallBeforeBody;

        return At(new JsFunctionExpression(
            name,
            parameters,
            body,
            isGenerator,
            isAsync,
            parameterInitializers: parameterInitializers,
            parameterPatterns: parameterPatterns,
            parameterPositions: parameterPositions,
            parameterBindingKinds: parameterBindingKinds,
            functionLength: functionLength,
            hasSimpleParameterList: hasSimpleParameterList,
            hasDuplicateParameters: hasDuplicateParameters,
            restParameterIndex: restParameterIndex,
            nameId: nameId,
            parameterIds: parameterIds), start);
    }
}
