# Host global-this objects

Status: implemented, 2026-09-15.

## Scope and contract

Provide a host-owned, retargetable same-agent window reference and a realm option for its
global-this object. Keep global binding storage in `JsGlobalObject`. Script entry, indirect
eval, sloppy calls, and host callbacks use the configured global-this object; strict calls
and module top-level this retain their language semantics. Default embedding behavior is unchanged.

The host forwarding object reuses internal proxy dispatch with an inaccessible, null-prototype
handler. Reads, writes, descriptors, symbols, keys, and receivers forward to the current global.
Prototype changes are rejected unless unchanged; preventing extensions fails. Only the host can
retarget or detach it. Allocate it in a long-lived anchor realm to avoid retaining a replaced
document. Cross-origin restricted properties, indexed/named browsing contexts, and navigation
policy belong to the browser host and are deferred. This is not a complete HTML WindowProxy.

## Repros and validation

`this === globalThis`, `function f() { return this; } f() === globalThis`, strict calls,
indirect eval, and module top-level this exercise the distinction. Save a window, document,
and function; retarget the window and verify only the window follows the replacement. Define a
nonconfigurable property, retarget, and inspect descriptors and keys without dummy-target
invariant failures. Test receiver forwarding, nested JS proxies, foreign-agent rejection,
detachment, and garbage collection in `tests/Okojo.Tests/HostGlobalThisTests.cs`.

## References and performance

HTML defines a stable reference forwarding to the active Window:
<https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object>.
V8/Okojo bytecode is inspected using `artifacts/okojobytecodetool/cases/host-global-this.js`;
observations will be recorded below. Copy language this semantics; host retargeting is an
embedding capability, not a JavaScript Proxy API. No compiler/opcode changes are planned.
Ordinary object access stays unchanged. Window access uses the existing uncached proxy slow
path; a fixed handler adds allocation per browsing context, not per property operation.

## Reference results and fixes

Node `vm.runInThisContext` returns `[true,true,true,true]` for script-this, sloppy-this,
strict-this, and indirect-eval identity checks. CommonJS top-level this intentionally differs
because Node wraps modules. V8's sloppy function uses `Ldar <this>; Return`; Okojo emits
`LdaThis; Return`. Keep bytecode unchanged and supply the configured realm this at entry/coercion.
The snapshot is `20260915-095417/host-global-this.disasm.txt` under the documented artifact tree.

The accessor repro returns 42 in Node. Okojo's VM trace now visits `LdaThis`, `Star`,
`LdaNamedProperty`, `Return` and returns 42 too. The old global object path ignored an explicit
receiver for accessors stored in ordinary named slots. It now passes that receiver through.
A warm nested-proxy read also exposed target-slot feedback being cached against the proxy's own
empty storage, causing an index-out-of-range exception. Proxy forwarding now returns invalid
slot feedback for named gets/sets. This stays on the existing slow path and avoids a new branch
in ordinary-object dispatch. Tests cover warm sites before and after replacement, nested proxies,
fixed descriptors, symbols/indices, receiver forwarding, anchor-prototype pollution, detachment,
foreign agents, and collection with the proxy/runtime still alive.

## Embedding example

```csharp
using var runtime = JsRuntime.CreateBuilder().Build();
var window = new JsWindowProxy(runtime.MainRealm);
var document = runtime.CreateRealm(options => options.GlobalThisObject = window);
window.SetTarget(document); // before executing the document's scripts
// On navigation, create a fresh realm with the same GlobalThisObject, then retarget.
window.SetTarget(null); // deny all property operations and release the current target
runtime.ReleaseRealm(document);
```

`JsRealmOptions.GlobalThisObject` is the stable realm-configuration seam. `JsWindowProxy` lives
in the host integration/object layer; the proxy core and object-layout machinery remain internal.
Hosts must disable access before exposing an opaque or disallowed target. Disabled references
throw TypeError, a conservative capability gate rather than HTML's restricted cross-origin surface.

Validation: nine focused regressions pass; full Okojo suite passes 2,331 tests with four existing
skips and no build warnings. Accessor and warm-property bytecode snapshots preserve the existing
instruction sequence; the correction is in receiver forwarding and proxy cache feedback.

### Follow-up: module entry this

The Falconet consumer test exposed a preexisting module-entry bug: `ModuleExecutor` used the
classic script entry point, so module top-level this received the global object (now the proxy).
Scope: pass undefined explicitly at module entry, retaining classic script this and strict
function behavior. Add synchronous and top-level-await module cases to `HostGlobalThisTests`.
Node `--input-type=module` is the reference; both module entry forms must observe undefined.
Keep compilation/opcodes unchanged and select the entry receiver at the execution boundary.

Module reference: Node prints true before and after top-level await. V8 reads `Ldar <this>` in
its module body; Okojo's `--module-disasm` emits `LdaThis; StaModuleVariable`. Supply undefined
at the module entry boundary, matching the reference without rewriting expressions. Snapshots
are under `20260915-100745`. Eleven focused cases and the full 2,333-test suite pass (four
existing skips, zero build warnings) after the module-entry correction.
