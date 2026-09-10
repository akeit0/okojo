# Runtime: caches, arrays, argument window

Status: `proposed` — next runtime work after the code/instance split.
Replaces: monomorphic property cache (`Execution/JsRealm.Vm.NamedPropertyIc.cs`),
range-scanning hole checks (`Execution/DenseArrayFastPath.cs`), caller-to-frame
argument copies (`Execution/JsRealm.Vm.cs`).

## A. Small polymorphic property caches

Today: one shape+slot per feedback location (overwritten on update); the
prototype fast path fires only when the holder is the receiver's immediate
prototype; the miss path does `TryGetPropertyAtom` and cache installation
can walk the chain a second time.

Direction: `empty → monomorphic → small polymorphic (2–4 entries) →
megamorphic fallback`. Monomorphic path stays cheap; wider storage allocates
only at sites that actually go polymorphic. Feedback lives on the
realm-local function instance, never on shared immutable code. The miss
result carries holder, slot, and cacheability so installation never
re-searches — while revalidating that accessor execution or reentrancy has
not invalidated the entry. Deeper-prototype caching follows with explicit
chain guards or invalidation tokens. Accessors, proxies, dynamic host
properties, and descriptor semantics are never bypassed.

## B. Explicit packed/holey array state

Dense storage and specialized builtin paths exist, but relocation fast paths
re-scan ranges for holes. Add explicit packed-vs-holey tracking with a
conservative packed→holey transition first; known-packed arrays skip scans.
Unboxed numeric element storage is a separate later experiment — never bundle
it with state tracking or with a `JsValue` redesign.

## C. Shared call argument window

Frame entry already avoids clearing unused registers, but arguments copy
whenever caller-side locations differ from the callee parameter window.
Make the compiler's outgoing argument area *be* the callee's incoming
parameter area on the common path. Requires the coordinated register
allocation and frame layout from [liveness-suspension.md](liveness-suspension.md) —
another reason the current ABI is renegotiable. Preserve extra arguments,
mapped `arguments`, rest parameters, constructors, cross-realm calls, and
tail-call behavior. Measure small-function and callback-heavy workloads, not
just arithmetic loops.

## Acceptance gate

Cache-hit behavior identical under prototype mutation, accessor, proxy, and
host-dynamic tests; packed arrays measurably skipping hole scans;
argument-window change with call-heavy (not only loop) benchmarks and full
conformance green (see [conformance-gate.md](conformance-gate.md)).
