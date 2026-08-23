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

- parsing-owned `FlatAst` / compiler bridge `FlatAstLowerer`
  - pooled 16-byte nodes plus dense function and parameter side tables
  - one arena shared by scripts and nested functions
  - no compiler-owned objects retained by direct parse results
- `FlatJavaScriptParser`
  - direct `JsLexer` to flat-node generation for the supported subset
  - no class statement/expression nodes and no silent class-parser fallback
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
- blocks, `if`, `while`, `do/while`, ordinary `for`, `switch`, `break`, and `continue`
- function declarations, expression statements, and function `return`
- null, boolean, number, and string literals
- BigInt and RegExp literals
- untagged template literals, including nested substitutions and escape cooking
- synchronous arrow functions with simple/default/rest/pattern parameters,
  expression/block bodies, inferred names, and lexical `this`/`arguments`/`new.target`
- `new.target` in ordinary functions with function-scope early errors
- unshadowed `undefined` intrinsic reads with lexical shadowing
- unary, arithmetic, bitwise, comparison, logical, conditional, sequence, and
  identifier update expressions
- identifier assignment and compound/logical assignment
- ordinary/spread calls and named/computed member loads with property-call receivers
- ordinary and spread construction
- array literals with holes, dynamic elements, and spread
- array binding declarations with elisions, defaults, nesting, and rest
- object binding declarations with static/computed keys, defaults, nesting, and rest
- object literals with named, indexed, computed, shorthand, duplicate, and spread
  data properties plus ordinary concise methods/getters/setters
- named/computed member assignment, compound/logical assignment, and update
- nested capture and assignment for parameter, root, function, and block bindings
- capture-gated per-iteration contexts for lexical ordinary `for` heads

`JsPlannedScriptCompiler.Compile(string)` uses the direct flat parser. The
`Compile(JsProgram)` overload remains as the compatibility bridge for parser and
lowerer comparison.

## Not Supported Yet

Still intentionally unsupported in the experimental pipeline:

- optional calls and private/super member access
- destructuring assignments and pattern parameters
- generator/async and `super` object methods
- tagged template literals
- module/global binding emission
- `for-in` / `for-of` per-iteration behavior
- labeled loop control
- direct production replacement of `JsCompiler`

Unsupported paths should fail explicitly, not silently degrade.

## Current Progress

Current milestone status:

- separate experimental assembly: done
- separate compiler-focused test project: done
- flat script/function emitter: active
- direct lexer-to-flat parser: active for the supported subset
- shared flat nested-function arena: done for the supported subset
- parser-owned flat function/parameter metadata: done
- shared production/flat operator grammar table: done
- shared bytecode call/property operand encoding: done
- ordinary calls and named/computed member loads: done
- ordinary construction: done
- spread calls and construction with source-ordered iterator materialization: done
- array/object literal spread with source-ordered effects: done
- ordinary concise object methods/getters/setters: done
- BigInt and fresh-object RegExp literals: done
- array binding declarations with iterator-safe step/store lowering: done
- object binding declarations with normalized-key rest exclusion: done
- data-property object literals with stable-prefix shapes: done
- prepared-reference member writes and updates: done
- dense scope/capture planning: done
- ordinary loop lowering with captured lexical per-iteration contexts: done
- switch selection, fallthrough, lexical scope, and abrupt break routing: done
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

1. destructuring assignments
2. binding patterns in formal parameters
3. object methods and accessors
4. `for-in` / `for-of`
5. converge the complete production grammar on flat node handles
