# Splay: object storage and named-property ICs

This pass starts from `4be9164` on `codex/v7-dispatch-gap`. Splay was 7.5×
slower than V8 jitless. Splay allocates payload trees that live for about 100
iterations, so the engine's object layout determines GC cost.

## Findings

- With the default concurrent workstation GC, native IP samples of Splay put
  46% of the time in coreclr (GC, allocation and write barriers) and 25% in
  ntdll waits. Only 21% was in `JsRealm.Run`. Every ephemeral GC was gen1 and
  promoted about 28 MB.
- Removing bytes alone did not help. A lazy affix-concat string node cut
  allocated bytes by 16% and left the time unchanged, so it was dropped.
  Removing .NET objects per JS object did help.
- `splay_` reads `left`, `right` and `key` across four or five node shapes.
  The monomorphic IC sent most of those reads to the generic lookup.
- Every `new F()` ran a generic `prototype` lookup and started the receiver
  with zero slots. The slot array then grew 0 → 3 → 6.
- Adding locals to `Run` slowed unrelated opcodes by about 35% through JIT
  register allocation. The IC miss logic therefore lives in `NoInlining`
  helpers. `AggressiveOptimization` on `Run` lost PGO and made calls about 27%
  slower, so `Run` keeps tiered compilation.

## Changes

1. **Copy-on-write array literals.** All-constant literals share the compiled
   `JsArrayLiteral` store. `JsArray.Dense` copies the shared store on first
   access, and only audited read paths use `DenseForRead`.
2. **Constructor roots (slack tracking).** Each constructor has its own
   transition root. The root records the largest slot count its descendants
   need, capped at 32. Receivers and growing instances allocate that size at
   once. The common `new F()` reads `prototype` directly.
3. **Polymorphic and transition named-property ICs.** Each site has four
   fallback ways behind the monomorphic entry: own slots, one-level prototype
   data, and cached add transitions. A transition is cached only for an
   ordinary receiver whose prototype chain has at most two ordinary prototypes.
   The fast path still checks the receiver's prototype and extensibility, and
   the shapes of both prototypes.
4. **Inline named slots.** Object literals and constructor receivers with up to
   8 slots are `JsPlainObjectInline{2,4,8}`. Logical slot `s` is inline when
   `s < InlineSlotCapacity`. Otherwise it is stored at `SlotsArray[s]`. Paths
   that replace the whole slot array first move the inline slots into the
   array, so rare paths keep their existing behavior. `{a, b}` drops from
   112 B in two objects to 88 B in one.
5. **Small slow paths.** Bitwise operations on finite double operands, such
   as the `0xffffffff` masks in base.js's `Math.random`, and `ToBoolean` on
   objects avoid the generic conversion.

## Results

Paired A/B with the pinned v7 inputs, 3 or 4 rounds, on the Windows host from
the dispatch-gap note. Times are median ms per iteration.

| Workload | Before | After | Paired ratio |
| --- | ---: | ---: | ---: |
| Splay | 4.75 | 2.92 | 0.62 |
| RayTrace | 40.6 | 29.1 | 0.72 |
| Boyer | 112.8 | 99.2 | 0.86 |
| Earley | 5.64 | 5.15 | 0.91 |
| Richards | 3.10 | 2.98 | 0.96 |
| Encrypt | 4.53 | 4.55 | 0.98 |
| Decrypt | 83.5 | 83.8 | 1.01 |
| RegExp | 71.9 | 73.7 | 1.02 |
| DeltaBlue | 7.05 | 7.23 | 1.02 |
| NavierStokes | 52.8 | 54.7 | 1.04 |

The v7 geometric mean of the paired ratios is about 0.90. The NavierStokes and
DeltaBlue slowdowns are still open, and `Run` codegen variance is the leading
suspect.

Splay with `DOTNET_gcConcurrent=0` runs about 30% faster again, with 3 GCs
instead of 50. That setting belongs to the host, so the comparison runner
does not use it.

## Tests

- `tests/Okojo.Tests/ObjectStorageOptimizationTests.cs`: literal COW
  isolation and holes, inline-slot growth, delete and redefine, learned
  constructor slots, polymorphic loads, transition guards for setters,
  read-only properties and non-extensible receivers, and bitwise ToInt32 on
  doubles. Expected values were checked against Node.
- Full unit suite: 2461 passed. Test262: every non-staging test passes. The
  424 remaining failures are all under `staging/`.

## Follow-up review

Two regressions reproduced against the original storage change. A warmed
add-property transition could add a property to a host subclass of
`JsPlainObject` whose `IsExtensible` override returns false. The cache now
uses its fast extensibility check only for engine-owned object layouts.
An embedding caller's `AsReadOnlySpan()` view could also stay on the
shared template after an in-place write. The public span accessors
provide a view of the array's own storage.

Host code continues to use `JsPlainObject`. The inline subclasses and
their storage remain internal. The integration branch selects the
concrete storage type when accessing inline slots and when moving
them into an array.
An unchecked exact-type trial reduced Splay's timing relative to the
checked build. Richards slowed by about 10.5% relative to the study
baseline in each of its three pairs. The candidate retains checked
concrete-type access. [Merge candidate timings](../../benchmarks/EngineComparison/RESULTS.md#merge-candidate-review)

The initial three cross-engine rounds with an 850 ms warm-up and a 650 ms
sample target measured Opus Splay at 3.04 ms versus 4.38 ms at
`4be9164`, 1.47 ms for Ekuma and 0.63 ms for V8 jitless. NavierStokes and
DeltaBlue were 3.8% and 3.2% slower by paired time. Decrypt was 9.8%
slower across those pairs.

Separate 10-second Decrypt traces measured 82.35 ms at `4be9164` and
82.82 ms on the Opus build. About 91% and 90% of native samples,
respectively, landed in tier-1 OSR `Run`. The bitwise slow helper
accounted for less than 1% in either profile.

`2db7147` lets the dense array-builtin iterator borrow the shared
literal buffer. The validity check compares it with the current store
after callbacks. A callback that writes to the array detaches the
buffer and the iterator resumes through the generic path. Dense
`fill` and `reverse` ask for writable storage before changing values.
[V8 fast map loop](https://github.com/v8/v8/blob/main/src/builtins/array-map.tq)

A probe creates 2,000 ten-element literal arrays and calls `indexOf`
on each. It saves 368,000 bytes per run, exactly one 184-byte buffer
per array. A broader prototype that also changed capacity checks and
several mutators made NavierStokes 6.0% slower than Opus. A separate
owned-buffer field produced conflicting results in Decrypt and slowed
NavierStokes in both screenings. The retained change is confined to
the dense iteration helpers and their two writable callers.

The polymorphic cache now reserves a table of references per linked
function and creates its four entries per active site. The monomorphic
fast path is unchanged. The first measured call executes one named store
and leaves the other sites cold. Compilation and linking are outside the
measurement.

| Cold named-property sites | Original first-call allocation | Per-site allocation |
| --- | ---: | ---: |
| 16 | 4,320 B | 896 B |
| 256 | 57,936 B | 2,672 B |
| 4,096 | 918,096 B | 33,392 B |

Each active site now has its own small array header. A function with
mostly monomorphic or cold sites saves the unused entries. This also
lets each polymorphic probe scan just its site array.

Three untraced Decrypt rounds used a 5,500 ms sample target. The
review baseline measured 91.42 ms, the updated build 82.95 ms,
and the pre-Opus build 83.40 ms. Updated time fell 9.3% relative
to the review baseline by the median paired ratio. The native-code
cause of the timing difference remains unresolved.

The updated Splay build allocated about 996 KB per run over 120
invocations after 40 warm-up runs. One fixed-work sample took 3.26 ms
per run with 118 ms of GC pauses over the timed interval. Reserving
a 384 MiB no-GC region brought the same workload to 1.12 ms per run
with the same allocation volume. The normal-GC comparison uses
each engine's default configuration.
[Cross-engine results](../../benchmarks/EngineComparison/RESULTS.md#v8-v7-object-storage-follow-up)

Raw timings and allocation probes are under
`artifacts/frame-study/` and `artifacts/opus-review/`.
