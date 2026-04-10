# Okojo.Node Ink Debug Workflow

This note defines the working workflow for driving `sandbox\OkojoInkProbe` forward without losing time in full-app noise.

Use this when working on:

- `sandbox\OkojoInkProbe`
- `sandbox\OkojoNodeDebugSandbox`
- `src\Okojo.Node`
- `src\Okojo`
- `tests\Okojo.Node.Tests`

## Goal

Run the Ink probe on Okojo with disciplined, engine-first debugging:

1. reduce to a focused repro
2. prove the bug with debugger/disasm
3. add a regression test
4. implement the narrow runtime/compiler fix
5. rerun Ink to expose the next blocker

Do not jump straight from a large Ink failure to broad speculative changes.

## Current status snapshot

Completed in this workflow:

- dedicated Node debugger sandbox exists:
  - `sandbox\OkojoNodeDebugSandbox`
- inline template `typeof` compiler drift was reduced and fixed
- wide-register CommonJS wrapper/call bug was reduced and fixed
- Ink now gets past the old `require(undefined)` failure
- tactical `AbortController` / `AbortSignal` support was added to move Ink further
- CommonJS wrapper behavior is now closer to Node for:
  - `exports`
  - `module`
  - `__filename`
  - `__dirname`
  - mutable `require.cache`, including cache-delete reload and built-in shadowing for non-`node:` specifiers
- pnpm/symlinked package identity is now canonicalized more like Node:
  - nested `node_modules` package paths now realpath-canonicalize on the deepest package segment
  - ESM import and CommonJS require now share singleton package instances for symlinked packages such as `react`
- Node globals now bind web-style timers to the embedder-provided queued host loop when available:
  - `setTimeout` / `setInterval` delayed work is now visible to `ManualHostEventLoop` / CLI idle detection
  - `Okojo.Node.Cli` no longer exits after the first Ink frame when the app still has active timer-driven rerenders

Current live Ink blocker after those fixes:

- the previous Yoga callback / Wasm invocation blocker has been reduced and fixed
- the earlier bare-package `react` resolution blocker has been fixed
- the React invalid-hook-call / duplicated-singleton blocker has also been fixed
- the earlier "render first frame then exit immediately" CLI liveness blocker has been fixed
- current result:
  - `dotnet run -c Release --project src\Okojo.Node.Cli\Okojo.Node.Cli.csproj sandbox\OkojoInkProbe\app\main.mjs`
    now stays alive through the Ink timer-driven rerender cycle instead of exiting after the first frame
- next blocker:
  - rerun the larger debugger/probe workflow after this render-success checkpoint to capture the next real mismatch, if any

Latest reduction result:

- the `new Set()` failure was not Ink-specific
- a focused `node-main` repro in `sandbox\OkojoProbeSandbox` reproduced it with:
  - one large CommonJS `var` declaration chain
  - `s = new Set()`
  - `module.exports = typeof Set + "|" + (s instanceof Set) + "|" + s.size`
- threshold finding during reduction:
  - passes around `240` vars
  - fails around `250` vars
- focused regression:
  - `RunMainModule_LargeVarChain_NewSet_RemainsConstructable_When_Register_Goes_Wide`
- current result:
  - focused regression passes again
  - scratch repro prints `function|true|0`

Root cause:

- this was a combined compiler + VM wide-operand bug in large declaration chains
- compiler side:
  - non-`Emit*.cs` lowering still emitted byte-truncated register-slot ops for `Add` / `TestInstanceOf` and related binary operations
  - template string concatenation still used raw byte-sized `Add`
- VM side:
  - arithmetic/comparison handlers still decoded several register-slot operands as single-byte even when prefixed `Wide`
  - confirmed affected runtime decode paths included:
    - `Add` / `Sub` / `Mul` / `Div` / `Mod` / `Exp`
    - ordered comparisons
    - `TestInstanceOf`
    - `TestIn`
    - bitwise / shift register-slot ops

Interpretation:

- this was not a missing `Set` builtin
- it was another core wide-register lowering/decode mismatch under high register pressure

Architectural note:

- `Abort*` and likely broader `Event*` APIs should eventually live in `Okojo.WebPlatform` / shared web-style globals rather than permanently in `Okojo.Node`

## Required debugging loop

For each non-trivial Ink blocker:

1. capture the real failure from `sandbox\OkojoInkProbe`
2. fix disassembly / debugger output first if the current trace is misleading
3. isolate the smallest reproducing JS shape
4. if possible, move it into `tests\Okojo.Node.Tests`
5. inspect Okojo bytecode / disassembly around the failing site
6. inspect debugger locals/registers/stack at the failing site
7. compare with Node behavior for the same JS shape
8. fill missing Web-style APIs when that is the actual blocker
   - `Abort*` / `Event*` are current examples
9. implement the smallest correct fix
10. rerun the focused regression set
11. if the change is broad enough, run full `Okojo.Tests` and `Okojo.Node.Tests`
12. rerun Ink

## Sandboxes and tools

### Main large-app probe

Use:

```powershell
dotnet run --project sandbox\OkojoInkProbe\OkojoInkProbe.csproj -c Release -- --debugger
```

This is the real integration checkpoint. It is not the first place to iterate.

### Focused Node debugger sandbox

Use:

```powershell
dotnet run --project sandbox\OkojoNodeDebugSandbox\OkojoNodeDebugSandbox.csproj -c Release -- --app-root <appRoot> --entry <entry> --stop caught --log <logPath>
```

Supported focused modes include:

- `--inline`
- `--file`
- `--app-root`

Use this sandbox for:

- `debugger;` stops
- caught-exception stops
- breakpoint-style focused stepping
- nearby disassembly
- register/local inspection
- stack inspection

This is the preferred debugging environment once a failure is reduced below full Ink.

### File-based repro sandbox

Use:

```powershell
dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- node-main --file <path> --entry /app/main.js --print-raw
```

Use this when:

- you want a tiny committed or temporary repro without debugger infrastructure
- you need a direct Okojo-vs-Node comparison for one file
- `dotnet <file>.cs` or a simple file-based app is enough and REPL behavior would only add noise

Prefer this over `CSharpRepl` for Okojo investigation work.

## Repro discipline

### Minimum repro first

Always try to shrink the issue before deep implementation work.

Good progression:

1. full Ink failure
2. focused local app in `sandbox\OkojoNodeDebugSandbox\scratch\...`
3. `tests\Okojo.Node.Tests` repro
4. if the issue is actually core/compiler, move to `tests\Okojo.Tests`

Current reduced callback repro:

- `sandbox\OkojoInkProbe\app\probes\yoga-measure-repro.mjs`
- shape:
  - create root + child Yoga nodes
  - install `child.setMeasureFunc(function () { ... })`
  - insert child
  - call `root.calculateLayout(undefined, undefined, Yoga.DIRECTION_LTR)`
- current Okojo result:
  - throws `BindingError: function Node.setGap called with 1 arguments, expected 2 args!`
- significance:
  - removes Ink app noise
  - keeps real Wasm + embind + layout callback behavior
  - strongest current engine-facing repro for the active blocker

### Reduction rules

- remove unrelated modules
- remove unused syntax/features
- keep only the observable failure
- prefer one file if possible
- prefer one assertion if possible
- if a repro can be made smaller, keep reducing

## Where to put repros

### Persistent regressions

Put stable failing/passing regressions in:

- `tests\Okojo.Node.Tests`
- `tests\Okojo.Tests` when the issue is proven core-only

### Temporary reduction artifacts

Put temporary debugging artifacts in:

- `sandbox\OkojoNodeDebugSandbox\scratch\`
- `sandbox\OkojoNodeDebugSandbox\artifacts\`
- `sandbox\OkojoProbeSandbox\scratch\`

Do not turn scratch artifacts into committed baselines unless they become long-lived references.

## Required validation order

### Focused inner loop

Prefer:

```powershell
dotnet test tests\Okojo.Node.Tests\Okojo.Node.Tests.csproj -c Release --filter <Name>
```

For repeated loops, keep the scope narrow.

### After a focused fix

Rerun the nearby focused Node regressions, especially:

- the reduced repro for the current bug
- previously fixed Chalk/template drift regressions
- previously fixed wide-register CommonJS regression
- JSON/CommonJS interop regression if the change touches module/runtime paths

### After a large runtime / compiler / diagnostics change

Run the full suites, sequentially:

```powershell
dotnet test tests\Okojo.Tests\Okojo.Tests.csproj -c Release
dotnet test tests\Okojo.Node.Tests\Okojo.Node.Tests.csproj -c Release
```

Use this when the change touches:

- shared runtime / VM behavior
- compiler bytecode emission
- debugger / diagnostics that affect broad investigation
- Node global/runtime compatibility surfaces

### Integration checkpoint

Then rerun:

```powershell
dotnet run --project sandbox\OkojoInkProbe\OkojoInkProbe.csproj -c Release -- --debugger
```

If Ink moves forward, stop and record the next blocker before making more changes.

## Bytecode/disassembly workflow

When the failure might be compiler/VM related:

1. inspect the failing disassembly window
2. verify object/callee/argument registers
3. verify wide operands are being emitted and decoded correctly
4. compare standalone expression lowering vs inline/template lowering if relevant

Important lesson already confirmed in this track:

- strange wide-register disassembly should not be assumed bogus without checking both emitter and VM decode
- the actual wide-register CommonJS bug was not in call opcode width itself
- it was in byte-truncated register stores during prologue/argument packing
- explicit wide-op operand counts in the decode table must match opcode definitions exactly or the trace drifts into fake instructions
- the next confirmed class of bug was subtler:
  - even after width-aware emitters were introduced, some VM opcode handlers still decoded prefixed register-slot operands as single-byte
  - if emitter and VM decode disagree, symptom shape can drift between:
    - `constructor is not a function`
    - wrong `instanceof`
    - bogus string concatenation
    - invalid `JsString` payload / `undefined`

### Caught-exception stop caveat

For caught exceptions, the paused program counter may point after the faulting operation rather than exactly at the throwing opcode.

That means:

- a highlighted `Return` or nearby post-fault instruction can be misleading
- always cross-check source location, nearby instructions, and operand/local state
- improve debugger presentation when this becomes confusing rather than trusting the first highlighted line blindly

Recent improvement:

- `Okojo.DebugServer.Core` and `sandbox\OkojoInkProbe` now compute a source-guided fallback highlight for caught exceptions
- the raw stop PC should still be kept visible when the highlighted PC is adjusted

## Known workflow lessons

### 1. Full Ink failures often hide a smaller engine bug

The old Ink error:

- `Cannot resolve CommonJS module 'undefined'`

looked like module resolution at first, but reduction proved it was a compiler register-width bug.

### 2. Dedicated debugger infrastructure pays off

`sandbox\OkojoNodeDebugSandbox` was necessary to isolate:

- inline template `typeof` drift
- wide-register CommonJS call issues

### 3. Fix the engine/runtime cause, not the app symptom

Do not add Ink-specific workarounds for compiler/runtime bugs that should be fixed generally.

### 4. After each fix, rerun Ink immediately

Do not stack many speculative fixes before rechecking the real app.

### 5. Escape full-app noise as soon as the source shape is known

When Ink points at a concrete source construct, move immediately to:

1. `sandbox\OkojoProbeSandbox`
2. then `tests\Okojo.Node.Tests`

Do not keep iterating only inside full Ink logs once a direct repro exists.

## Current open work items

1. improve caught-exception debugger presentation so the likely faulting instruction is clearer
2. turn the reduced large-var-chain `new Set()` failure into a permanent `tests\Okojo.Node.Tests` regression
3. decide whether the temporary `Abort*` implementation should move into `Okojo.WebPlatform` immediately or after the next Ink blocker is understood
4. continue the separate `Require_Json_File_Parses_As_CommonJs_Exports` track

## Suggested multi-agent split

When using multiple agents, split work by goal:

### Agent A: current live Ink blocker

- rerun Ink
- inspect current debugger log
- reduce to smallest repro

### Agent B: focused engine/runtime cause

- inspect compiler/runtime code around the reduced repro
- verify bytecode/disasm and VM behavior

### Agent C: regression/test track

- add or refine focused `Okojo.Node.Tests`
- keep the validation set ready

Keep one source of truth for the active blocker and update this note or the session plan when the blocker changes.
