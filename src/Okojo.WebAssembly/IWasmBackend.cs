namespace Okojo.WebAssembly;

public interface IWasmBackend
{
    bool Validate(ReadOnlySpan<byte> wasm);

    IWasmCompiledModule Compile(ReadOnlySpan<byte> wasm);

    IWasmInstance Instantiate(
        IWasmCompiledModule module,
        IWasmImportResolver imports);

    IWasmFunction CreateFunction(WasmFunctionType type, WasmHostFunctionCallback callback);
    IWasmMemory CreateMemory(WasmMemoryType type);
    IWasmTable CreateTable(WasmTableType type, object? initialValue = null);
    IWasmGlobal CreateGlobal(WasmGlobalType type, WasmValue initialValue);
}
