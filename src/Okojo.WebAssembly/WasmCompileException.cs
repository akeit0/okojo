namespace Okojo.WebAssembly;

public sealed class WasmCompileException(string message, Exception? innerException = null)
    : WasmException(message, innerException);
