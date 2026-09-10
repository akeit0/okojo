# Exception handling as a measured experiment

Status: `proposed` experiment (not accepted design).
Input: `OKOJO_EH_SPLIT_DESIGN.md` (this directory) — partly stale, see below.
Concerns `JsRealm.VmLoop.cs` whole-loop `try{while}` handling.

## What is still worth testing

Two separable experiments:

1. A cold exception-handling wrapper around a lean dispatch core.
2. Static JavaScript handler tables replacing some interpreted
   handler-stack operations.

Verified premises worth keeping: catch needs only the faulting `pc`+`fp`
(plus `stopAtCallerFp`); the fault-time accumulator is never read (overwritten
by the thrown value/error object); `fp` mutates only at `ReloadFrame` /
`TryHandleJsRuntimeException`, i.e. rarely vs dispatch.

## What is stale and must be re-baselined

The input note motivates part of its case with a per-dispatch dead
`opcodePc` null-store. Current code hoists that initialization to once per
`Run` (`JsRealm.VmLoop.cs`). Do not cite the old cost; re-measure the
funclet/register-allocation effect on the current build first.

## Constraints

Never replace local-state execution with per-opcode state-object writes.
Evaluate generated code and real workloads (exception-heavy *and*
exception-free) before adopting either variant.

## Acceptance gate

Same-config assembly delta plus `bench-ab` medians on both workload kinds;
no per-dispatch state-object traffic; full conformance green.
