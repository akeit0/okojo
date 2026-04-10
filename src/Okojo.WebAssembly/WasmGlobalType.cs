namespace Okojo.WebAssembly;

public readonly record struct WasmGlobalType(WasmValueKind ValueKind, WasmMutability Mutability);
