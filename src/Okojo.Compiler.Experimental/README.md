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

- simple/default/rest/pattern parameters and `var` / `let` / `const` declarations
- blocks, `if`, `while`, `do/while`, ordinary `for`, `switch`, `break`, and `continue`
- function declarations, expression statements, and function `return`
- null, boolean, number, and string literals
- BigInt and RegExp literals
- untagged template literals, including nested substitutions and escape cooking
- tagged templates with cooked/raw frozen site objects and per-realm identity
- `debugger` statements through the existing VM checkpoint opcode
- synchronous arrow functions with simple/default/rest/pattern parameters,
  expression/block bodies, inferred names, and lexical `this`/`arguments`/`new.target`
- synchronous generator declarations/expressions with `yield`/`yield*`, advanced
  parameters, delegation, and next/return/throw resume modes
- named/computed generator object methods through the same function metadata
- ordinary async function declarations/expressions with `await`, fulfilled and
  rejected resume paths, and the existing async promise driver
- named/computed async object methods through the same function metadata
- async arrows with simple/default/rest/pattern parameters and lexical
  `this`/`arguments`/`new.target`
- async generator declarations/expressions/object methods with `await`, `yield`,
  awaited return values, and sync/async `yield*` delegation
- `for-await-of` over async or wrapped sync iterators with awaited abrupt close
- `new.target` in ordinary functions with function-scope early errors
- unshadowed `undefined` intrinsic reads with lexical shadowing
- unary, arithmetic, bitwise, comparison, logical, conditional, sequence, and
  identifier update expressions
- identifier assignment and compound/logical assignment
- ordinary/spread calls and named/computed member loads with property-call receivers
- optional named/computed member access and direct/member calls, including spread
  and delete short-circuit behavior
- ordinary and spread construction
- array literals with holes, dynamic elements, and spread
- array binding declarations with elisions, defaults, nesting, and rest
- object binding declarations with static/computed keys, defaults, nesting, and rest
- object literals with named, indexed, computed, shorthand, duplicate, and spread
  data properties plus ordinary concise methods/getters/setters
- named/computed member assignment, compound/logical assignment, and update
- nested capture and assignment for parameter, root, function, and block bindings
- capture-gated per-iteration contexts for lexical ordinary `for` heads
- `for-in` enumeration with declaration/pattern heads, identifier assignment,
  loop control, and captured per-iteration lexicals
- synchronous `for-of` iteration with declaration/pattern heads, per-iteration
  capture, and IteratorClose for abrupt completion
- identifier and named/computed member assignment heads for `for-in`/`for-of`
- chained labels and labeled break/continue, including finally replay and crossed
  iterator cleanup

`JsPlannedScriptCompiler.Compile(string)` uses the direct flat parser. The
`Compile(JsProgram)` overload remains as the compatibility bridge for parser and
lowerer comparison.

## Not Supported Yet

Still intentionally unsupported in the experimental pipeline:

- private member access
- instance class fields, static blocks, and private names
- module binding emission
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
- base/derived classes, public methods/accessors, and `super()` construction: done
- named/computed super properties, calls, stores, updates, and nested arrows: done
- named/computed static public fields with source-ordered initializers: done
- anonymous class name inference: done
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

1. instance class fields, static blocks, and private names
2. module parse/binding/link metadata
3. complete declaration early errors and debugger/source metadata
4. planned-compiler Test262 mode and parser differential campaigns
5. corpus benchmarks, explicit production cutover, and bounded old-path removal
