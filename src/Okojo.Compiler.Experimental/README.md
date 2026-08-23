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

- `FlatAst` / `FlatAstLowerer`
  - pooled 16-byte nodes and side tables
  - one arena shared by scripts and nested functions
  - typed nested-function metadata
- `CompilerBindingCollector`
  - flat scope, binding, and identifier-reference discovery
- `CompilerStoragePlanner`
  - dense-ID storage and capture planning without dictionaries
- `JsPlannedCompilerBase`
  - shared allocation, scope, expression, statement, and bytecode emission
- `JsPlannedScriptCompiler`
  - experimental script compiler
- `JsPlannedFunctionCompiler`
  - experimental function-body compiler

The shared emitter is split into partial files to keep growth readable.

## Current Supported Experimental Subset

Both planned compilers currently support:

- parameters and `var` / `let` / `const` declarations without patterns
- blocks, `if`, `while`, `do/while`, ordinary `for`, `break`, and `continue`
- function declarations, expression statements, and function `return`
- null, boolean, number, and string literals
- unary, arithmetic, bitwise, comparison, logical, conditional, sequence, and
  identifier update expressions
- identifier assignment and compound/logical assignment
- nested capture and assignment for parameter, root, function, and block bindings

## Not Supported Yet

Still intentionally unsupported in the experimental pipeline:

- general call expressions
- member/property access
- destructuring
- object/array literals beyond current collector support
- module/global binding emission
- full per-iteration context behavior
- labeled loop control
- direct production replacement of `JsCompiler`

Unsupported paths should fail explicitly, not silently degrade.

## Current Progress

Current milestone status:

- separate experimental assembly: done
- separate compiler-focused test project: done
- flat script/function emitter: active
- shared flat nested-function arena: done for the supported subset
- dense scope/capture planning: done
- ordinary loop lowering: done, excluding per-iteration closure cloning
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
3. parser-direct flat output
4. per-iteration context cloning
5. destructuring and literals
6. measured production migration slices
