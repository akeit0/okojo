# Performance experiments

Index, not storage. Completed attempt history lives in
[../OKOJO_VM_ATTEMPT_LOG.md](../OKOJO_VM_ATTEMPT_LOG.md) (verdict table keeps
rejected attempts recorded so they are not silently retried). The single
active plan is
[../OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md](../OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md).
Per-attempt evidence (notice, patch diff, JIT dumps) lives under
`artifacts/vmloopopt/snapshots/<ts>-<AttemptId>/` per
[../OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md](../OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md);
durable conclusions accumulate in
[../OKOJO_VM_OPTIMIZATION_INSIGHTS.md](../OKOJO_VM_OPTIMIZATION_INSIGHTS.md).

New experiment reports go to the attempt log first; promote only durable,
re-measured findings to the insights file. Method:
[../methodology.md](../methodology.md).
