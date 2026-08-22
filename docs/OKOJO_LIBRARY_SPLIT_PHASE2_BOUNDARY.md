# Phase 2 Boundary: direct engine inputs

## Scope

Remove the broad `IJsRuntimeHost` dependency from `JsAgent` and `JsRealm` while preserving execution, module, Promise, timer, worker, and interop behavior. Keep the physical project split and namespace rename deferred.

The boundary must also let a browser profile own its complete event loop. Engine and runtime convenience APIs must not force task selection, timer, rendering, worker-message, waiting, or microtask-checkpoint policy on `Okojo.Browser`.

## Minimal repros

```js
Promise.resolve(1).then(() => globalThis.done = true);
```

```js
const order = [];
Promise.resolve().then(() => {
  order.push("promise-1");
  Promise.resolve().then(() => order.push("promise-2"));
});
```

```js
const worker = createWorker();
worker.postMessage({ value: 1 });
```

```js
import { value } from "./module.js";
```

## Planned regression coverage

- `tests/Okojo.Tests/EngineRealmTests.cs`
- `tests/Okojo.Tests/AgentJobQueueTests.cs`
- Promise/async tests
- Promise FIFO, recursively queued Promise jobs, and non-reentrant checkpoint tests
- custom host-loop tests proving that a browser can select one task and request the checkpoint itself
- module loading/linking/evaluation tests
- timer, worker, host interop, and execution-check tests

## Reference observations

- ECMA-262: `HostEnqueuePromiseJob` is host-defined, but Promise jobs must run in enqueue order.
- HTML: an event-loop iteration selects a runnable task and then performs a microtask checkpoint; a checkpoint is non-reentrant and drains the microtask queue, including microtasks queued by other microtasks.
- V8: agent and realm execution state is engine-owned; host scheduling is an embedding concern.
- Node: module loading, timers, worker delivery, Node priority jobs, and CLR-style host binding are host integration concerns rather than ECMAScript event-loop rules.
- Copy: remove the runtime-container reference from engine state.
- Intentional difference: the runtime may provide a convenience pump, but a browser profile is not required to use it and can drive the same low-level primitives directly.

Normative references:

- [ECMA-262 Jobs and Host Operations to Enqueue Jobs](https://tc39.es/ecma262/multipage/executable-code-and-execution-contexts.html#sec-jobs-and-host-operations-to-enqueue-jobs)
- [HTML event loops](https://html.spec.whatwg.org/multipage/webappapis.html#event-loops)
- [HTML perform a microtask checkpoint](https://html.spec.whatwg.org/multipage/webappapis.html#perform-a-microtask-checkpoint)

## Event-loop ownership

The engine owns the Promise-job queue and the operation that runs a Promise-job checkpoint. The checkpoint must preserve enqueue order, prevent nested checkpoints, and continue until no Promise jobs remain.

The embedding host owns:

- task sources, readiness, selection, and fairness
- timer, I/O, rendering, and worker-message queues
- waiting and wake-up behavior
- the points at which the selected host specification requires a microtask checkpoint
- profile-specific priority queues such as Node's `nextTick`

Low-level operations must remain independently callable. No engine operation may silently run a host task, and no mandatory runtime pump may run between a browser's selected task and its required microtask checkpoint. `HostPump` and other default loops are optional policies above these primitives.

## Worker boundary for the next slice

- Keep `IHostMessageSerializer` as the cohesive engine-owned host contract because its boundary values are `JsRealm` and `JsValue`.
- Move the default serializer implementation, worker lifecycle, queue choice, and per-realm worker messaging state to `Okojo.JavaScript.Runtime` ownership.
- Use one concrete runtime-owned `WorkerMessaging` component; do not add a speculative `IWorkerMessagingService`.
- Move `IWorkerHost`, `WorkerHostBinding`, `DefaultWorkerHost`, and `WorkerHandleFactory` out of engine ownership.
- Keep `JsAgent.PostMessage` and `MessageReceived` for this slice as the narrow engine delivery mechanism; the runtime owns worker policy and the JS-facing projection. Do not add another transport interface unless the physical split proves this mechanism insufficient.
- Keep cross-realm value bridging in `JsRealm`, separated from worker messaging into an engine-owned file.
- Keep browser `Worker`, `postMessage`, `onmessage`, `onmessageerror`, and event-object behavior in `Okojo.WebPlatform`. The Okojo-specific `createWorker` convenience remains an opt-in hosting API and must not be installed by the browser profile.

## Conformance gate during migration

The split is not allowed to use temporary observable behavior that violates the applicable specification. Every behavior-bearing slice must preserve:

- Promise-job FIFO order and draining of jobs queued during the checkpoint
- non-reentrant Promise-job checkpoints
- run-to-completion for JavaScript execution on one agent
- host task ordering selected by the active host profile
- no duplicated or dropped accepted task or job during initialization and queue attachment

A slice is incomplete if it introduces a build warning, a failing test, an accepted "known" failure, or requires a browser to use the default runtime pump. Agent host queues must be attached before realm or agent initialization callbacks can enqueue or run work.

## Performance plan

Pass immutable service values and narrow callbacks once at agent creation. Keep property, Promise, and VM hot paths free of runtime-container lookup. Host queue policy is an explicit host-side slow path. Browser-controlled scheduling must use the same primitives as the convenience pump, without an extra policy layer in the engine hot path.
