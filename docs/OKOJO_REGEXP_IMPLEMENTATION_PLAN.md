# Okojo RegExp Implementation Plan

## Purpose

This document is the detailed implementation plan for the experimental RegExp engine work.

Keep `docs\OKOJO_REGEXP_EXPERIMENTAL_PLAN.md` focused on project scope, rollout, validation surfaces, and repo wiring.

Use this document for:

1. algorithm direction
2. internal engine architecture
3. code-generation strategy
4. table/data layout strategy
5. optimization order
6. benchmark policy

## Design Goal

Build the best managed ECMAScript-oriented RegExp engine Okojo can reasonably carry, not merely a lightly tuned clone of the current implementation.

Priority order:

1. correctness against `test262` and JS-observable semantics
2. predictable low allocation on hot paths
3. hot-path simplicity and branch reduction
4. compile/runtime structure that remains maintainable even if large generated tables are required

Non-goals for the near term:

1. native machine-code JIT
2. preserving every current internal shape if a better VM/codegen layout exists
3. optimizing intentionally unsupported legacy behavior first

## Current Assessment

The current engine is a good correctness baseline, but it is still too interpreter- and tree-walk-oriented for the long-term fast path.

Main current costs likely include:

1. recursive matcher/tree dispatch instead of compact linear instructions
2. repeated branching through general node kinds on common scans
3. capture-state cloning pressure around alternatives, assertions, and quantifiers
4. expensive Unicode/class/property checks in paths that should be predecoded
5. compile output that is closer to parsed structure than to execution structure

That means the experimental project should not be constrained to "same algorithm, cleaner code". A deeper redesign is justified.

## Recommended Target Architecture

### Pipeline

Recommended pipeline:

1. parse pattern into rich AST
2. normalize AST into an execution-oriented IR
3. analyze the IR for fast-path opportunities and resource bounds
4. emit a compact regex bytecode program
5. execute through a dedicated VM with explicit backtracking storage

The important change is the middle: do not execute the parser tree directly in the final design.

### Internal Layers

Recommended internal layers in `src\Okojo.RegExp.Experimental`:

1. `Syntax`
   - parser-facing AST
   - retains precise ECMAScript structure and diagnostics
2. `Analysis`
   - capture planning
   - width analysis
   - prefix/first-set analysis
   - anchored/sticky analysis
   - backtracking-risk analysis
3. `IR`
   - execution-oriented graph or linear block form
   - explicit branch, repeat, save/restore, char-test, and assertion operations
4. `Codegen`
   - bytecode emission
   - peephole fusion
   - constant/table packing
5. `VM`
   - explicit backtracking stack
   - capture register storage
   - specialized dispatch
6. `Data`
   - generated Unicode/property/class tables
   - compact spans for static data

This separation matters because parser correctness and runtime speed want different shapes.

## Execution Model Recommendation

### Use a bytecode VM, not AST walking

Recommended bytecode properties:

1. linear instruction stream
2. small opcode enum
3. compact operands
4. explicit control-flow instructions
5. no recursion on the managed stack for matching

Why:

1. easier to add peephole fusion
2. easier to profile and instrument
3. easier to centralize hot dispatch
4. easier to add specialized fast loops
5. easier to keep uncommon paths out of the common dispatch stream

### Explicit backtracking stack

The VM should maintain an explicit backtracking stack storing only what is needed to resume:

1. program counter
2. input position
3. capture checkpoint marker
4. optional loop bookkeeping

Do not use deep managed recursion as the long-term core algorithm.

### Capture storage model

Recommended capture storage:

1. flat register arrays for start/end pairs
2. one "current working state"
3. checkpoint markers for restoration
4. pooled storage per exec

Avoid full-state cloning when a checkpoint/rewind model is enough.

Preferred approach:

1. `int[]` or pooled compact backing storage for capture registers
2. explicit stack of capture-write deltas or checkpoint offsets
3. restore by rewinding to a checkpoint instead of copying entire arrays

This is one of the highest-probability wins versus the current engine.

## Recommended Bytecode Shape

The exact opcode set can evolve, but the initial target should look roughly like this:

1. `Match`
2. `Fail`
3. `Jump target`
4. `Split targetA,targetB`
5. `SaveStart captureIndex`
6. `SaveEnd captureIndex`
7. `Char codePoint`
8. `CharEqIgnoreCase codePoint`
9. `CharClass tableIndex`
10. `Property tableIndex`
11. `Any`
12. `AssertStart`
13. `AssertEnd`
14. `AssertWordBoundary`
15. `Advance`
16. `LoopCharNot` / `LoopCharClass` fast opcodes
17. `LookaroundEnter` / `LookaroundLeave` or equivalent compiled form
18. `BackReference index`

Do not aim for a huge opcode set immediately. Start with:

1. a minimal correct VM
2. a few very high-value fused ops

## Fast-Path Strategy

### 1. Search plan before full VM entry

For non-sticky, non-anchored regexes, do not always enter the full matcher at every input position.

Precompute a search plan:

1. exact first code point if available
2. small first-set bitmap/range table if available
3. fixed literal prefix if available
4. minimum match length

Execution policy:

1. fail early if remaining length is below minimum
2. use literal/prefix scanning to jump candidate positions
3. enter the VM only at plausible start positions

This is the first optimization to prioritize because it cuts the entire engine workload.

### 2. Fused scan opcodes

Patterns like these should not dispatch once per AST node per character:

1. `[^_] *`
2. `.*`
3. `[A-Za-z0-9_]+`
4. literal runs like `hello`

Recommended fused operations:

1. scan until char mismatch
2. scan through ASCII word ranges
3. scan through predecoded class bitmap
4. compare literal run in one loop

This follows the same general lesson as V8 peephole optimization: remove dispatches from common loops.

### 3. ASCII-first specialization

A large amount of JavaScript regex traffic is ASCII-heavy even when Unicode support is required semantically.

Recommended policy:

1. detect ASCII-only literal/class paths at compile time
2. generate ASCII-specialized opcodes where legal
3. branch to the Unicode-general path only when needed

This is especially useful for:

1. identifier-like patterns
2. web routing patterns
3. simple tokenization patterns
4. many `test262` hot loops

### 4. Literal-run optimization

If a sequence of literal code points has no reason to stay broken up, emit one instruction for the run.

Benefits:

1. fewer dispatches
2. tighter loops
3. easier vectorization opportunities later

Even without SIMD, a single literal-run opcode is better than N single-char opcodes.

## Parser and Analysis Improvements

### Normalize before codegen

Normalization passes should include:

1. flatten nested sequences and alternations
2. collapse empty nodes
3. canonicalize trivial quantifier forms
4. rewrite single-literal classes into literal checks when semantics allow
5. isolate constructs that force slow paths

### Width analysis

Compute:

1. minimum width
2. maximum width where finite
3. zero-width status
4. whether a node can consume at all

Uses:

1. start-position pruning
2. infinite-loop protection for zero-width global progress
3. lookbehind planning
4. better quantifier codegen

### Prefix and first-set analysis

Compute:

1. exact literal prefix when present
2. possible first code points when finite enough
3. whether the pattern is anchored
4. whether sticky mode removes the need for search

This data should be stored in compiled metadata and not recomputed on each exec.

### Quantifier classification

Quantifiers should be classified into buckets:

1. simple linear repetition without captures/assertions
2. bounded repetition
3. repetition with nested backtracking
4. repetition that forces slow path

Only category 1 should go through the hottest specialized loop path.

## Backtracking Control

Okojo must preserve ECMAScript semantics, so full DFA conversion is not the right global strategy.

But backtracking can still be controlled better.

Recommended controls:

1. explicit backtracking frames only at real branch points
2. checkpoint rewinds instead of whole-state copies
3. compile-time simplification of deterministic subgraphs
4. simple-loop specialization that avoids unnecessary branch pushes
5. internal telemetry for branch push/pop counts in debug builds

Do not try to "fix" catastrophic backtracking by silently changing semantics. Keep semantics, but reduce engine overhead on normal paths.

## Unicode and Table Strategy

### Generated data

Unicode/property/script/category tables should be generator-owned once the new engine stabilizes.

Recommended generated outputs:

1. `ReadOnlySpan<int>` range endpoints
2. compact encoded category/script/property payloads
3. ASCII fast maps
4. optional trie-like structures only if range tables prove insufficient

Follow these rules:

1. prefer `ReadOnlySpan<T> Data => [ ... ]`
2. avoid tuple-heavy static structures for large primitive tables
3. use `MemoryMarshal` for reinterpretation without copying when needed

### Two-tier Unicode checks

Recommended property/class matching strategy:

1. ASCII fast path
2. compact range lookup for general Unicode

Avoid expensive object-heavy dictionaries on the hot path.

Prefer:

1. sorted range tables
2. compressed masks
3. binary search or segmented lookup

### Character class compilation

Character classes should be precompiled into one of a few forms:

1. single literal
2. small ASCII bitmap
3. ASCII range list
4. Unicode range table
5. property matcher handle
6. class-set expression evaluator handle for `v` mode

Do not leave character-class interpretation as a generic tree walk during exec.

## Lookaround and Backreference Strategy

These are correctness-critical and can destroy hot-path simplicity if not isolated.

Recommended strategy:

1. keep them correct first
2. isolate them into explicit slow-path opcodes or helper execution branches
3. do not let their complexity infect common literal/class scanning paths

Specific advice:

1. lookbehind should compile with known-width help when possible
2. backreferences should avoid repeated string slicing where possible
3. named backreferences should resolve to numeric indices before execution

## Code Generator Strategy

### Prefer generation over hand-maintained giant tables

Good generator candidates:

1. Unicode property/category/script data
2. prepacked character-class tables
3. opcode metadata tables
4. ASCII classification maps

Potential generator layout:

1. tool under `tools\` or a source-generation helper project
2. checked-in generated `.cs` output
3. deterministic, reproducible generation

### Code generation phases

Recommended codegen phases:

1. IR block creation
2. label resolution
3. opcode selection
4. peephole fusion
5. operand packing
6. metadata emission

Do not mix parser concerns directly into final bytecode emission if it can be avoided.

## Optimization Order

Recommended order for actual implementation work:

### Phase A - Instrument and reshape

1. add internal counters/tracing for backtrack pushes, rewinds, VM steps, and search skips
2. separate AST/analysis/codegen/VM layers
3. get a bytecode VM running with correctness parity on focused tests

### Phase B - Search-path wins

1. minimum-length pruning
2. anchored/sticky special handling
3. first-character filtering
4. fixed-prefix scan

### Phase C - Dispatch wins

1. literal-run opcode
2. ASCII class scan opcode
3. negated-class scan opcode
4. peephole fusion for common loops

### Phase D - State-management wins

1. replace broad capture-state cloning with checkpoint rewinds
2. reduce temporary allocations in match materialization
3. preallocate or pool execution buffers

### Phase E - Unicode/class wins

1. generated tables
2. faster class/property lookup
3. `v`-mode class-set specialization where practical

### Phase F - JS-observable path cleanup

1. reduce runtime wrapper overhead around `RegExp.prototype.exec`
2. ensure result materialization stays lazy or minimal where allowed
3. keep lastIndex/global/sticky semantics exact

## Benchmark Policy

Benchmarking is required, but it should not dominate the inner loop.

Recommended cadence:

1. frequent loop:
   - focused unit tests
   - focused `test262` runs
2. medium cadence:
   - benchmark discovery or a single benchmark class after a meaningful engine batch
3. milestone cadence:
   - full side-by-side benchmark run after large algorithm/data-layout changes once tests are green

Practical guidance:

1. do not rerun the benchmark suite after every small edit
2. do run it after:
   - bytecode layout changes
   - search-path changes
   - capture/backtracking storage changes
   - Unicode table redesign

## Validation Strategy

Required validation stack:

1. focused direct-engine tests
2. JS-observable Okojo tests
3. `test262` under `--regexp-engine experimental --filter test/built-ins/RegExp`
4. benchmark comparison after meaningful batches

Recommended working loop:

```powershell
dnrelay test tests\Okojo.Tests\Okojo.Tests.csproj -- --filter RegExp
```

```powershell
dnrelay run --project tools\Test262Runner\Test262Runner.csproj -- --regexp-engine experimental --filter test/built-ins/RegExp --skip-passed
```

Milestone benchmark:

```powershell
dnrelay bench --project benchmarks\Okojo.Benchmarks\Okojo.Benchmarks.csproj --select RegExpEngineBenchmarks
```

## Immediate Next Steps

Recommended next implementation slice:

1. introduce an explicit execution IR and bytecode container in `Okojo.RegExp.Experimental`
2. keep the current parser as the temporary front-end if that speeds bring-up
3. replace direct AST execution with a small VM for a correctness-preserving subset
4. land first-character, prefix, and minimum-length search planning immediately after the VM is stable

If a large rewrite is needed, prefer the better design. The experimental project now exists specifically so the engine can evolve aggressively without preserving every current internal decision.
