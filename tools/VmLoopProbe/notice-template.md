# VM Loop Optimization Attempt Notice

- Attempt ID:
- Date:
- Commit: (paste from commit.txt)
- Snapshot dir:

## Hypothesis

(What dispatch/VM-loop behavior is being changed, and why it should be faster.
Reference: V8 for language/compiler/VM decisions, Node for built-ins.)

## Change Summary

(Files touched, one line per change. `patch.diff` in this directory holds the
exact diff at capture time.)

## Evidence

| Case | Config | mean ns | median ns | min ns |
| ---- | ------ | ------- | --------- | ------ |
|      | pgo-on |         |           |        |
|      | pgo-off |        |           |        |
|      | tiered-off |     |           |        |

(Paste from results.txt. BenchmarkDotNet numbers, if any, supersede probe
numbers for go/no-go decisions.)

## IL / JIT Observations

(Diffable disasm lives under jit/. Note concrete codegen deltas:
jump table vs compare chain, inlined callees, guarded devirtualization,
OSR/tiering headers, code size delta.)

## PGO On/Off Delta

(What Dynamic PGO changes for Run(): which guards/inlines appear/disappear,
and the measured effect.)

## Copy vs Intentional Difference

(Where this differs from V8/Ignition and why that is acceptable.)

## Decision

(Accept / reject / defer, with reasoning. If deferred, add to TODO.md.)
