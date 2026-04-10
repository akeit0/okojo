# Okojo Debug Server Note

## Scope

Create the first synchronous debug host for Okojo in `src/Okojo.DebugServer`.

This host is the runtime-side companion to the VS Code debugger scaffold. It should stay thin, line-oriented, and checkpoint-driven.

## Minimal JS Repros

- `debugger;`
- `function inner() { debugger; } inner();`
- `function outer() { return inner(); } function inner() { debugger; } outer();`
- `function* gen() { yield 1; return 2; } const it = gen(); it.next(); it.next();`

## Planned Tests

- `tests/Okojo.Tests/ExecutionCheckTests.Debugger.cs`
  - debugger statement pause metadata
  - source location on stop
- future debug-host integration tests
  - script launch from source path
  - source-path breakpoint arming
  - continue / step / quit command handling

## Reference Observations

- Lua-CSharp splits the debug host from the VS Code adapter.
- Okojo already has checkpoint payloads and paused snapshots, so the host should consume them rather than rebuilding execution state.
- Okojo should stay synchronous in the VM and debugger stop path.

## Intended Copy vs Difference

- Copy: separate host and adapter projects.
- Copy: line-oriented stop reporting is enough for the first pass.
- Copy: breakpoint arming should be source-path keyed.
- Intentional: Okojo does not need async VM execution control.
- Intentional: the host can remain much smaller than the Lua-CSharp reference because the runtime already exposes structured checkpoints.

## Perf Plan

- keep stop formatting off the VM hot path
- keep transport simple and serial
- avoid serializing large payloads unless the debugger actually pauses
