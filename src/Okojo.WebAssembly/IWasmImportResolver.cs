namespace Okojo.WebAssembly;

public interface IWasmImportResolver
{
    bool TryResolveImport(WasmImportDescriptor descriptor, out IWasmExtern wasmExtern);
}
