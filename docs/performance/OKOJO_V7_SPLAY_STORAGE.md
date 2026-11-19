# V8 v7 Splay allocation and storage

The baseline is Okojo `ec06f6b` (the interpreter in `b48c623`), Ekuma
`41a55ec` with Go 1.27.4, and Node 26.12.1 with `--jitless`. All timings
use the pinned v7 JavaScript on Windows 11, an Intel Core i7-13700F, and
.NET 10.0.14. Setup is outside the fixed-work sample.
[Harness](../../benchmarks/EngineComparison/README.md)

## Where Splay spends time

Okojo fetched 17.07 million opcodes during setup and five Splay runs.
Ekuma fetched 18.60 million. Each steady invocation creates 5,040
two-property payload objects and 2,560 ten-element arrays. Okojo
allocates a managed object and a separate buffer for each of them.
Allocation ticks attributed about 44% of sampled bytes to
`JsValue[]`, 29% to strings, and 26% to plain object and array
headers. The trace includes setup, which creates the same payloads
in a larger batch.
[Fixed-work opcode counts](OKOJO_V7_DISPATCH_GAP.md#fixed-work-opcode-profile)

Over 120 timed runs after 40 warm-up runs, Okojo allocated about
1.88 MB per run. Ekuma's process allocation counter reported 2.31 MB
and about 21,500 allocations per run. Its two-property object slots
are embedded in the object allocation, while its ten-element arrays
exceed the eight-element inline capacity and use a separate buffer.
Go records more bytes even though the workload finishes faster.
[Ekuma allocation opcodes](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/internal/engine/interp.go#L1600)
[Ekuma storage size classes](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/internal/engine/object.go#L795)

One fixed-work Okojo run spent 226 ms in GC pauses out of 637 ms
elapsed. With a 384 MiB no-GC region reserved before timing, the
same allocation volume took 268 ms. Ekuma took 194 ms in its default
run and 231 ms with `GOGC=off`. The runs with suppressed collections
diagnose collector sensitivity. They also change the heap's growth
and allocation behavior. The warmed comparisons use default
collection on both runtimes.

Ignition's `GeneratePayloadTree` bytecode creates the leaf object and
its array together using an object boilerplate. Okojo emits
`CreateObjectLiteral`, `CreateArrayLiteral`, and
`InitializeNamedProperty`. V8 can share the literal elements
template and detach it on a write. Both engines issue two additions
for the leaf string.
[V8 array write detachment](https://github.com/v8/v8/blob/main/src/builtins/array.tq)

The native Ekuma `OpNewObject` arm calls `newObjectWithStorage` and
checks the Go GC write barrier when publishing the shape. Okojo's
unshared literal path calls both the array constructor and the dense
buffer initializer, then copies through .NET's bulk write barrier.
In the copy-on-write build, the literal helper issues one
`CORINFO_HELP_NEWSFAST` for the wrapper and assigns the existing
buffer reference. Its FullOpts listing shrinks from 198 to 127 bytes.
The native `Run` listing for the runtime-call string version matches
the baseline after normalizing compiler-generated method names.

## Storage experiments

Copy-on-write arrays share the compiled primitive buffer. Each
evaluation still has a distinct array object. Reads use the original
dense buffer path, and indexed writes, bulk mutations and mutable
span access detach before modifying the elements. Both tested
versions saved 471,040 bytes per Splay invocation, exactly 2,560
ten-element buffers. In four rotating screening rounds, the ownership
flag reduced Splay time by 17.8% and raised NavierStokes time by
12.1%. Using writable capacity for the indexed-store guard put
Splay at 3.96 ms and NavierStokes at 54.87 ms versus 4.85 ms and
53.47 ms for the baseline. Richards rose from 3.04 to 3.18 ms in
that round. The FullOpts indexed setter grew from 378 bytes to
426 bytes with the flag and 433 bytes with the capacity layout.
The flag tests ownership after finding the occupied dense slot.
The capacity layout moves detachment into the bounds-check miss path.

Using ropes above 24 UTF-16 code units simplified the concatenation
policy and saved 333 KB per Splay invocation. The first three
screening pairs were inconsistent for Splay and gave a 15.7% median
slowdown for RayTrace. Short-lived strings that are read or flattened
interact differently with this representation than Splay's retained
payloads.

A dedicated instruction for `"prefix" + value + "suffix"` saved
232 KB per Splay run by allocating the finished short string
directly. It improved Splay time by 15.4% while NavierStokes took
6.8% longer across three pairs. The increase motivated trying the
existing runtime-call instruction.

## Retained change

The compiler emits one runtime call for the two additions. It loads
the literal strings into consecutive argument registers and leaves
the middle value in the accumulator. The runtime converts the middle
value once with the default hint, then creates the final short string
in one allocation. Longer or rope-backed values use the existing
concatenation path. Node and Okojo agree on the regression case
covering BigInt, the default hint, Symbol rejection and a rope input.

This version saved 231,797 bytes per Splay run, about 12% of
baseline allocations. Splay's paired time was 6.1% lower, while
NavierStokes was within 0.2%. Richards took 2.2% longer across the
four pairs. Its changed string expressions are in the failure
message and a diagnostic `toString` method. The scheduler loop
executes the same bytecode in both builds. Six further pairs with
tiered compilation disabled put Richards 0.7% slower at the median,
with individual changes between −0.9% and +2.4%.
[Full engine comparison](../../benchmarks/EngineComparison/RESULTS.md#v8-v7-splay-allocation-pass)

The raw timings are in
`artifacts/frame-study/v7-affix-runtime-v7-update-bits.json`.
The allocation and native listings and the prototype patches are
under `artifacts/splay-storage-study/`.
