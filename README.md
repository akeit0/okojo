# Okojo

Okojo is an experimental low allocation managed JavaScript engine for .NET, aimed at **correctness first**, strong observability/tooling, and practical host integration for modern ECMAScript workloads.

This repository contains the engine itself, host/runtime layers, web-platform support, WebAssembly integration, examples, Test262 infrastructure, and internal sandboxes used to drive compatibility and packaging work.

## What Okojo is trying to be

The current direction is:

- a correct, observable JavaScript engine for .NET
- a clean embedding surface for host applications
- a base for browser-compatibility and Node-compatibility work
- a package set that can be consumed in layers instead of one monolithic runtime

The project is still **prerelease**. Public APIs and package boundaries are being refined, but the repo is already organized around the package set that is expected to ship first.

## Current status

- core language and runtime correctness are the top priority
- non-legacy, non-staging Test262 baseline coverage is currently passing in the working baseline
- deprecated / legacy corners are intentionally not a priority unless explicitly re-approved
- browser-facing and Node-facing integration work is active, but not all related packages are intended for early publication

## Prerelease package set

These are the `src\` packages currently marked `IsPackable=true` and intended as the public package wave.

| Package | Role | Depends on |
| --- | --- | --- |
| `Okojo` | Core engine, runtime, modules, compiler, embedding API | - |
| `Okojo.Hosting` | Schedulers, queues, worker helpers, host infrastructure | `Okojo` |
| `Okojo.Diagnostics` | Formatting, inspection, and bytecode diagnostics helpers | `Okojo` |
| `Okojo.Reflection` | Reflection-based CLR interop extensions | `Okojo` |
| `Okojo.WebPlatform` | `fetch`, timers, workers, and host-installed web APIs | `Okojo`, `Okojo.Hosting` |
| `Okojo.WebAssembly` | Backend-agnostic WebAssembly integration layer | `Okojo` |
| `Okojo.WebAssembly.Wasmtime` | Wasmtime backend for `Okojo.WebAssembly` | `Okojo.WebAssembly`, external `Wasmtime` |
| `Okojo.Annotations` | Attributes for marking Okojo globals and objects for generation/tooling | - |
| `Okojo.SourceGenerator` | Roslyn source generator for Okojo export glue | `Okojo.Annotations` |
| `Okojo.DocGenerator.Annotations` | Attributes for declaration-file generation control | - |
| `Okojo.DocGenerator.Cli` | dotnet tool that emits TypeScript declaration files | `Okojo.Annotations`, `Okojo.DocGenerator.Annotations` |

Not part of the current public package wave:

- `Okojo.Browser`
- `Okojo.Node`
- `Okojo.Node.Cli`
- `Okojo.Repl`
- debug server packages
- compiler experimental projects

Package/versioning/publishing strategy for the packable projects is documented in [docs\OKOJO_PACKABLE_PACKAGE_WORKFLOW.md](docs/OKOJO_PACKABLE_PACKAGE_WORKFLOW.md).

## `src\` project map

`src\` contains both the public package wave and repo-internal projects used for host integration, tooling, experiments, and debugging. Being under `src\` does **not** mean a project is intended for near-term NuGet publication.

| Project | Role | Publication status |
| --- | --- | --- |
| `Okojo` | Core engine, runtime, compiler, modules, embedding API | Public package wave |
| `Okojo.Hosting` | Host queues, scheduling, worker helpers | Public package wave |
| `Okojo.Diagnostics` | Formatting, inspection, disassembly helpers | Public package wave |
| `Okojo.Reflection` | Reflection-backed CLR interop extensions | Public package wave |
| `Okojo.WebPlatform` | Host-installed web APIs such as fetch, timers, workers | Public package wave |
| `Okojo.WebAssembly` | Backend-agnostic WebAssembly integration surface | Public package wave |
| `Okojo.WebAssembly.Wasmtime` | Wasmtime backend for `Okojo.WebAssembly` | Public package wave |
| `Okojo.Browser` | Browser-oriented host/integration surface | Not in current NuGet wave |
| `Okojo.Node` | Node-compatibility host/runtime layer | Not in current NuGet wave |
| `Okojo.Node.Cli` | Dotnet tool for the Node-like CLI host | Packable tool, intentionally not in current public workflow |
| `Okojo.Repl` | Interactive shell and console-facing runtime host | Internal/dev host surface for now |
| `Okojo.DebugServer` | Debug transport/server host | Internal diagnostics infrastructure |
| `Okojo.DebugServer.Core` | Shared debug server core types | Internal diagnostics infrastructure |
| `Okojo.Compiler.Experimental` | Experimental compiler work | Experimental/internal |
| `Okojo.Annotations` | Shared annotations for source generation and tooling | Public package wave |
| `Okojo.SourceGenerator` | Roslyn source generator used by Okojo export patterns | Public package wave |
| `Okojo.DocGenerator.Annotations` | Doc generation annotation types | Public package wave |
| `Okojo.DocGenerator.Cli` | Documentation generator dotnet tool | Public package wave |

## Quick start

```csharp
using Okojo;
using Okojo.Hosting;
using Okojo.WebPlatform;

using var runtime = JsRuntime.CreateBuilder()
    //.UseThreadPoolHosting()
    //.UseFetch()
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

## Requirements

- .NET 10 SDK

## Repository guide

### Main source projects

- `src\Okojo` - core engine
- `src\Okojo.Hosting` - host queues, scheduling, worker helpers
- `src\Okojo.Diagnostics` - formatting and disassembly helpers
- `src\Okojo.Reflection` - reflection-backed CLR interop
- `src\Okojo.WebPlatform` - web/server host APIs
- `src\Okojo.WebAssembly` - WebAssembly integration surface
- `src\Okojo.WebAssembly.Wasmtime` - Wasmtime backend
- `src\Okojo.Browser` / `src\Okojo.Node` - larger host-compatibility layers that are still evolving
- `src\Okojo.Node.Cli` / `src\Okojo.Repl` - executable host and developer entry points

### Supporting areas

- `examples` - public-facing sample apps
- `sandbox` - probes, bring-up apps, and internal experiments
- `tests` - unit and integration tests
- `tools` - Test262 runner, bytecode tools, and other development utilities
- `docs` - design notes, plans, workflow notes, and package/process documentation

If you are looking for the likely first public surface, start with the **Prerelease package set** table above rather than assuming every `src\` project is a package candidate.

## Examples and sandboxes

Public-facing samples live under [`examples`](examples):

- `examples\OkojoModuleSample`
- `examples\OkojoModuleSampleRunner`
- `examples\OkojoGameLoopSandbox`
- `examples\OkojoHostEventLoopSandbox`
- `examples\OkojoArtSandbox`

Internal probes and debugging sandboxes remain under [`sandbox`](sandbox).

## Test262 progress and compatibility tracking

This repo tracks compatibility progress in checked-in artifacts so that work can be prioritized by **passed**, **failed**, and **classified skip** status rather than a single aggregate number.

Important files:

| File | Purpose |
| --- | --- |
| [`TEST262_PROGRESS_INCREMENTAL.md`](TEST262_PROGRESS_INCREMENTAL.md) | Human-readable progress snapshot grouped by category/folder, including passed, failed, and split skip classes |
| `TEST262_PROGRESS_INCREMENTAL.json` | Machine-readable version of the same incremental progress data |
| [`docs\TEST262_SKIP_TAXONOMY.md`](docs/TEST262_SKIP_TAXONOMY.md) | Skip classification policy and grouped skip inventory |
| `tools\Test262Runner` | Runner and progress generation logic |

### How to read `TEST262_PROGRESS_INCREMENTAL.md`

The main columns are:

- **Passed** - tests currently passing
- **Failed** - tests currently failing
- **Skip Std** - baseline ECMAScript coverage intentionally skipped for now
- **Skip Legacy** - deprecated legacy coverage intentionally not prioritized
- **Skip Annex B** - Annex B coverage tracked separately from other legacy behavior
- **Skip Proposal** - proposal/staging work not part of the baseline target
- **Skip Finished** - finished proposals that are still intentionally outside the current carried baseline
- **Skip Other** - intentional exceptions or non-standard buckets that do not fit the above
- **Baseline Passed %** - completion percentage after excluding non-baseline skip classes from the denominator

That last column is usually the best single number to use for practical baseline progress discussions.

## Development

Fast local validation loop:

```powershell
dotnet test tests\Okojo.Tests\Okojo.Tests.csproj
```

Focused Test262 example:

```powershell
dotnet run --project .\tools\Test262Runner\ -c Release --filter test262/test/language --parallel 14
```

## Key docs

- [`OKOJO_BROWSER_COMPATIBILITY_PLAN.md`](OKOJO_BROWSER_COMPATIBILITY_PLAN.md) - top-level compatibility direction
- [`docs\TEST262_SKIP_TAXONOMY.md`](docs/TEST262_SKIP_TAXONOMY.md) - skip taxonomy and inventory
- [`docs\OKOJO_PACKABLE_PACKAGE_WORKFLOW.md`](docs/OKOJO_PACKABLE_PACKAGE_WORKFLOW.md) - packable package versioning and publishing strategy

## Licensing

See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
