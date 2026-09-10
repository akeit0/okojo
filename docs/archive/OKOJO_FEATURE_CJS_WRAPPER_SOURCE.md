# CommonJS wrapper drops script source (no stack locations)

Status: implemented (2026-09-10).
Scope for this iteration: attach the wrapped source to the CommonJS wrapper
compilation so thrown-error stack frames carry file/line locations like every
other compile path. No change to wrapping, resolution, or module semantics.

## Problem

`NodeRuntime.CompileCommonJsWrapper` builds `new JsFunctionCompiler(realm)`
without `scriptSourceCode`. `CompileFunctionCore` therefore finalizes with
`ToScript(sourceCode: null)`, and `TryGetSourceLocation` fails for every
frame of the module: CLI stack traces render `(pc:N, kind:...)` with no
`@ path:line:col`, and `--enable-source-maps` has nothing to remap. Script,
module, and nested-function paths all attach their source; only the direct
`JsFunctionCompiler` use in `Okojo.Node` drops it. (Nested `JsFunctionCompiler`
uses in `Expressions`/`Statements` thread the outer `scriptSourceCode`
through — verified.)

This is also why `OkojoNode_EnableSourceMaps_Remap_StackTrace_To_Original_Source`
fails: its first assertion (plain `index.js:` location without maps) already
fails on the pristine tree in this environment.

## Fix

Pass `new SourceCode(wrappedSource, resolvedId)` at the wrapper call site.
The wrapper prefix is a single line without a newline, so reported line
numbers already match user source lines; columns shift by the prefix length.
A wrapper→user column remap belongs to the debug-info redesign
(`debug-info-redesign.md`), not this slice.

## Repro

```js
// index.js (CommonJS)
const title = "probe";
function boom() { throw new Error("boom"); }
boom();
```

Before: `at boom (pc:13, kind:FunctionFrame)` with no location.
After (expected): `at boom @ <path>:2:… (pc:13, kind:FunctionFrame)` —
path present, line 2 (`throw` line).

## Planned tests

- In-process `Okojo.Node.Tests` regression: build a `NodeRuntime`, write the
  fixture file, `RunMainModule`, catch the `JsRuntimeException`, assert a
  frame carries the file path and line 2. (Avoid the CLI-subprocess test for
  this — it is environment-sensitive; the existing CLI test covers the
  end-to-end shape once locations flow.)
- Full `Okojo.Tests` + `Okojo.Node.Tests` green (minus the known
  environment failure, which this fix may additionally repair — re-check it).

## Reference observations

- Node: stack traces carry file/line/column; no new behavior invented.
- No V8 internals involved — this restores Okojo's own invariant (every
  compile path attaches source) on the one path that broke it.

## Copy vs intentional difference

None beyond the pre-existing wrapper column shift (documented above).

## Perf plan

None — one small object per module compile; zero hot-path impact.

## Verification (2026-09-10)

- New in-process regression test passes (frame carries path + line 2).
- `Okojo.Node.Tests`: 115 passed, 0 failed, 1 skipped — including the
  previously-failing `OkojoNode_EnableSourceMaps_Remap_StackTrace_To_Original_Source`,
  which this fix repairs (plain `index.js:` locations flow, so the source-map
  remap to `app.ts:` works too). That failure is therefore struck from the
  known-environment-failure list.
- CLI repro confirms `at boom @ <path>:1:117 (pc:13, kind:FunctionFrame)`.

## Deferred

- Wrapper→user column remap: `debug-info-redesign.md`.
- Source-map PC lookup binary search: `debug-info-redesign.md` §D.
