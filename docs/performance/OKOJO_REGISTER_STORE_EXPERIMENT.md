# E014: direct assignment in narrow Star (2026-09-12)

Outcome: rejected; `CopyValueTo` restored. Seven new cases remain in
`tests/Okojo.Tests/RegisterStoreTests.cs`.

Base revision: `6ddb2b77e81d12b4cd67677f745fc9fafbd97f70`.
The isolated change was:

```csharp
// Before
JsValue.CopyValueTo(ref Unsafe.Add(ref registerRef, pc), in acc);
// Candidate
Unsafe.Add(ref registerRef, pc) = acc;
```

Only narrow Star changed. StarWide, Mov, lexical stores, JsValue layout and
numeric representation stayed unchanged. The destination is an element of
the realm's managed JsValue[] Stack; the source is the local accumulator.

## Evidence and result

CopyValueTo has a null-object path that writes U and zeroes the object slot
with scalar writes. Its non-null object path already uses struct assignment.
Captured FullOpts and PGO Tier1 code show direct Star calling
`CORINFO_HELP_ASSIGN_BYREF` unconditionally after `movsq`. Baseline numeric
stores avoid that helper. This is a visible code-generation difference, not
proof that every helper call marks a GC card or that it alone explains the
entire performance change.

Run IL: 8,490 -> 8,489 bytes, still 50 locals. FullOpts native size:
22,281 -> 22,220 bytes. Separate final PGO Tier1 capture: 24,077 -> 24,033 bytes.
Smaller code does not imply a cheaper hot store.

PGO-enabled measurements on Windows x64 10.0.26200, i7-13700F, SDK
11.0.100-preview.5.26302.115, .NET 10.0.12, Release, ReadyToRun=0, GC defaults,
no affinity pinning. Seven fresh A/B pairs per workload, alternating AB/BA;
250 warmup and 50 measured calls. Each change compares the medians of seven
process medians; positive is slower. Total: 56 timing processes.

| Workload | Candidate change | Allocation per call (unchanged) |
|---|---:|---:|
| smi-sum-loop | +13.37% | 0 B |
| named-get | +18.11% | 112 B |
| pure-function-call | +4.27% | 200 B |
| referenceShuffle | +2.27% | 192 B |

All call pairs regress. Other paired ranges include noise/outliers; none were
discarded. Reference shuffle creates two objects, swaps them 5,000 times and
checks identity during setup. It also contains numeric induction and Mov;
this is not isolated object-store latency. PGO gate failure stopped further
timing configurations. FullOpts assembly captures are not throughput results.

## Validation and references

Focused checks: 7/7 pass, covering negative zero, NaN, fractional values,
object-to-number overwrite, number-to-object overwrite, shared identity and
symbol/string values. Candidate and restored-engine full suites: 2,257 passed,
4 skipped, 0 failed. Formatting preceded validation; builds had no warnings
or errors. No Test262 or forced-GC stress run claimed.

Okojo's referenceShuffle bytecode has six registers: the loop contains Mov,
two reference Star instructions and one numeric induction Star. Node v25.5.0
runs the same identity guard successfully; Ignition uses its own Star/Mov
encoding and feedback operands. Okojo intentionally retains its ABI. No
execution mismatch occurred requiring VM trace investigation.

Raw provenance: VM snapshot `20260912-e014-star`; bytecode snapshot
`20260912-145115/e014-reference.disasm.txt`. These identifiers are not required
to understand the conclusion. Retain CopyValueTo here; local copies and other
destination categories need independent evidence.
