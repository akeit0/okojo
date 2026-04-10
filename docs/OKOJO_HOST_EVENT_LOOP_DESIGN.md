# Okojo Host Event Loop Design

## Status

Draft design note for review.

Implemented alignment checkpoint:

- `queueMicrotask(...)` remains engine-owned via Okojo microtasks
- `requestAnimationFrame(...)` / `cancelAnimationFrame(...)` now map to a host rendering queue in `Okojo.Browser`
- `setImmediate(...)` / `clearImmediate(...)` now map to a host check queue in `Okojo.Node`
- `JsRealm.QueueHostTask(...)` is the low-level core seam for queue-targeted host callbacks

This document is written for reviewers who know JavaScript and C# but do not know Okojo internals.

## Summary

Okojo currently has two separate host-facing scheduling concepts:

- a host task queue
- a timer host based on `ITimer`

That split is workable, but it does not model JavaScript host behavior cleanly.

JavaScript engines do not expose "timers" as a core language concept. They expose:

- ECMAScript jobs / microtasks
- host tasks
- host-defined event loop turns

Timers are just one source of host tasks.

This document proposes moving Okojo from:

- `IHostTaskQueue`
- `ITimerHost`

to a more browser-like and ECMA-262-aligned host abstraction built around host task scheduling and event-loop semantics.

The short version:

- core `Okojo` should own ECMAScript jobs and microtasks
- host integration should own tasks, delayed tasks, and event loop turns
- timers should stop being a first-class core host primitive
- `setTimeout` / `setInterval` / worker delivery / fetch callbacks should all ride through the same host event-loop abstraction
- the final public host contract should stay semantic and avoid .NET-specific wait primitives

## Why This Change Is Needed

### The current split models implementation details, not host semantics

Today Okojo roughly says:

- "here is how to enqueue a host task"
- "here is how to create a timer"

That describes how Okojo currently happens to run work.

It does not describe the real JavaScript model, which is:

- the engine owns ECMAScript jobs
- the host owns task sources and event loop turns
- timers are just one host task source

### Browser-like behavior is broader than timers

If the long-term direction is browser-grade behavior, Okojo needs a host abstraction that naturally covers:

- `setTimeout`
- `setInterval`
- `queueMicrotask`
- worker message delivery
- fetch completion
- future DOM/event delivery
- future rendering / idle / animation task sources

`ITimer` is too narrow for that role.

### ECMA-262 draws a clear line between jobs and host work

At a high level:

- ECMAScript defines jobs such as promise reactions
- the host decides when tasks happen and when the engine gets to run again

Okojo should reflect that line directly in its architecture.

## JavaScript Model Refresher

This section is intentionally plain-language.

### 1. ECMAScript jobs / microtasks

These are engine-level units of work.

Examples:

- promise reaction jobs
- async/await continuation jobs
- `queueMicrotask(...)`

Important property:

- once execution reaches a microtask checkpoint, microtasks drain before the next host task starts

### 2. Host tasks

These are scheduled by the environment around the engine.

Examples in a browser-like environment:

- a timer firing
- a message event being delivered
- a network completion callback becoming observable

Important property:

- tasks are not part of the ECMAScript language core
- they are defined by the host environment

### 3. Event loop turns

The event loop decides:

- when to pick the next task
- when to let the engine run script
- when to run the microtask checkpoint

That is the level Okojo should expose to hosts.

## Current Okojo Model

Current relevant host seams:

- `IHostTaskQueue`
- `ITimerHost`
- `DefaultHostTaskQueue`
- `DefaultTimerHost`

Current responsibility split:

- `JsAgent` owns ECMAScript microtasks
- host queue adapters own enqueuing external tasks
- timer host creates raw timers which later enqueue work back into the agent

This works, but it has these problems:

### Problem 1: timers are elevated too early

`ITimerHost` treats timers as a fundamental host primitive.

But from the point of view of JavaScript semantics, a timer is just:

- "schedule a future host task after at least N delay"

The real primitive is delayed task scheduling, not a reusable timer object.

### Problem 2: the model does not read like an event loop

A reviewer has to mentally reconstruct:

- tasks live here
- timers live there
- microtasks live elsewhere
- `PumpJobs()` drains some of them

That is harder to review than a direct event-loop model.

### Problem 3: future browser-like work will not fit cleanly

If Okojo later adds more host APIs, this split pushes the design toward more one-off services instead of one coherent host loop.

## Design Goals

### Primary goals

1. make the host model understandable to non-Okojo reviewers
2. align the architecture with the JavaScript event-loop model
3. keep ECMAScript jobs in core `Okojo`
4. move timers behind delayed-task scheduling rather than raw `ITimer`
5. let browser-like APIs share one host event-loop seam

### Non-goals

This design does not try to:

- implement DOM behavior
- define rendering or animation frame behavior yet
- fully redesign worker lifecycle APIs in one step
- move ECMAScript microtasks out of `Okojo`

## Proposed Architecture

## A. Core `Okojo` owns ECMAScript jobs

Core `Okojo` should continue to own:

- promise jobs
- async/await continuation jobs
- internal microtask draining

This stays engine-owned because it is language/runtime behavior, not host policy.

## B. Host integration owns tasks and delayed tasks

Host integration should own:

- enqueueing a host task
- scheduling a delayed host task
- choosing when an event loop turn happens

This is the right ownership boundary for:

- timers
- worker messages
- host callbacks from web APIs

## C. Separate the public host contract from the default .NET hosting implementation

The most important review point is this:

- the public host boundary should describe JavaScript host semantics
- the default `Okojo.Hosting` implementation may use .NET-specific waiting and signaling internally

Those are not the same API.

The public contract should stay small and semantic.

The default hosting package can add:

- waiting
- pumping helpers
- `TimeProvider`
- channels
- timers
- dispatcher integration

without leaking those details into the public design.

## D. Preferred public host contract shape

Instead of separate task queue and timer host contracts, the long-term public direction should be closer to:

```csharp
public interface IHostTaskScheduler
{
    void EnqueueTask(Action<object?> callback, object? state);

    IHostDelayedTask ScheduleDelayedTask(
        TimeSpan delay,
        Action<object?> callback,
        object? state);
}

public interface IHostDelayedTask : IDisposable
{
    bool Cancel();
}
```

This keeps the contract focused on what Okojo actually needs from a host:

- enqueue a host task
- schedule a delayed host task
- cancel delayed delivery

It does not assume:

- a `WaitHandle`
- a thread-based pump
- one specific waiting model
- per-call exposure of `JsAgent`

Important point:

- the abstraction talks about tasks and delayed tasks
- it does not talk about `ITimer`
- it does not force a .NET synchronization primitive into the public boundary

That keeps the interface at the correct semantic level.

## E. Default hosting implementation can still expose loop mechanics

The default .NET hosting package will still need practical loop mechanics.

Examples:

- wait for more work
- pump one turn
- integrate with a thread host
- integrate with a UI dispatcher

Those are valid implementation concerns, but they belong in `Okojo.Hosting`, not in the reviewer-facing public host contract.

Directional examples for implementation-side helpers:

- `IHostLoopPump`
- `IHostWorkNotifier`
- `OkojoThreadPoolEventLoop`
- `OkojoDeterministicTestLoop`

None of those need to be part of the minimal public scheduler boundary.

## F. Define the event loop in terms of turns

At a conceptual level, one host event-loop turn looks like:

1. pick one ready host task
2. run the host task into Okojo
3. let Okojo drain ECMAScript microtasks/jobs
4. return to the host loop

That is much easier to reason about than:

- timer callback
- task queue callback
- special-case microtask pumping

## Required Semantic Guarantees

The host/event-loop design should make the following guarantees explicit.

### 1. One agent is single-execution at a time

For a single `JsAgent`, host tasks must not execute concurrently unless a future API explicitly documents otherwise.

This preserves deterministic task ordering and reduces accidental races in host integrations.

### 2. Microtask checkpoint after each host task

After a host task runs into Okojo, Okojo performs a microtask checkpoint before the next host task starts.

This is the most important observable ordering guarantee.

### 3. Delayed tasks are a source of host tasks

Delayed tasks do not get a special execution path.

When a delayed task becomes ready, it becomes eligible for host-task delivery.

### 4. Nested turn semantics must stay explicit

The design should explicitly define whether nested pumping is allowed.

Current recommended direction:

- no concurrent host-task execution for the same agent
- nested pumping should be treated as an advanced/default-hosting concern, not silently allowed by the public contract

### 5. Host callbacks may enqueue more work

A host callback may enqueue additional host tasks or ECMAScript jobs while it is running.

Those follow normal ordering rules:

- enqueued ECMAScript jobs run at the next microtask checkpoint
- newly enqueued host tasks wait until a later host-task turn

## Proposed Terminology

Use names that map to the JavaScript model directly.

Preferred terms:

- ECMAScript jobs
- microtasks
- host tasks
- delayed host tasks
- event loop
- event loop turn

Avoid using "timer" as the main abstraction name.

Keep "timer" only at the API layer where JavaScript users see:

- `setTimeout`
- `setInterval`

## How Existing APIs Map To The New Model

### `queueMicrotask(...)`

Ownership:

- core `Okojo`

Behavior:

- enqueue an ECMAScript microtask/job

It should not go through the host event loop.

### `setTimeout(...)`

Ownership:

- host event loop

Behavior:

- schedule one delayed host task
- when the delay expires, the host loop enqueues the callback task

### `setInterval(...)`

Ownership:

- host event loop

Behavior:

- repeated delayed host tasks
- rescheduling policy belongs to the host-facing timer/window module
- actual future delivery uses the same delayed-task/event-loop path

Current recommended policy:

- do not backlog missed interval firings as multiple queued callbacks
- if the host falls behind, one future delivery is enough to represent the next interval turn
- exact drift/catch-up behavior should be documented as host-defined until Okojo intentionally standardizes browser-like timer semantics more tightly

### Worker `postMessage(...)`

Ownership:

- host task delivery

Behavior:

- enqueue a host task for the target agent

### `fetch(...)`

Ownership:

- web/host module

Behavior:

- network completion eventually enqueues a host task
- promise reactions stay in core microtask semantics

## Why This Is Better Than `ITimer`

### Reviewability

Someone who knows browsers or Node can immediately understand:

- host task queue
- delayed host task
- microtask checkpoint

They should not have to infer an event loop from raw .NET timer objects.

### Correctness

A single host event-loop abstraction makes it easier to preserve:

- task vs microtask ordering
- cancellation behavior
- worker message ordering
- future task-source-specific ordering rules

### Flexibility

Hosts may implement delayed tasks using:

- `System.Threading.Timer`
- `TimeProvider`
- a UI dispatcher
- a custom test loop
- a browser shell loop
- a single-thread deterministic simulator

Okojo should not care.

## Cancellation Semantics

Cancellation should be defined in terms of delivery eligibility, not timer internals.

Recommended semantics for `IHostDelayedTask.Cancel()`:

- before the delayed task becomes ready:
  - cancellation prevents future delivery
- after the delayed task becomes ready but before the callback begins:
  - host-defined, but the implementation must document the behavior
- after callback execution begins:
  - cancellation has no effect on the already-started callback

Additional rules:

- cancellation should be idempotent
- disposal should be safe to call more than once
- interval cancellation stops future deliveries, not a callback already in progress
- the contract should not imply rescheduling support on the delayed-task object itself

## Relationship To `TimeProvider`

`TimeProvider` is still useful, especially for tests and host integration.

But it should be used behind the host event loop implementation, not exposed as proof that timers are the central abstraction.

In other words:

- `TimeProvider` remains a good implementation dependency
- `ITimerHost` should stop being the architectural boundary

## Relationship To `JsAgent`

`JsAgent` should remain responsible for:

- ECMAScript microtasks
- running queued JS work
- cross-realm / cross-agent internal runtime coordination

`JsAgent` should not define the long-term public host event-loop contract.

That contract belongs at the host boundary.

`JsAgent` may still appear in internal Okojo implementation APIs, but it should not be required by the long-term public scheduling contract on every call.

## Proposed Layering

### `Okojo`

Owns:

- ECMAScript jobs
- microtask drain behavior
- agent execution

Knows about:

- a host task-scheduling interface

Does not own:

- timer policy
- host loop policy

### `Okojo.Hosting`

Owns:

- default event-loop adapters
- thread-pool-hosted implementations
- deterministic test/host integrations

### `Okojo.WebPlatform` and `Okojo.Browser`

Owns:

- browser-style globals such as:
  - `setTimeout`
  - `setInterval`
  - `queueMicrotask`
  - `fetch`

Uses:

- the host event-loop contract

## Migration Plan

## Phase 1: Add the new abstraction

Introduce:

- a small public scheduling abstraction such as `IHostTaskScheduler`

Keep old interfaces temporarily to avoid a large break in one patch.

## Phase 2: Port existing default implementations

Replace:

- `DefaultHostTaskQueue`
- `DefaultTimerHost`

with a single default event-loop implementation.

Likely implementation shape:

- enqueue immediate tasks
- schedule delayed tasks
- optionally wait for work inside `Okojo.Hosting`

## Phase 3: Port hosting adapters

In `Okojo.Hosting`, replace:

- `IHostingTaskQueue`
- `IHostingTimerHost`

with one hosting-side event-loop abstraction plus any implementation-side wait/pump helpers that the default .NET host needs.

That is the right public review surface for embedders.

## Phase 4: Move timer-using code to delayed-task APIs

Update:

- timer globals
- worker/message scheduling where appropriate
- fetch/web modules that currently indirectly depend on timer/task splits

## Phase 5: Remove old interfaces

After migration:

- remove `ITimerHost`
- remove raw timer-host configuration from runtime options
- simplify host service storage

## Compatibility Strategy

This is a breaking-change-friendly area.

The project direction already allows breaking cleanup here, so the design should optimize for clarity over preserving a transitional API forever.

Still, the migration should happen in small steps so behavior regressions are easy to review.

## Example Before And After

### Current mental model

```text
Promise jobs -> agent microtasks
Timers -> ITimerHost -> enqueue host task
Messages -> host task queue
PumpJobs -> drain mixed queues
```

### Proposed mental model

```text
ECMAScript jobs -> Okojo microtask queue
Host tasks -> host event loop
Delayed work -> host event loop delayed task scheduling
One event-loop turn:
  host task runs
  Okojo drains microtasks
```

The second model is much easier to explain and review.

## Open Questions

### 1. Should the public host boundary expose task sources explicitly?

Maybe later.

For now, a single host-task abstraction is enough.

Future expansion could add metadata such as:

- timer task
- worker message task
- network task

That could help debugging and observability.

### 2. Should `queueMicrotask` live in `Okojo.WebPlatform` or `Okojo`?

Behavior-wise it is closer to ECMAScript job semantics.

API-surface-wise it is a host-installed global in web-like environments.

Recommended approach:

- keep the queueing mechanics in core `Okojo`
- keep installation of the global function in `Okojo.WebPlatform` or another host/global package

### 3. Should event-loop pumping be explicit in tests?

Yes.

Tests should keep using explicit pumping because it makes ordering visible and deterministic.

### 4. Should the public interface be named `IHostEventLoop` or `IHostTaskScheduler`?

Both names are defensible.

Current recommendation:

- use a smaller semantic interface
- prefer `IHostTaskScheduler` for the minimal public contract
- reserve `IHostEventLoop` for a richer implementation-side abstraction only if Okojo later needs to model turns directly in the public API

## Risks

### Risk 1: accidental ordering regressions

Mitigation:

- keep focused tests for:
  - microtask-before-task ordering
  - timer cancellation
  - interval rescheduling
  - worker message delivery ordering
  - fetch completion ordering

### Risk 2: over-designing task sources too early

Mitigation:

- start with one host-task scheduling abstraction
- add richer task-source metadata only if a real need appears

### Risk 3: mixing host loop policy back into core

Mitigation:

- keep the interface small
- keep browser/web modules outside `Okojo`
- do not let core types grow timer-specific configuration again

### Risk 4: public API leaking .NET-specific waiting primitives

Mitigation:

- keep `WaitHandle`, channels, and timer objects inside `Okojo.Hosting`
- keep the public host contract semantic and implementation-agnostic

## Recommended Next Slice

The next implementation slice should be:

1. introduce a small public scheduling contract such as `IHostTaskScheduler` plus `IHostDelayedTask`
2. keep any richer wait/pump/event-loop mechanics inside `Okojo.Hosting`
3. route current host tasks and delayed tasks through the new scheduling seam
4. adapt current default and hosting implementations without exposing `WaitHandle` or per-call `JsAgent` coupling in the public API
5. keep ECMAScript microtasks unchanged
6. remove `ITimerHost` after behavior is green

That is the smallest slice that produces a clearer and more reviewable architecture.
