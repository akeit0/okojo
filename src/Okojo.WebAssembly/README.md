# Okojo.WebAssembly

`Okojo.WebAssembly` is the backend-agnostic WebAssembly integration layer for Okojo.

It provides:

- the `IWasmBackend` abstraction
- runtime builder extensions for WebAssembly setup
- `WebAssembly` global installation support

## Typical use

```csharp
using Okojo;
using Okojo.WebAssembly;

using var runtime = JsRuntime.CreateBuilder()
    .UseWebAssembly(wasm => wasm
        .UseBackend(CreateBackend)
        .InstallGlobals())
    .Build();
```

Use `Okojo.WebAssembly.Wasmtime` when you want the packaged Wasmtime backend.
