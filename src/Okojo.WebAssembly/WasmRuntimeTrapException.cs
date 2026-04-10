namespace Okojo.WebAssembly;

public sealed class WasmRuntimeTrapException(string message, Exception? innerException = null)
    : WasmException(message, innerException);
