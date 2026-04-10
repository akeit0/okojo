namespace Okojo.WebAssembly;

public class WasmException(string message, Exception? innerException = null) : Exception(message, innerException);
