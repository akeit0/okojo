namespace Okojo.WebAssembly;

public readonly record struct WasmMemoryType(
    long MinimumPages,
    long? MaximumPages,
    bool IsShared = false,
    bool Is64Bit = false);
