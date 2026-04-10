# Okojo

Okojo is a managed JavaScript engine for .NET. This repository is being prepared for a **public prerelease**, with the first NuGet wave focused on the core engine and opt-in host/runtime layers rather than the full browser or Node stack.

## Prerelease package set

| Package | Purpose |
| --- | --- |
| `Okojo` | Core JavaScript engine and embedding API |
| `Okojo.Hosting` | Host schedulers, queues, workers, and runtime helpers |
| `Okojo.Diagnostics` | Value formatting and bytecode disassembly helpers |
| `Okojo.Reflection` | Opt-in reflection-based CLR interop |
| `Okojo.WebPlatform` | `fetch`, timers, workers, and web/server host helpers |
| `Okojo.WebAssembly` | Backend-agnostic WebAssembly integration |
| `Okojo.WebAssembly.Wasmtime` | Wasmtime backend for `Okojo.WebAssembly` |

Not published yet:

- `Okojo.Browser`
- `Okojo.Node`
- `Okojo.Node.Cli`
- `Okojo.RegExp`
- `Okojo.Repl`
- debug server packages
- doc generator and other repo tooling

## Current status

- primary target: correctness first, then observability, then measured optimization
- non-legacy, non-staging `test262` coverage is passing in the current working baseline
- legacy/deprecated language corners remain intentionally out of scope unless re-approved
- public surface is still prerelease and expected to be refined

## Requirements

- .NET 10 SDK

## Quick start

```csharp
using Okojo;
using Okojo.Hosting;
using Okojo.WebPlatform;

using var runtime = JsRuntime.CreateBuilder()
    .UseThreadPoolHosting()
    .UseFetch()
    .Build();

var realm = runtime.MainRealm;
var value = realm.Evaluate("1 + 2");

Console.WriteLine(value);
```

Useful entry points:

- `JsRuntime.CreateBuilder()`
- `JsRuntime.Create(...)`
- `JsRuntime.MainRealm`
- `JsRuntime.CreateRealm()`
- `JsRealm.Evaluate(...)`
- `JsRealm.Execute(...)`
- `JsRealm.Import(...)`
- `JsRealm.LoadModule(...)`

## Examples

Public-facing samples now live under [`examples`](examples):

- `examples\OkojoModuleSample`
- `examples\OkojoModuleSampleRunner`
- `examples\OkojoGameLoopSandbox`
- `examples\OkojoHostEventLoopSandbox`
- `examples\OkojoArtSandbox`

Internal probes, debug apps, and bring-up sandboxes remain under [`sandbox`](sandbox).

## Repository guide

- `src\Okojo` - core engine
- `src\Okojo.Hosting` - host queues, worker helpers, and scheduling
- `src\Okojo.Diagnostics` - formatting and disassembly helpers
- `src\Okojo.Reflection` - reflection-backed CLR interop
- `src\Okojo.WebPlatform` - web/server host APIs
- `src\Okojo.WebAssembly` - WebAssembly integration surface
- `src\Okojo.WebAssembly.Wasmtime` - Wasmtime backend
- `examples` - public sample apps
- `sandbox` - internal probes and experimental bring-up apps
- `tests` - unit and integration tests
- `tools` - bytecode, Test262, and other development tools

## Development

Fast validation loop:

```powershell
dotnet test tests\Okojo.Tests\Okojo.Tests.csproj
```

Examples and package metadata are still being refined for the first public prerelease.

## Licensing

See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
