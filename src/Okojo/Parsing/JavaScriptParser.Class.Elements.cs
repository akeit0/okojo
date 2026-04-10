namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private JsClassExpression ParseClassExpressionCore(string? name, int nameId, int start,
        IReadOnlyList<JsExpression>? decorators = null)
    {
        var hasExtends = false;
        JsExpression? extendsExpr = null;
        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("extends"))
        {
            hasExtends = true;
            Next();
            extendsExpr = ParseAssignment(true);
        }

        Expect(JsTokenKind.LeftBrace);
        var strictBeforeClass = strictMode;
        strictMode = true;
        try
        {
            var elements = new List<JsClassElement>(4);
            var constructorCount = 0;
            while (current.Kind != JsTokenKind.RightBrace && current.Kind != JsTokenKind.Eof)
            {
                if (Match(JsTokenKind.Semicolon)) continue;

                var elementStart = current.Position;
                var isStatic = false;
                if (current.Kind == JsTokenKind.Identifier &&
                    CurrentSourceTextEquals("static") &&
                    !Peek().HasLineTerminatorBefore &&
                    Peek().Kind is not (JsTokenKind.LeftParen or JsTokenKind.Assign or JsTokenKind.Semicolon
                        or JsTokenKind.RightBrace))
                {
                    isStatic = true;
                    Next();
                    if (current.Kind == JsTokenKind.LeftBrace)
                    {
                        var allowSuperPropertyBeforeBlock = allowSuperProperty;
                        var allowSuperCallBeforeBlock = allowSuperCall;
                        allowSuperProperty = true;
                        allowSuperCall = false;
                        var staticBlockBody = ParseBlockStatement(true);
                        allowSuperProperty = allowSuperPropertyBeforeBlock;
                        allowSuperCall = allowSuperCallBeforeBlock;
                        elements.Add(At(new JsClassElement(
                                null,
                                JsClassElementKind.StaticBlock,
                                null,
                                true,
                                staticBlock: staticBlockBody),
                            elementStart));
                        _ = Match(JsTokenKind.Semicolon);
                        continue;
                    }
                }

                var functionStart = current.Position;

                var isAsyncMethod = false;
                if (current.Kind == JsTokenKind.Identifier &&
                    CurrentSourceTextEquals("async") &&
                    !Peek().HasLineTerminatorBefore &&
                    Peek().Kind is not (JsTokenKind.LeftParen or JsTokenKind.Assign or JsTokenKind.Semicolon
                        or JsTokenKind.RightBrace))
                {
                    isAsyncMethod = true;
                    Next();
                }

                var isGeneratorMethod = Match(JsTokenKind.Star);

                var parsedKey = ParsePropertyName(true, true);
                var key = parsedKey.Key;
                var computedKey = parsedKey.ComputedKey;
                var isComputedKey = parsedKey.IsComputed;
                var isPrivateKey = parsedKey.IsPrivate;

                if (!isGeneratorMethod && !isComputedKey && (key == "get" || key == "set") &&
                    !current.HasLineTerminatorBefore &&
                    current.Kind != JsTokenKind.LeftParen)
                {
                    var parsedAccessorKey = ParsePropertyName(true, true);
                    var accessorKey = parsedAccessorKey.Key ?? string.Empty;
                    var accessorComputedKey = parsedAccessorKey.ComputedKey;
                    var accessorIsPrivate = parsedAccessorKey.IsPrivate;

                    Expect(JsTokenKind.LeftParen);
                    IReadOnlyList<string> accessorParameters;
                    IReadOnlyList<int> accessorParameterIds;
                    IReadOnlyList<JsExpression?> accessorInitializers;
                    IReadOnlyList<JsExpression?> accessorParameterPatterns;
                    IReadOnlyList<int> accessorParameterPositions;
                    IReadOnlyList<JsFormalParameterBindingKind> accessorParameterBindingKinds;
                    int accessorFunctionLength;
                    bool accessorHasSimpleParameterList;
                    bool accessorHasDuplicateParameters;
                    if (key == "get")
                    {
                        var parsedAccessorParams = ParseFormalParameterList();
                        accessorParameters = parsedAccessorParams.Parameters;
                        accessorParameterIds = parsedAccessorParams.ParameterIds;
                        accessorInitializers = parsedAccessorParams.Initializers;
                        accessorParameterPatterns = parsedAccessorParams.ParameterPatterns;
                        accessorParameterPositions = parsedAccessorParams.ParameterPositions;
                        accessorParameterBindingKinds = parsedAccessorParams.ParameterBindingKinds;
                        accessorFunctionLength = parsedAccessorParams.FunctionLength;
                        accessorHasSimpleParameterList = parsedAccessorParams.HasSimpleParameterList;
                        accessorHasDuplicateParameters = parsedAccessorParams.HasDuplicateParameters;
                        if (accessorParameters.Count != 0)
                            throw Error("Getter must not have parameters", current.Position);
                    }
                    else
                    {
                        var parsedAccessorParams = ParseFormalParameterList();
                        accessorParameters = parsedAccessorParams.Parameters;
                        accessorParameterIds = parsedAccessorParams.ParameterIds;
                        accessorInitializers = parsedAccessorParams.Initializers;
                        accessorParameterPatterns = parsedAccessorParams.ParameterPatterns;
                        accessorParameterPositions = parsedAccessorParams.ParameterPositions;
                        accessorParameterBindingKinds = parsedAccessorParams.ParameterBindingKinds;
                        accessorFunctionLength = parsedAccessorParams.FunctionLength;
                        accessorHasSimpleParameterList = parsedAccessorParams.HasSimpleParameterList;
                        accessorHasDuplicateParameters = parsedAccessorParams.HasDuplicateParameters;
                        if (accessorParameters.Count != 1) throw Error("Expected setter parameter", current.Position);

                        var setterParamName = accessorParameters[0];
                        var setterParamId = accessorParameterIds[0];
                        var setterParamPosition = accessorParameterPositions[0];
                        EnsureIdentifierAllowedInCurrentMode(setterParamName, setterParamId, setterParamPosition);
                        if (strictMode && IsEvalOrArguments(setterParamName, setterParamId))
                            throw Error("Unexpected eval or arguments in strict mode", setterParamPosition);
                    }

                    var allowSuperPropertyBeforeBody = allowSuperProperty;
                    var allowSuperCallBeforeBody = allowSuperCall;
                    allowSuperProperty = true;
                    allowSuperCall = false;
                    var accessorBody = ParseBlockStatement(true);
                    allowSuperProperty = allowSuperPropertyBeforeBody;
                    allowSuperCall = allowSuperCallBeforeBody;
                    var accessorFunction = At(new JsFunctionExpression(
                        null,
                        accessorParameters,
                        accessorBody,
                        parameterInitializers: accessorInitializers,
                        parameterPatterns: accessorParameterPatterns,
                        parameterPositions: accessorParameterPositions,
                        parameterBindingKinds: accessorParameterBindingKinds,
                        functionLength: accessorFunctionLength,
                        hasSimpleParameterList: accessorHasSimpleParameterList,
                        hasDuplicateParameters: accessorHasDuplicateParameters,
                        parameterIds: accessorParameterIds), functionStart);
                    elements.Add(At(new JsClassElement(
                            accessorKey,
                            key == "get" ? JsClassElementKind.Getter : JsClassElementKind.Setter,
                            accessorFunction,
                            isStatic,
                            accessorComputedKey,
                            isPrivate: accessorIsPrivate),
                        elementStart));
                }
                else
                {
                    if (current.Kind != JsTokenKind.LeftParen)
                    {
                        JsExpression? fieldInitializer = null;
                        if (Match(JsTokenKind.Assign))
                        {
                            var allowSuperPropertyBeforeInitializer = allowSuperProperty;
                            allowSuperProperty = true;
                            try
                            {
                                fieldInitializer = ParseAssignment(true);
                            }
                            finally
                            {
                                allowSuperProperty = allowSuperPropertyBeforeInitializer;
                            }
                        }

                        elements.Add(At(new JsClassElement(
                                key,
                                JsClassElementKind.Field,
                                null,
                                isStatic,
                                computedKey,
                                fieldInitializer,
                                isPrivate: isPrivateKey),
                            elementStart));
                        _ = Match(JsTokenKind.Semicolon);
                        continue;
                    }

                    Expect(JsTokenKind.LeftParen);
                    var isDerivedConstructor =
                        !isStatic &&
                        string.Equals(key, "constructor", StringComparison.Ordinal) &&
                        hasExtends;
                    var allowSuperPropertyBeforeParameters = allowSuperProperty;
                    var allowSuperCallBeforeParameters = allowSuperCall;
                    allowSuperProperty = true;
                    allowSuperCall = isDerivedConstructor;
                    JsParsedFormalParameters parsedParams;
                    try
                    {
                        parsedParams = ParseFormalParameterList();
                    }
                    finally
                    {
                        allowSuperProperty = allowSuperPropertyBeforeParameters;
                        allowSuperCall = allowSuperCallBeforeParameters;
                    }

                    var parameters = parsedParams.Parameters;
                    var parameterIds = parsedParams.ParameterIds;
                    var parameterInitializers = parsedParams.Initializers;
                    var parameterPatterns = parsedParams.ParameterPatterns;
                    var parameterPositions = parsedParams.ParameterPositions;
                    var functionLength = parsedParams.FunctionLength;
                    var hasSimpleParameterList = parsedParams.HasSimpleParameterList;
                    var hasDuplicateParameters = parsedParams.HasDuplicateParameters;
                    var restParameterIndex = parsedParams.RestParameterIndex;
                    var allowSuperPropertyBeforeBody = allowSuperProperty;
                    var allowSuperCallBeforeBody = allowSuperCall;
                    var generatorLevelBeforeMethodBody = generatorFunctionLevel;
                    var asyncLevelBeforeMethodBody = asyncFunctionLevel;
                    allowSuperProperty = true;
                    allowSuperCall = isDerivedConstructor;
                    generatorFunctionLevel = isGeneratorMethod ? generatorLevelBeforeMethodBody + 1 : 0;
                    asyncFunctionLevel = isAsyncMethod ? asyncLevelBeforeMethodBody + 1 : 0;
                    var body = ParseBlockStatement(true);
                    allowSuperProperty = allowSuperPropertyBeforeBody;
                    allowSuperCall = allowSuperCallBeforeBody;
                    generatorFunctionLevel = generatorLevelBeforeMethodBody;
                    asyncFunctionLevel = asyncLevelBeforeMethodBody;
                    for (var i = 0; i < parameters.Count; i++)
                        if (IsEvalOrArguments(parameters[i], parameterIds[i]))
                            throw Error("Unexpected eval or arguments in strict mode", parameterPositions[i]);

                    var kind = JsClassElementKind.Method;
                    if (!isComputedKey && !isStatic && string.Equals(key, "constructor", StringComparison.Ordinal))
                    {
                        kind = JsClassElementKind.Constructor;
                        constructorCount++;
                        if (constructorCount > 1) throw Error("Duplicate constructor in class", elementStart);
                    }

                    if (isGeneratorMethod && !isComputedKey && !isStatic &&
                        string.Equals(key, "constructor", StringComparison.Ordinal))
                        throw Error("Class constructor may not be a generator", elementStart);

                    var function = At(new JsFunctionExpression(
                        null,
                        parameters,
                        body,
                        isGeneratorMethod,
                        isAsyncMethod,
                        parameterInitializers: parameterInitializers,
                        parameterPatterns: parameterPatterns,
                        parameterPositions: parameterPositions,
                        parameterBindingKinds: parsedParams.ParameterBindingKinds,
                        functionLength: functionLength,
                        hasSimpleParameterList: hasSimpleParameterList,
                        hasDuplicateParameters: hasDuplicateParameters,
                        restParameterIndex: restParameterIndex,
                        parameterIds: parameterIds), functionStart);
                    elements.Add(At(
                        new JsClassElement(key, kind, function, isStatic, computedKey, isPrivate: isPrivateKey),
                        elementStart));
                }

                _ = Match(JsTokenKind.Semicolon);
            }

            var endPosition = current.Position + 1;
            Expect(JsTokenKind.RightBrace);
            var classExpression = At(new JsClassExpression(name, elements, decorators, hasExtends, extendsExpr, nameId),
                start);
            classExpression.EndPosition = endPosition;
            return classExpression;
        }
        finally
        {
            strictMode = strictBeforeClass;
        }
    }
}
