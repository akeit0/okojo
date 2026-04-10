namespace Okojo.WebAssembly;

public readonly record struct WasmTableType(WasmValueKind ElementKind, uint MinimumElements, uint? MaximumElements);
