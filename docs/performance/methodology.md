# Performance methodology

Status: methodology synthesis (base `c651320`). Detail documents (moved,
preserved):

- `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` — workflow, tooling, snapshot layout.
- `OKOJO_VM_DEEP_INSPECTION_METHOD.md` — four-layer investigation model.
- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` — durable findings (append-only).
- `OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md` — the single active plan (C/V/A).
- `OKOJO_VM_ATTEMPT_LOG.md` — completed attempt history with verdicts.
- `reports/` — dated snapshots (allocation profile, A8/A9 corpus, regexp split).

## Order of work

Correctness, then observability/tooling, then measured optimization.
Name the layer, hold layer evidence **and** wall time; source, IL, or
assembly alone never decides.

## Loop

Falsifiable layer hypothesis → ceiling measurement (kill if noise) →
falsifying artifact → one change (`bench-ab` medians plus same-config
assembly diff) → mechanistic delta → record even when rejected.

## Tooling

- `tools/VmLoopProbe` (`--inspect-run` IL/locals, `--profile-opcodes` with
  `-p:OkojoVmProfile=true`, evidence-only).
- `capture-jit.ps1` → `artifacts/vmloopopt/snapshots/<ts>-<AttemptId>/`
  (`notice.md`, `patch.diff`, `jit/*.jit.txt`).
- `tools/OkojoBytecodeTool` for emission identity; `tools/CompilerAllocProbe`
  for phase-split allocation medians.
- V8 (`node --print-bytecode`, `tools/V8BytecodeTool`) for language/compiler/VM
  reference; `node -e` for builtin/runtime API reference. QuickJS is design
  input only.

## Rules carried from the attempt log

- Rejected attempts stay recorded with reasons; silence is not a verdict.
- Static emission frequency is a size signal, never an execution profile.
- Report compilation phases, first-use cost, allocated vs retained memory,
  bytecode size, register/context counts, suspension payload, and cold vs
  warmed feedback. Trade-offs are explicit; not every microbenchmark must improve.
