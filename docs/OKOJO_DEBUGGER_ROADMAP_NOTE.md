# Okojo Debugger Roadmap Note

## Status

Not complete.

Okojo now has a genuinely usable debugger core, but the full debugger product and VS Code UX are still in progress.

## Implemented

### Runtime / checkpoint layer

- `debugger;` stops
- periodic execution checks
- independent hook flags for:
  - breakpoint
  - debugger statement
  - call
  - return
  - pump
  - generator suspend
  - generator resume
- structured checkpoint source location
- paused script reference for bytecode viewing
- eager current-frame info
- paused snapshots
- paused locals with:
  - names
  - storage kind/index
  - pc-liveness ranges
  - current values
- paused scope-chain lookup for captured locals
- generator suspend/resume checkpoint kinds

### Breakpoints / stepping

- source-path + line breakpoints
- imported-module and nested-function breakpoint support
- breakpoint opcode patching with restore of the original instruction
- bytecode viewer can show the original instruction at a patched stop
- line stepping
- instruction stepping
- line-mode step now:
  - ignores unmapped intermediate ops
  - does not fall back to bogus `line:1` for template-expression internals
  - skips adjacent same-line ops when stepping from a breakpoint

### Debug host / adapter

- `src/Okojo.DebugServer`
- reusable core in `src/Okojo.DebugServer.Core`
- separate integration tests in `tests/Okojo.DebugServer.Tests`
- VS Code extension scaffold in `src/vscode-debug/extension`
- process-backed launch to `Okojo.DebugServer`
- sample workspace under `samples/okojo-debugger-workspace`
- launch-time `stopOnEntry`
- module entry support for multi-file `.mjs` samples
- extensionless / index-style file module resolution for debugger samples

## Not Complete

### VS Code / DAP UX

- adapter control flow still needs hardening and cleanup
- breakpoint verification / relocation UI is still minimal
- variables / scopes / watches are not yet polished enough to call complete
- evaluate / hover / watch-expression UX is still incomplete
- bytecode viewer has improved runtime support, but the VS Code UI is still behind the Lua reference
- no near-production DAP behavior yet

### Debugger feature set

- no exception breakpoint support
- no mature stop-on-entry UX polish
- no step-in-targets / excluded-callers style features
- no full debugger-facing formatting layer for paused values
- no final public debugger API cleanup yet

## Scope Of This Iteration

The main completed slice was:

1. make runtime checkpoints and paused state useful
2. make breakpoint patching and stepping work in practice
3. fix source-position emission so debugger stepping uses real lines

Recent fixes that materially changed debugger quality:

- template AST nodes now keep absolute positions
- `${...}` nested parser positions are emitted directly in absolute coordinates
- function-declaration synthetic completion ops no longer inherit declaration-line positions
- line-mode stepping now targets the next exact source line instead of reacting to arbitrary intermediate checkpoints

## Reference Observations

- Lua-CSharp still remains the main UX reference for debugger shape.
- Its split between runtime/debug host/editor integration is still the right direction for Okojo.
- Okojo can stay synchronous in the VM/debug stop path.
- Okojo does not need to copy Lua-CSharp transport details to copy its debugger usability.

## Intended Copy vs Difference

- Copy:
  - runtime-side breakpoint patching
  - thin editor adapter over a debugger host
  - paused stack / locals / source context as separate debugger concepts
- Intentional:
  - Okojo uses structured execution checkpoints instead of rebuilding pause state from transport concerns
  - Okojo remains sync-only in the VM/debug stop path
  - Okojo can keep a smaller debugger host than the Lua reference if runtime metadata stays strong

## Remaining High-Value Work

### 1. VS Code adapter polish

- remove remaining ad-hoc debugger-control behavior
- tighten stop / resume / step semantics in the adapter
- improve breakpoint feedback and relocation display
- improve bytecode viewer UI

### 2. Debugger value UX

- better formatting for paused local values
- better scope presentation
- evaluate / hover / watch support

### 3. Additional debugger controls

- exception breakpoints
- more mature stop-on-entry behavior
- further stepping coverage across more complex call/module/generator flows

## Minimal Repros To Keep

- `debugger;`
- `function inner() { debugger; } inner();`
- `function outer() { return inner(); } function inner() { debugger; } outer();`
- `function* gen() { yield 1; return 2; } const it = gen(); it.next(); it.next();`
- `console.log(\`entry: ${message}\`);`
- `import "./lib/message.mjs";`

## Tests That Matter

### `tests/Okojo.Tests`

- runtime checkpoint metadata
- locals / scope-chain debugger payloads
- source location on debugger and generator stops

### `tests/Okojo.DebugServer.Tests`

- stop on entry
- debugger statement pause
- source breakpoints
- imported-module breakpoints
- bytecode view at a patched stop
- line stepping
- stepping from a breakpoint on a multi-op line
- template-literal line-step regression

## Perf Plan

- keep the dispatch loop hot path unchanged
- keep breakpoint patching off the hot path
- keep paused-state materialization on slowpaths only
- keep debugger transport shaping outside core VM execution
