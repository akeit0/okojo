namespace Okojo.WebAssembly;

public sealed class WasmImportDescriptor(string moduleName, string name, WasmExternalKind kind, object type)
{
    public string ModuleName { get; } = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public WasmExternalKind Kind { get; } = kind;
    public object Type { get; } = type ?? throw new ArgumentNullException(nameof(type));
}
