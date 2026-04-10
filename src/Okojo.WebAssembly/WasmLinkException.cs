namespace Okojo.WebAssembly;

public sealed class WasmLinkException(string message, Exception? innerException = null)
    : WasmException(message, innerException);
