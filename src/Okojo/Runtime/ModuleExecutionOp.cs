using Okojo.Parsing;

namespace Okojo.Runtime;

internal sealed class ModuleExecutionOp(
    ModuleExecutionOpKind kind,
    JsStatement? statement,
    JsExpression? expression,
    string? exportLocalName,
    bool setDefaultName)
{
    public ModuleExecutionOpKind Kind { get; } = kind;
    public JsStatement? Statement { get; } = statement;
    public JsExpression? Expression { get; } = expression;
    public string? ExportLocalName { get; } = exportLocalName;
    public bool SetDefaultName { get; } = setDefaultName;
}
