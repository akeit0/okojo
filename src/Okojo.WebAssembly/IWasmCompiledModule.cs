namespace Okojo.WebAssembly;

public interface IWasmCompiledModule : IDisposable
{
    IReadOnlyList<WasmImportDescriptor> Imports { get; }
    IReadOnlyList<WasmExportDescriptor> Exports { get; }
}
