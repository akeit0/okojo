# Okojo.Compiler.Experimental

## Purpose

`Okojo.Compiler.Experimental` is the non-shipping workspace for the new multi-pass Okojo compiler pipeline.

It exists to let compiler work move forward with:

- clean separation from production `JsCompiler`
- faster local iteration through `Okojo.Compiler.Tests`
- direct access to Okojo internals through `InternalsVisibleTo`
- freedom to change compiler structure without widening the public API

This project is intentionally experimental.

Production compilation still goes through `src/Okojo/Compiler/JsCompiler`.

## Current Direction

The experimental compiler is building the target flow step by step:

1. collect bindings and scopes
2. collect identifier references
3. plan storage
4. plan capture/context usage
5. emit bytecode from the plan

The goal is to replace the current mixed compiler model with a clearer pipeline:

- discover
- resolve/capture
- classify storage
- allocate
- emit

## Current Pieces

Current project contents:

- `CompilerBindingCollector`
  - scope discovery
  - binding discovery
  - identifier reference collection
- `CompilerStoragePlanner`
  - storage classification
  - first capture-aware planning
- `JsPlannedScriptCompiler`
  - experimental script compiler
- `JsPlannedFunctionCompiler`
  - experimental function-body compiler

Both emitters are split into partial files to keep growth readable.

## Current Supported Experimental Subset

### Script compiler

`JsPlannedScriptCompiler` currently supports:

- top-level `var` / `let` / `const` declarations without patterns
- block statements
- `if`
- function declarations
- expression statements
- literals:
  - `null`
  - booleans
  - small integers
- identifier reads
- binary `+`
- comparisons:
  - `==`
  - `===`
  - `<`
  - `>`
  - `<=`
  - `>=`
- assignments:
  - `=`
  - `+=`
  - `-=`
- nested function capture for:
  - root lexicals
  - function-scope bindings
  - block lexicals via pushed block contexts

### Function compiler

`JsPlannedFunctionCompiler` currently supports:

- parameters
- local declarations without patterns
- block statements
- `if`
- function declarations
- expression statements
- `return`
- literals:
  - `null`
  - booleans
  - small integers
- identifier reads
- binary `+`
- comparisons:
  - `==`
  - `===`
  - `<`
  - `>`
  - `<=`
  - `>=`
- assignments:
  - `=`
  - `+=`
  - `-=`
- nested function capture for:
  - parameters
  - outer/root function lexicals
  - block lexicals
- inherited captured-binding assignment

## Not Supported Yet

Still intentionally unsupported in the experimental pipeline:

- general call expressions
- member/property access
- destructuring
- loops
- object/array literals beyond current collector support
- module/global binding emission
- full per-iteration context behavior
- direct production replacement of `JsCompiler`

Unsupported paths should fail explicitly, not silently degrade.

## Current Progress

Current milestone status:

- separate experimental assembly: done
- separate compiler-focused test project: done
- planned script emitter: started
- planned function emitter: started
- compare/branch lowering: done for current subset
- root/function current-context capture: done
- block-context push/pop capture: done for current subset
- inherited captured-binding store: done for current subset
- production compiler migration: not started

## How To Work On It

Fast loop:

```powershell
dotnet build tests/Okojo.Compiler.Tests/Okojo.Compiler.Tests.csproj /p:UseSharedCompilation=false
dotnet test tests/Okojo.Compiler.Tests/Okojo.Compiler.Tests.csproj --no-build
```

Production sanity check:

```powershell
dotnet build tests/Okojo.Tests/Okojo.Tests.csproj /p:UseSharedCompilation=false
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj --no-build --filter "TryCatchTests"
```

## Rules

- do not patch new planned-compiler behavior into `JsCompiler`
- keep experimental emitter growth split across partial files
- prefer focused execution regressions in `tests/Okojo.Compiler.Tests`
- copy Okojo’s real bytecode/context ABI instead of inventing a parallel one
- keep unsupported features explicit

## Next Suggested Steps

Recommended next slices:

1. call expressions
2. member access
3. destructuring
4. loops and per-iteration context planning
5. richer storage/allocation planning
6. only then consider production migration slices
