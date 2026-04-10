namespace Okojo.WebAssembly;

public interface IWasmGlobal : IWasmExtern
{
    WasmGlobalType Type { get; }
    WasmValue GetValue();
    void SetValue(WasmValue value);
}
