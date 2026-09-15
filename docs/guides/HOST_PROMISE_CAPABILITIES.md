# Host Promise capabilities

Scope: provide the host integration layer with a pending intrinsic Promise, first-call
resolution locking, internal reactions, and explicit handled marking. Internal runtime
capabilities, reaction records, and Promise state remain internal. No compiler or VM changes.

Falconet's View Transition binding needs three stable promises and callback result adoption.
Capturing JavaScript `Promise`/`then` functions is insufficient: species and constructor
properties remain observable and mutable. `JsRealm.CreatePromiseCapability()` provides the
host-owned operation; only its `Promise` is published to script. Calls stay on the owning
agent's sequence. Host reactions must not throw; no derived Promise is allocated.

Reference repro (Node):

```js
let complete;
const result = Promise.withResolvers();
result.resolve(new Promise(resolve => complete = resolve));
result.reject('late');
result.promise.then(console.log);
complete('first');
```

Copy: resolution locks immediately and adopts asynchronously (`first`, never `late`).
Intentional host distinction: Observe uses internal reactions without then/species lookup.
This is the Web IDL host-operation boundary, not a replacement for JavaScript `.then()`.
Tests: `tests/Okojo.Tests/HostPromiseCapabilityTests.cs` checks pending resolution, thenable
adoption, late rejection, tampered Promise properties, reaction scheduling, and agent checks.
Performance: one host handle and one Promise per operation; reaction functions allocate only
when Observe is called. Existing JavaScript Promise hot paths are unchanged.
