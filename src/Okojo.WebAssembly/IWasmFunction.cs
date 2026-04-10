namespace Okojo.WebAssembly;

public interface IWasmFunction : IWasmExtern
{
    WasmFunctionType Type { get; }
    void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> returnValues);
}
