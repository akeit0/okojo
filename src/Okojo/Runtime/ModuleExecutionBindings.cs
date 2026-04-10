namespace Okojo.Runtime;

internal sealed class ModuleExecutionBindings(
    string moduleResolvedId,
    JsValue imports,
    JsValue defineLiveExport,
    ModuleVariableSlot[] regularExports,
    ModuleVariableSlot[] regularImports,
    JsValue setFunctionName)
{
    public string ModuleResolvedId { get; } = moduleResolvedId;
    public JsValue Imports { get; } = imports;
    public JsValue DefineLiveExport { get; } = defineLiveExport;
    public ModuleVariableSlot[] RegularExports { get; } = regularExports;
    public ModuleVariableSlot[] RegularImports { get; } = regularImports;
    public JsValue SetFunctionName { get; } = setFunctionName;
    public JsContext? TopLevelContext { get; set; }
}
