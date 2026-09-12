# Okojo Execution Checkpoint Note

## Scope

Add a low-overhead execution checkpoint path for agent-level debugger sessions and execution constraints.
Extend the debugger checkpoint payload with an eager current-frame view and an on-demand JS stack snapshot so tools can inspect stack state without throwing.
Also support an agent-level execution timeout that is enforced on the same slowpath as checkpoint constraints.

This iteration keeps the runtime hot path lean:

- one fast flag check in the VM dispatch loop
- one countdown decrement per instruction when enabled
- one no-inline slowpath when the countdown hits zero or a `debugger` statement forces a checkpoint
- a separate no-inline boundary emit helper on call, return, pump, and generator suspend/resume paths
- call/return/pump/generator/debugger checkpoints are independently togglable per usage
- generator suspend/resume emit their own boundary checkpoints for debugger stop support
- current frame info is eager; full stack snapshot materialization is on-demand
- debugger stops expose a lightweight structured `CheckpointSourceLocation` on the checkpoint without bloating `StackFrameInfo`
- timeout checks stay on the slowpath so the dispatch loop stays minimal

## Minimal JS Repros

- `debugger;`
- `function inner() { debugger; } inner();`
- `function add(a, b) { return a + b; } add(1, 2);`
- `let total = 0; total = total + 1;`
- `let total = 0; while (true) total++;` with a short execution timeout

## Planned Tests

- `tests/Okojo.Tests/ExecutionCheckTests.cs`
- debugger checkpoint exposes a non-empty stack trace
- debugger checkpoint exposes current frame info immediately
- debugger checkpoint exposes a structured checkpoint source location when available
- debugger-facing consumers can use a cheap stop summary without walking the whole stack
- function call/return emits boundary checkpoints
- pump jobs emits a boundary checkpoint
- generator suspend/resume emits boundary checkpoints with source position
- call/return hooks can be enabled or disabled at runtime
- execution timeout throws a `RangeError`
- keep `tests/Okojo.Tests/SharedArrayBufferAtomicsFeatureTests.cs` as a regression guard for unrelated runtime changes

## Reference Observations

- V8/Node treat `debugger` as observable only when a debugger is attached; otherwise it is effectively inert. This implementation follows that shape.
- No direct Node equivalent exists for Okojo-specific execution constraints. That part is engine policy, not standard JavaScript behavior.

## Copy vs Intentional Difference

- Copy: `debugger` should be a no-op without an attached debugger.
- Intentional: Okojo exposes a lightweight checkpoint hook for engine-local constraints and debugger sessions.
- Intentional: debugger checkpoints expose current frame info eagerly and a JS stack snapshot on-demand.
- Intentional: debugger checkpoints expose a structured checkpoint source location, not on every frame.
- Intentional: generator suspend/resume are first-class checkpoint reasons for debugger stop support.
- Intentional: execution timeout is checked only at checkpoint boundaries, not per instruction.

## Perf Plan

- keep checkpoint dispatch on the opcode hot path as a flag check plus countdown decrement
- avoid nullable state in the runtime loop
- use `ulong.MaxValue` as the disabled sentinel
- keep checkpoint payload construction and callback invocation on the slowpath only
- keep call/return/pump emission behind a separate no-inline helper
- build full stack frames lazily from checkpoint state
- keep current frame info eager for cheap debugger stop inspection
- surface source identity on the checkpoint itself instead of duplicating it into each frame
- enforce timeouts on the slowpath using the host time provider
