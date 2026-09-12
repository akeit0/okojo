# Debug slices: leading-pc clamp, stale root frame, source-map binary search

Status: implemented (2026-09-10).
Scope for this iteration: two split-independent fixes from
`debug-info-redesign.md` §D plus the pc-0 thread from the CJS wrapper work.
No structural change; both survive the code/instance split untouched.

## Slice 1. Clamp leading pcs to the first sequence point

`JsScriptDebugInfo.TryGetSourceLocation` binary-searches `DebugPcOffsets`
and falls back to the previous entry (`~index - 1`) — except before the
first entry, where it fails. Prologue pcs (e.g. a script root frame at pc 0
before the first statement's sequence point) therefore carry no location,
mirroring nothing: trailing pcs already clamp to the last entry. Clamp
leading pcs to the first entry symmetrically — the standard "function entry
position" attribution. The exact-match variant (`TryGetExactSourceLocation`)
stays exact; the clamped lookup is used only for stack frames (verified:
single consumer via `JsRealm.Vm`).

Repro: the CJS fixture's `at root (pc:0, kind:ScriptFrame)` gains
`@ <path>:1:…`.

Finding (implemented as part of this slice): the dark root frame was not a
lookup gap at all. `CaptureStackTraceSnapshot` appended a synthetic root
frame whenever the walk bottomed out at fp/pc (0, 0) — reading slot 0, which
on an `Invoke`-started run holds a *previous* run's frame object (`Execute`
always restarts at `StackTop` 0 with its own root, and every live bottom
frame breaks out earlier via `callerFp == fpCursor`). It was a resurrected
stale slot with `HasSourceLocation` hardcoded false. The fix removes the
append (stop at the dangling bottom) instead of resolving a ghost: traces
now contain exactly the live frames, all located. Notably the CLI REPL
already filtered `ScriptFrame` frames named `"root"` as noise
(`EnumerateReplFrames`) — the engine now agrees with its own tooling.

Planned tests: extend the CJS stack-location test to assert the root frame
carries a location (line 1).

## Slice 2. Binary-search source-map columns

`SourceMapDocument.TryMapToOriginal` linearly scans a generated line's
entries. Sort each line's index list stably by `GeneratedColumn` once at
construction, then take upper-bound minus one. Semantics preserved exactly
(stable order keeps last-duplicate-wins identical to the linear scan);
large single-line generated files stop paying per-frame linear scans on the
throw path. Reverse indexes for breakpoint mapping stay lazy (unchanged).

Planned tests: mapping correctness over multi-entry lines (first/middle/
last/between-columns/before-first), duplicate columns (last wins), and
unsorted input order (constructor sort normalizes).

## Reference observations

None external — internal lookup semantics only. Spec-adjacent note: source
maps within a generated line are conventionally column-ordered, but nothing
here enforces it, hence the explicit sort.

## Copy vs intentional difference

None.

## Perf plan

Slice 2 is the perf item: one-time per-line sort at document build vs
per-frame linear scan on throws. No microbenchmark theater required — the
complexity change (O(n) → O(log n) per lookup) is the claim; assert
correctness, not wall time, in tests.

## Verification (2026-09-10)

- `Okojo.Tests`: full suite green, incl. 4 new `SourceMapDocumentTests`
  (upper-bound semantics, duplicates, unsorted input) and 3 new
  `JsScriptDebugInfoTests` (leading clamp, exact/between/trailing, no tables).
- `Okojo.Node.Tests`: CJS stack test now asserts exactly 2 live frames, all
  located (wrapper at line 3); CLI repro shows no ghost root line.
- Per-line stable sort at document build; `TryMapLine` (reverse) untouched.

## Deferred

- Everything structural: `debug-info-redesign.md` (location ranges,
  pre-layout sequence points, COW breakpoint views, lazy identity).
- Reverse breakpoint indexes: still lazy, unchanged.
