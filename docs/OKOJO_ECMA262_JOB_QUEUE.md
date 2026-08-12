# ECMA-262 Job Queue Model

## Status

Implemented. The agent's job queues now use ECMA-262 named-job-queue semantics with granular, host-driven manual execution.

## Scope

- `JsAgent` owns four named FIFO job queues:
  - `ScriptJobs` — script/module evaluation
  - `PromiseJobs` — promise reactions, async continuations, `queueMicrotask`
  - `HostJobs` — default host task queue (timers, messages, host callbacks)
  - `HostPriorityJobs` — host-defined priority class (e.g. Node `nextTick`)
- `JobQueueName` constants name the queues.
- `PendingJob` is a `(callback, state)` FIFO entry; each queue drains independently.

## API

Engine-owned (public on `JsAgent`):

- `EnqueueScriptJob(Action)` / `EnqueueScriptJob(Action<object?>, object?)`
- `EnqueuePromiseJob(Action)` / `EnqueuePromiseJob(Action<object?>, object?)`
- `EnqueueJob(string queueName, ...)` — host-defined named queues (unknown names → `HostJobs`)
- `RunJobs(string queueName)` — drains one named queue FIFO, returns count
- `RunScriptJobs()` / `RunPromiseJobs()` — convenience
- `GetJobCount(string queueName)` — inspection
- `PumpJobs()` — default policy: host priority → promise checkpoint → one host job (retained for simple hosts)

Engine-internal:

- `EnqueueHostTask(...)` — routes through the `IHostTaskScheduler` seam so hosts observe/intercept deliveries; `HostTaskTarget` delivers straight into `HostJobs`.
- `EnqueueHostPriorityJob(...)` — host priority class (Node `nextTick`).

## Manual host management

Browsers and other hosts that drive their own event loop use the granular primitives instead of `PumpJobs`:

```csharp
// host task sources inject work:
agent.EnqueueJob("timers", () => OnTimer());
agent.EnqueueJob("network", () => OnNetwork());

// host decides ordering per turn:
agent.RunPromiseJobs();        // microtask checkpoint after a task
agent.RunJobs("timers");
agent.RunJobs("network");
```

The engine never hardcodes a task/microtask ordering policy; `PumpJobs` is only a default convenience.

## Why

The previous `priorityMicrotasks` / `microtasks` / `tasks` three-queue model baked an HTML/Node event-loop shape into the agent. ECMA-262 defines FIFO named job queues and lets the host choose ordering across job classes. This restructure aligns the agent with the spec and gives hosts precise manual control.

## Reference

- V8 (primary) for language/compiler/VM job behavior.
- Node for host-defined priority job classes (`nextTick`).

## Tests

- `tests/Okojo.Tests/AgentJobQueueTests.cs` — ordering, reentrancy, per-queue manual run, named host queue isolation, script queue.
- `TimerTests`, `WorkerAgentTests`, `WebWorkerTests`, `AsyncPromiseTests` — regression coverage.
- test262 `built-ins/Promise` 250/250, `built-ins/AsyncFunction` 18/18.
