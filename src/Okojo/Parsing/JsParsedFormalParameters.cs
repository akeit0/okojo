namespace Okojo.Parsing;

internal sealed class JsParsedFormalParameters(
    IReadOnlyList<string> parameters,
    IReadOnlyList<int> parameterIds,
    IReadOnlyList<JsExpression?> initializers,
    IReadOnlyList<JsExpression?> parameterPatterns,
    IReadOnlyList<int> parameterPositions,
    IReadOnlyList<JsFormalParameterBindingKind> parameterBindingKinds,
    int functionLength,
    bool hasSimpleParameterList,
    bool hasDuplicateParameters,
    int restParameterIndex)
{
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public IReadOnlyList<int> ParameterIds { get; } = parameterIds;
    public IReadOnlyList<JsExpression?> Initializers { get; } = initializers;
    public IReadOnlyList<JsExpression?> ParameterPatterns { get; } = parameterPatterns;
    public IReadOnlyList<int> ParameterPositions { get; } = parameterPositions;
    public IReadOnlyList<JsFormalParameterBindingKind> ParameterBindingKinds { get; } = parameterBindingKinds;
    public int FunctionLength { get; } = functionLength;
    public bool HasSimpleParameterList { get; } = hasSimpleParameterList;
    public bool HasDuplicateParameters { get; } = hasDuplicateParameters;
    public int RestParameterIndex { get; } = restParameterIndex;

    public static JsParsedFormalParameters Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<int>(),
        Array.Empty<JsExpression?>(),
        Array.Empty<JsExpression?>(),
        Array.Empty<int>(),
        Array.Empty<JsFormalParameterBindingKind>(),
        0,
        true,
        false,
        -1);
}
