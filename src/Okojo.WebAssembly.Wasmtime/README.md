# Okojo.WebAssembly.Wasmtime

`Okojo.WebAssembly.Wasmtime` provides a `WasmtimeBackend` implementation for `Okojo.WebAssembly`.

## Typical use

```csharp
using Okojo;
using Okojo.WebAssembly;
using Okojo.WebAssembly.Wasmtime;

using var runtime = JsRuntime.CreateBuilder()
    .UseWebAssembly(wasm => wasm
        .UseBackend(static () => new WasmtimeBackend())
        .InstallGlobals())
    .Build();
```

Reference both `Okojo.WebAssembly` and `Okojo.WebAssembly.Wasmtime` when enabling WebAssembly globals with Wasmtime.
