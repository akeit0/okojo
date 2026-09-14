# Releasing embedding ownership of a realm

Status: implemented; the six focused ownership tests pass.

Scope: let a browser host stop rooting a replaced child realm without terminating the shared
agent or invalidating JavaScript references retained by another realm. The stable entry point
is `JsRuntime.ReleaseRealm`; registry mutation stays internal to `JsAgent`.

## Minimal repro

An embedding creates a child with `runtime.CreateRealm()`, evaluates the following code, and
saves the returned function in its parent realm:

```js
globalThis.value = 42;
() => value
```

Releasing the child removes the runtime's registry ownership. Calling the saved function must
still return 42. After all references and pending jobs disappear, the realm can be collected.
The main realm remains owned until the runtime is disposed. Realm IDs are never recycled.

## Contract and boundaries

- Release is idempotent for an additional realm belonging to the runtime's main agent.
- Reject null, foreign-agent, and main-realm arguments; reject calls after runtime disposal.
- Release does not clear globals, kill functions, or cancel promise jobs. The browser host
  detaches document services and cancels its own navigation/timer/network work separately.
- Agent module caches and other externally retained objects can still root a released realm.
  This API removes registry ownership only; it is not a forced garbage collection API.
- Same-agent object references preserve identity. `BridgeFromOtherRealm` copies object data
  and is not a replacement for a browser window proxy.

## Tests

`tests/Okojo.Tests/RealmOwnershipTests.cs` covers registry bounds, collectible released realms,
retained function and promise behavior, argument validation, main-realm stability, and unique
IDs after release and reentrant initialization.

## Reference observations

Node's `vm.createContext` does not force-invalidate functions when the embedder drops its
context reference. The observed result of creating `() => value`, dropping the context,
calling `global.gc()` under `node --expose-gc`, then invoking the saved function is 42.

This copies reference-lifetime behavior. Explicit registry release is an Okojo embedding API,
not a new JavaScript builtin. There is no compiler, bytecode, VM dispatch, or builtin change;
bytecode/VM mismatch inspection is not applicable to this ownership-only change.

## Performance and deferred work

Release is an infrequent host operation under the existing realm registry lock. It removes
one list entry; no new branch enters JavaScript execution or property access. Reserve monotonic
IDs before initialization so reentrant creation and failed initialization cannot reuse IDs.
The GC test checks the actual root removal; no benchmark is required for a list removal.

Per-realm module cache/loading isolation and browser task cancellation are separate work,
tracked in `TODO.md`. They must be resolved before claiming complete iframe document teardown.
