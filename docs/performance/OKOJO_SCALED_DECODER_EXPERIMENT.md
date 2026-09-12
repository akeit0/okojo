# Scaled operand decoder experiment (E012, 2026-09-12)

Outcome: rejected; production decoder restored. Two regression cases remain in
`tests/Okojo.Tests/ToolingTests.cs`.

Base revision: `6a76bbcf271a6bd9617823b18948d8fb61b53699`.
Scope: change only `ReadScaledUnsignedOperandSlow` from a by-reference offset
to a by-value offset; move width-based advancement into its inline caller.
The Single branch, opcode ABI and invalid-scale throw behavior remain the same.

Hypothesis: avoiding an addressable offset at the cold call boundary would let
the JIT remove hot offset stack traffic. FullOpts Add assembly confirms that
local effect, but does not establish a throughput win.

## Measurement

Windows x64, i7-13700F, SDK 11.0.100-preview.5.26302.115, .NET 10.0.12.
VmLoopProbe Release; seven fresh-process pairs, alternating AB/BA; 250 warmup
and 50 measured calls per process. ReadyToRun disabled; no affinity pinning.
Each change compares medians of seven process medians; positive is slower.

| Workload | Tiering off | Tiering on, PGO off | Tiering on, PGO on |
|---|---:|---:|---:|
| smi-sum-loop | +1.55% | -4.76% | +18.19% |
| named-get | -1.20% | -6.93% | +0.18% |
| pure-function-call | -0.78% | -0.70% | +5.44% |

Allocations unchanged (0, 112, 200 bytes/call respectively). No rounds discarded;
one PGO-off call pair has a +106.15% outlier. PGO-on sum is variable, but call
regression appears in all seven pairs. These data reject this candidate; they
do not support a general claim about ref versus value arguments.

Run IL remains 8,490 bytes with 50 locals. FullOpts native size grows from
22,281 to 22,586 bytes. A separate PGO capture's final Tier1 body shrinks from
24,080 to 23,940 bytes. Code size and a simpler Add entry do not explain the
whole-loop throughput; the regression's hardware/tiering mechanism is unresolved.

## Correctness and reference checks

Minimal JS reference:

```js
function e012Sum(n) { let s = 0; for (let i = 0; i < n; i++) s += i; return s; }
e012Sum(100);
```

Inspected Okojo bytecode and Node v25.5.0 Ignition output. Both use Add and loop
comparison; V8 has feedback operands and an extra Mov. Okojo intentionally
keeps its existing bytecode conventions. There was no execution mismatch
requiring a VM trace investigation.

New width=2/4 tests execute prefixed Add r300 followed by narrow Add r0, checking
register decoding, PC advance and scale reset. Focused tests pass (2/2).
Candidate full suite: 2,246 passed, 4 skipped, 0 failed. After reverting the
production candidate, the full Release suite has the same result. Builds have
zero warnings/errors. This does not claim a Test262 run or full invalid-bytecode
coverage.

Raw local provenance: VM snapshots `20260912-142830-e012-baseline` and
`20260912-143106-e012-value-offset`; bytecode snapshot
`20260912-142847/e012-before.disasm.txt`. These identifiers are not dependencies
for understanding this outcome.

Next: test a two-argument cold helper receiving an already-addressed operand,
with independent source identity; add multi-operand and wide-workload coverage.
Require PGO-enabled measurements before accepting any decoder change.
