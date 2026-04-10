namespace Okojo.Parsing;

internal readonly struct JsToken(
    JsTokenKind kind,
    int position,
    int sourceLength,
    double numberLiteral = 0,
    int dataIndex = -1,
    bool hasLineTerminatorBefore = false)
{
    public JsTokenKind Kind { get; } = kind;
    public int Position { get; } = position;
    public int SourceLength { get; } = sourceLength;
    public int Length => SourceLength;
    public double NumberLiteral { get; } = numberLiteral;
    public int DataIndex { get; } = dataIndex;
    public int IdentifierId => DataIndex;
    public bool HasLineTerminatorBefore { get; } = hasLineTerminatorBefore;
}
