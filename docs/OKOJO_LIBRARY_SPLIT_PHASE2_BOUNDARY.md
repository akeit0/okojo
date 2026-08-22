# Phase 2 Boundary: direct engine inputs

## Scope

Remove the broad `IJsRuntimeHost` dependency from `JsAgent` and `JsRealm` while preserving execution, module, Promise, timer, worker, and interop behavior. Keep the physical project split and namespace rename deferred.

## Minimal repros

```js
Promise.resolve(1).then(() => globalThis.done = true);
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
- module loading/linking/evaluation tests
- timer, worker, host interop, and execution-check tests

## Reference observations

- V8: agent and realm execution state is engine-owned; host scheduling is an embedding concern.
- Node: module loading, timers, worker delivery, and CLR-style host binding are host integration concerns.
- Copy: remove the runtime-container reference from engine state.
- Intentional difference: ECMAScript-facing worker dispatch remains on the complete `JsRealm`; concrete worker lifecycle and serialization are still the next host-side slice. Agent construction now completes before host queue attachment and user initialization callbacks.

## Performance plan

Pass immutable service values and narrow callbacks once at agent creation. Keep property, Promise, and VM hot paths free of runtime-container lookup. Host queue policy is now an explicit host-side slow path; worker policy remains for the next slice.
