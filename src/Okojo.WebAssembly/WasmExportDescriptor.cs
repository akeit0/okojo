namespace Okojo.WebAssembly;

public sealed class WasmExportDescriptor(string name, WasmExternalKind kind, object type)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public WasmExternalKind Kind { get; } = kind;
    public object Type { get; } = type ?? throw new ArgumentNullException(nameof(type));
}
