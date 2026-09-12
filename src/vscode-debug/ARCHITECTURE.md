# Okojo debugger architecture

## Layers

```text
VS Code debug UI                    Any stdio DAP client
       |                                    |
extension.ts (inline descriptor)       main.ts + dapProtocol.ts
       |                                    |
       +---------- OkojoDebugSession --------+
                         |
                   hostClient.ts
              correlated newline-JSON RPC
                         |
               Okojo.DebugServer process
                         |
                 DebuggerSession queue
                         |
               synchronous VM checkpoints
```

`adapter.ts` contains no VS Code dependency. `extension.ts` owns editor settings,
workspace trust, per-session UI bindings and the optional bytecode webview. The
standalone entry reserves stdout for Content-Length-framed UTF-8 DAP. The debug
host remains usable through its legacy text commands as well as the new private
RPC protocol. The host is not itself a DAP endpoint.

## Lifecycle and execution ownership

The adapter state machine is `created -> initialized -> starting -> configuring
-> running <-> paused -> terminated`. Launch always starts the host behind a
pre-execution entry barrier. Breakpoints may be installed before compilation.
`initialized` is sent when the barrier is ready, and `configurationDone` releases
the pending launch response. The entry stop is exposed only for `stopOnEntry`.

Only the execution thread reads or changes VM inspection/breakpoint state. The
stdin thread queues strings and wakes the existing blocking command collection.
At periodic checkpoints, running-safe commands are drained. During a pause,
`WaitForResume` handles queued commands without unwinding the JavaScript stack.

All host requests carry `id`, `command`, and `arguments`; replies carry
`event: "response"`, `requestId`, `success`, and `body`/`message`. Reply correlation
and timeouts live in `hostClient.ts`. RPC replies are processed before subsequent
stop events from the same pipe chunk, preserving resume-response/continued/stop
ordering. The adapter does not use timers or editor observation requests to decide
whether continue is allowed. It owns and cleans up the child process tree.

## Inspection and stepping

Frame IDs are allocated per stop. Variables references are allocated on the VM
thread, retained only for that pause, and never reused. Scopes refer to the chosen
call frame, not arbitrary caller locals. Object children are enumerated lazily
with paging and descriptor reads; getters/proxies are not invoked. Identity-based
handles preserve cycles without recursive formatting. Evaluation accepts only
identifier/property paths and rejects calls, operators and writes.

Continue restores the configured checkpoint interval (default 1024 in DAP).
Stepping requests interval 1. The periodic slow path reloads the interval after a
pause so it does not overwrite a step request with the previous interval. A
monotonic instruction counter distinguishes repeated visits to the same PC.
Frame transitions are valid step targets even when source line numbers coincide.
Exact step positions are remapped into the same authored coordinate space as
breakpoints/stops when source maps are enabled.

Debugger termination uses `JsFatalRuntimeException`, not a cancellation exception
that JavaScript could catch. `IsStopRequested` lets the host report intentional
shutdown as exit code 0. Runtime constraints and ECMAScript language semantics are
otherwise unchanged; no new checks are added to the per-instruction VM hot path.

## Deferred capabilities and references

See the extension README and ROADMAP for supported operations and explicit limits.
`docs/dap-debugger/` records the supplied ChibiRuby/Lua-CSharp references,
rationale, regression cases, and the validation record. No reference
implementation code was copied.
