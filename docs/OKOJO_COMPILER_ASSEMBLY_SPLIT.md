# Okojo Compiler Assembly Split

## Current Position

`src/Okojo/Compiler` is now treated as a distinct subsystem, but it is not yet a clean standalone assembly boundary.

Immediate dev-speed step completed:

- compiler-focused tests live in `tests/Okojo.Compiler.Tests`
- `src/Okojo` exposes internals to `Okojo.Compiler.Tests`
- `src/Okojo.Compiler.Experimental` exists as a non-shipping friend assembly
- `src/Okojo` exposes internals to `Okojo.Compiler.Experimental`
- compiler iteration can use:
  - `dotnet build tests/Okojo.Compiler.Tests/Okojo.Compiler.Tests.csproj`
  - `dotnet test tests/Okojo.Compiler.Tests/Okojo.Compiler.Tests.csproj --no-build`

This improves the local loop without forcing a risky runtime/compiler split too early.

## Why `Okojo.Compiler` Cannot Be Split Out Cleanly Yet

The current compiler depends directly on runtime and bytecode implementation types from `Okojo`, including:

- `JsRealm`
- `JsScript`
- `JsBytecodeFunction`
- `JsOpCode`
- `RuntimeId`
- `JsValue`
- object/layout/debug metadata types

At the same time, `Okojo` runtime entry points construct and call the compiler directly.

So a naive extraction creates a circular graph:

- `Okojo.Compiler` would need `Okojo`
- `Okojo` would also need `Okojo.Compiler`

`InternalsVisibleTo` helps tests and friend assemblies, but it does not solve that dependency cycle.

## Recommended Split Direction

The safe direction is:

1. keep production compiler code in `Okojo` while multipass structure is being introduced
2. keep compiler tests separate in `Okojo.Compiler.Tests`
3. keep new experimental compiler pipeline in `Okojo.Compiler.Experimental`
4. extract a smaller shared lower layer before any real production assembly split

Target assembly graph:

- `Okojo.Core`
  - AST-facing compiler contracts
  - bytecode model
  - source/debug metadata
  - symbol/binding/storage planning data
- `Okojo.Compiler`
  - parser-to-bytecode compilation
  - multipass binding/capture/storage/register planning
- `Okojo.Runtime`
  - VM
  - objects
  - intrinsics
  - execution/runtime services
- `Okojo`
  - facade package if needed

## Practical Near-Term Rule

Do not split the compiler assembly until the following are no longer runtime-owned:

- bytecode container/model types
- compiler runtime ids/opcode metadata
- script/function debug payload types
- compile-time pool helpers required by compiler

## Incremental Steps

1. Keep growing the new multipass collector/planning types inside `src/Okojo.Compiler.Experimental`.
2. Move compiler-focused regressions into `tests/Okojo.Compiler.Tests`.
3. Separate compiler metadata/data-model types from VM/runtime behavior.
4. Introduce a real shared lower layer only after the dependency graph is one-way.
5. Split `Okojo.Compiler` as an assembly after the lower layer exists.
