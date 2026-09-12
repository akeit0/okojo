# E016: per-invocation RegExp replacement arguments (2026-09-12)

Outcome: retained as a scoped allocation optimization. Base revision:
`b485878b584e2b04a186c6e3573ccc1a6e00bc5c`.

The functional replacement path previously allocated a JsValue[] for every
match. It now reuses a local buffer when matching is global, the current match
comes from intrinsic RegExp exec, and the callback is a normal bytecode
function. A missing or differently sized buffer is allocated lazily. Custom
exec, non-global replacement and other callback kinds retain the old path.

## Correctness and ownership

InvokeBytecodeFunction copies input arguments into its own VM frame before
execution; JsArgumentsObject copies frame arguments into its own array.
Mapped parameters reference the callback context. The buffer belongs to one
host replacement invocation, so nested replacements cannot overwrite it.
Every entry is assigned before calling the callback, including Undefined for
unmatched optional captures. Named-groups objects are still created per match.
No shared pool or callback-order change is introduced.

Five new cases in `tests/Okojo.Tests/RegExpReplacementBufferTests.cs` cover
saved mapped arguments, strict arguments and optional named groups, nested
replacement, changing custom-exec capture counts, and recovery from callback
exceptions. All five also pass in Node v25.5.0. Full Okojo.Tests Release suite:
2,262 passed, 4 skipped, 0 failed. Formatting preceded validation; builds had
zero warnings/errors. No Test262 or exhaustive callback-kind coverage claimed.

Minimal workload shape:

```js
const input = 'ab '.repeat(64);
input.replace(/a(b)/g, function(match, capture, offset) { return capture; });
```

Okojo bytecode was inspected and the exact benchmark sources executed with
result guards in Okojo and Node. Node is the built-in reference; no intentional
semantic difference or execution mismatch occurred, so no mismatch-specific
VM trace was required.

## Measured results and tradeoffs

SDK 11.0.100-preview.5.26302.115, .NET 10.0.12, Windows x64 10.0.26200,
i7-13700F, Release, ReadyToRun=0, GC defaults, no affinity pinning.

| 64-match case | Baseline B/call | Candidate B/call | Saved |
|---|---:|---:|---:|
| Ordinary capture | 19,488 | 13,944 | 5,544 (28.45%) |
| Optional named capture | 45,056 | 38,504 | 6,552 (14.54%) |

The savings equal 63 avoided arrays: 88 B each for four arguments and 104 B
for five arguments on this runtime. Single-match and custom-exec controls
retain their allocation counts. This measures allocated bytes, not retained
heap or peak memory.

Four protocols, 224 fresh timing processes, seven A/B pairs per case/protocol,
alternating AB/BA. Medians of process medians are compared; no outliers removed
and no protocols pooled. The initial 250-warmup/50-call PGO run looked faster;
the 1,000-warmup/200-call run did not confirm it. This motivated explicit
10,000-warmup/2,000-call runs:

- Ordinary captures: 16,600 -> 16,400 ns/call (1.20% faster).
- Named captures: 25,900 -> 25,200 ns/call (2.70% faster).
- Custom exec: 30,600 -> 30,500 ns/call (effectively flat).
- Non-global single match: 5,200 -> 5,300 ns/call (100 ns / 1.92% slower).

The allocation benefit and modest eligible-path gains justify retaining the
change with the stated small single-match cost. This is not a universal speedup
or a population confidence interval. Tiering-off controls improve eligible
cases by 3.52% and 1.87%; non-global and custom controls are near flat.

## IL/JIT and measurement lesson

The replacement host lambda grows from 1,850 to 1,937 IL bytes, 68 to 72
locals, and 6,889 to 6,971 FullOpts native bytes. Its allocation helper is now
behind eligibility/buffer-length checks. More static code produces fewer
executed allocations.

In a separate long capture, JsRealm.Run reaches Tier1 in both builds while
the host replacement method remains Tier0-FullOpts. PGO=1 is not proof that
all code is Tier1. Preserve all short-run results; do not rewrite earlier
experiments or infer that they would reverse under this protocol.

Raw provenance: `20260912-e016-reuse`; bytecode snapshot
`20260912-150858/e016-many.disasm.txt`. These identifiers are not required to
understand the decision. Larger application-level gains remain unmeasured.
