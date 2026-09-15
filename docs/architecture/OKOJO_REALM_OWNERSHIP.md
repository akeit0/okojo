# Releasing embedding ownership of a realm

Status: implemented; six ownership tests and six realm-module tests pass.

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
- A retained module namespace or function can still root a released realm. The agent's module
  map registry uses weak keys and does not independently keep it alive. This is not a forced GC API.
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

## Realm module maps

Each realm owns its source loader and module map, including JavaScript, JSON, and text
namespaces, link records, and pending evaluations. `JsRealmOptions.ModuleSourceLoader` overrides
the runtime loader for that realm. URLs stay unchanged for resolution and `import.meta.url`.
Agent diagnostics without a realm argument continue to address the main realm; explicit realm
overloads address a child. Runtime termination clears all live maps. Registry release leaves a
retained realm's map usable and does not root an otherwise unreachable realm through the agent.

Minimal repro: two realms load `export const value = globalThis.value` from the same URL,
with globals 1 and 2. They must export 1 and 2 through distinct namespaces; repeated imports in
one realm keep identity. Node `vm.SourceTextModule` with two contexts and the same identifier
prints `1 2 false` for those observations (2026-09-15). This copies context ownership;
Okojo supplies caching as host policy. HTML associates a module map with environment settings:
<https://html.spec.whatwg.org/multipage/webappapis.html#module-map>.

Tests in `RealmModuleTests.cs` cover loader isolation, static/dynamic imports, top-level
await, namespace identity, import metadata, per-realm invalidation, and collection after release
with loaded modules. This changes host module ownership, not bytecode or VM dispatch; bytecode
mismatch inspection is not applicable. Module map access remains an infrequent import/link path,
with no additional branch in property access or instruction dispatch. Browser task cancellation
remains separate work in `TODO.md`.

Validation: the full Okojo suite passes 2,322 tests with four existing skips (2026-09-15).
