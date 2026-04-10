namespace Okojo.Runtime;

internal enum ModuleExecutionOpKind : byte
{
    ExecuteStatement = 0,
    ExportDefaultExpression = 1,
    InitializeHoistedDefaultExport = 2
}
