namespace Okojo.WebAssembly;

public delegate void WasmHostFunctionCallback(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> returnValues);
