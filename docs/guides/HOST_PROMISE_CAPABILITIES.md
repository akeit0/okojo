# Host Promise capabilities

Scope: provide the host integration layer with a pending intrinsic Promise, first-call
resolution locking, internal reactions, and explicit handled marking. Internal runtime
capabilities, reaction records, and Promise state remain internal. No compiler or VM changes.

Host APIs need stable native promises for asynchronous completion and callback result adoption.
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
when Observe is called.

## Intrinsic conversion and existing-promise reactions

Host callback return conversion needs `PromiseResolve(%Promise%, value)` identity and ordering:
an existing intrinsic Promise must not gain an extra adoption job. `CreateResolvedPromise`
must observe a Promise's constructor as that operation requires. `ObservePromise` attaches
internal reactions to an existing native Promise without consulting then/species or creating
a derived result. Capability observation uses the same boundary.

The contracts follow ECMA-262's
[PromiseResolve](https://tc39.es/ecma262/multipage/control-abstraction-objects.html#sec-promise-resolve)
and [Promise resolve functions](https://tc39.es/ecma262/multipage/control-abstraction-objects.html#sec-promise-resolve-functions).

General runtime regression: resolving with a native Promise must still read an overridden
`Promise.prototype.then`. The assimilation shortcut previously tested only the instance's
own `then` descriptor. A prototype guard alone is insufficient: direct assimilation also
omits the thenable job and the builtin `then` call's observable species construction.
Remove this shortcut and use ordinary thenable assimilation. Reject self-resolution before
any `then` lookup, including when the instance has replaced its own method.
Node reference: save the original `then`, replace the prototype method with a function that
resolves `custom`, then resolve a second Promise with the first. Observing the second with
the saved method reports `custom`. Promise conversion tests cover same-realm identity,
constructor getters, related-realm wrapping, host reaction order, and self-resolution.
Node's adoption order is `one,two,adopted,three`. No opcode changes; the corrected native-Promise
path allocates the ordinary thenable job and resolving functions. Any later optimization must
preserve this job order and observable species behavior, with measurements before adding it.

Evidence: pre-fix host conversion/prototype tests fail in three cases. The VM trace shows the
property overwrite and construction, but never calls the replacement method. The disassembly
is saved under `artifacts/okojobytecodetool/snapshots/20260915-144327/`; the fault is in Promise
assimilation, not emitted bytecode.

The extracted Promise Test262 slice initially passes 627 cases and fails two newer
`Promise.try` identity cases. The current
[Promise.try algorithm](https://tc39.es/ecma262/multipage/control-abstraction-objects.html#sec-promise.try)
converts a successful callback result with PromiseResolve; it returns an existing Promise of
the receiver constructor directly. It invokes the callback before constructing a result, and
turns a non-callable callback into rejection. Implement the complete operation, sharing the
same PromiseResolve helper. That helper accepts an object receiver and checks constructibility
only if a new capability is needed, as required by Promise.resolve too.

Node v25.9.0 still wraps the Promise.try result; this is an intentional difference from that
older implementation, supported by the current specification and the checked-out Test262 cases.
The sparse Test262 checkout is unchanged; Promise tests and harness are extracted with git archive
to `artifacts/host-promise-conformance-20260915` for this verification.

Validation: 13 focused host/Promise regressions and the full Okojo suite pass (2,346 passed,
four existing skips, no build warnings). The extracted Promise Test262 slice passes all 629
executed cases; 103 cases follow the runner's existing exclusion rules. This is a focused
Promise run, not a full Test262 conformance result.
