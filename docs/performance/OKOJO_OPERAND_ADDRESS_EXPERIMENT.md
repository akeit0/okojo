# E013: pre-addressed operand decoder (2026-09-12)

Outcome: **rejected; production decoder restored**. Four new cases remain in
`tests/Okojo.Tests/ScaledOperandDecoderTests.cs`.

Base revision: `09c044712f99f9d10cbb049878744efb20175dc6`.
Following [E012](OKOJO_SCALED_DECODER_EXPERIMENT.md), this independent candidate
changed the non-inlined decoder from `(ref byte pc, ref int offset, scale)`
to `(ref byte operand, scale)`. The inline caller computes
`ref Unsafe.Add(ref pc, operandOffset)` and advances the offset by the scale
after a successful helper return. Single-width behavior and bytecode ABI stay
the same. Hypothesis: fewer cold-call arguments might preserve reduced hot
stack traffic without E012's PGO regressions.

## Measurements

Windows x64 10.0.26200, Intel i7-13700F, SDK
11.0.100-preview.5.26302.115, .NET 10.0.12, Release. ReadyToRun=0, no affinity
pinning, GC defaults. Seven fresh A/B process pairs per cell, alternating
AB/BA; 250 warmup and 50 measured calls. Changes compare medians of seven
process medians; positive means slower. PGO-enabled ordinary workloads ran
first. Controls followed to document scope after the call regression failed
the acceptance gate. Total: 168 timing processes.

| Workload | Tiering off | Tiering on, PGO off | Tiering on, PGO on |
|---|---:|---:|---:|
| smi-sum-loop | -1.52% | -3.60% | -5.14% |
| named-get | +1.76% | +4.17% | -1.80% |
| pure-function-call | -3.53% | +0.48% | +5.04% |
| e013-wide-sum | +5.49% | -2.02% | +12.88% |

PGO-enabled calls and wide sum regress in every pair. No outliers discarded;
PGO-off call pairs span -50.87% to +109.92%. These are bounded measurements
on one unpinned hybrid CPU, not population confidence intervals. Allocations
are unchanged: 0/112/200/0 bytes per call respectively.

The cold helper's IL shrinks 55 -> 29 bytes; its inline wrapper grows 30 -> 42.
Run remains 8,490 IL bytes / 50 locals. FullOpts Run native size grows
22,281 -> 22,560 bytes; final Tier1 in a separate PGO capture shrinks
24,077 -> 23,911 bytes. Add loses visible offset stack traffic. Those local
improvements do not establish a whole-engine win; the regression's causal
hardware/tiering mechanism remains unresolved.

## Validation and references

The wide workload declares 300 live locals, sums v299 for 1,000 iterations,
then adds all locals; setup asserts 343850. Okojo emits Add r300 and
TestLessThan r301 with wide operands (302 registers). Node v25.5.0 succeeds
with the same source; Ignition uses Add.Wide/TestLessThan.Wide (303 registers).
V8 feedback operands and register encoding intentionally differ. There is no
proposed bytecode change and no execution mismatch requiring VM trace analysis.

Four new cases check two prefixed store operands followed immediately by a
narrow load, ExtraWide high bits in a safe buffer at nonzero offset, and an
invalid scale throwing without offset advancement. Candidate and final restored
engine full suites: 2,250 passed, 4 skipped, 0 failed. Formatting completed
before validation; builds reported no warnings/errors. No Test262 run claimed.

Raw provenance: VM snapshot `20260912-e013-address`; bytecode snapshot
`20260912-144319/e013-wide.disasm.txt`. These identifiers are not required to
understand the outcome. The wider workload is synthetic and is not a claim
about real-world wide-operand frequency.

Before another decoder rewrite, investigate the shared PGO call regression
seen in E012/E013 with complete handler/native evidence and a separately named
longer-warmup/affinity protocol. Do not infer cache or branch causes from code
size alone.
