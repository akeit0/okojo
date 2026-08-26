# dromaeo-object-regexp performance note (split fast paths)

Scope: close the Okojo-vs-Jint gap on `dromaeo-object-regexp-modern`
(Okojo_Execute 2036us/op -> 354us/op quick-run, allocations 1.65GB -> 351MB).
Jint delegates regex to compiled BCL `System.Text.RegularExpressions`; Okojo
uses its spec-compatible backtracking VM, so parity is not the goal. The
workload's real cost turned out to be per-position JS glue around the VM, not
the VM itself.

## Findings (tools/RegExpProbe)

- Pure scanning (`match`/`test` over non-matching 65KB inputs) is within
  1.2x-3x of BCL compiled regex; no VM work needed for this workload.
- The dominant cost was `RegExp.prototype[Symbol.split]`: one full
  observable match object per input position (~2M constructions), plus
  property-path `lastIndex` writes/readbacks per attempt.
- Evented dotnet-trace inclusive percentages overstated leaf methods with
  short calls; reliable attribution came from tools/RegExpProbe shape
  timings (per-test-family mini-scripts) plus targeted traces.

## Changes

1. `JsRegExpRuntime.IntrinsicExecStepAt`: raw sticky-at-index step with no
   lastIndex traffic (mirrors existing R8-regexp stepping design).
2. `[Symbol.split]` fast path gated on plain `JsRegExpObject` receiver whose
   `exec` still resolves to this realm's builtin (`Intrinsics.IsDefaultRegExpExec`,
   new `_regExpPrototypeExec` field). Species-constructor splitter creation
   stays fully generic. Segment/capture/limit semantics mirror the generic loop.
3. Trivially-empty pattern sources (empty or `(?:)`) skip the engine entirely;
   segments are individual code points (surrogate-aware advance preserved).
4. `String.prototype.split`'s own regex branch (no-Symbol.split exotics) uses
   raw steps too; its empty-pattern char loop uses `JsValue.FromLatin1Char`.
5. `JsValue.FromLatin1Char`: single-character string cache (Latin-1 range,
   mirrors V8's single_character_string table; benign first-use race).
6. `JsRegExpRuntime.Test` checks match existence via raw step instead of
   building a result array.

## Copy vs intentional difference vs references

- Fast-step split copies V8/R8-regexp's "no observable side effects between
  attempts" reasoning; the generic loop remains as fallback for overridden
  `exec`, so ECMA-262 RegExpExec observability is intentionally preserved at
  the gate rather than inside the loop.
- Single-char string cache copies V8 behavior; Node/V8 share immutable
  single-char strings across contexts.

## Verification

- Okojo.Tests: 2150 passed / 0 failed.
- Test262 filters: built-ins/String/prototype/split, .../replace, .../match,
  built-ins/RegExp, RegExp/prototype/Symbol.split all 0 failed. Full-suite
  failures unchanged and confined to staging/* (intentionally unsupported).
- Probe: SplitEmpty 1683ms -> 203ms, SplitChar 384ms -> 117ms,
  full js scenario 2150ms -> 373ms per iteration.

## Deferred

See TODO.md regexp follow-ups (lead-literal scan-ahead, general empty-match
recognition, Exec allocation trimming).
