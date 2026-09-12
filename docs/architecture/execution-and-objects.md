# Execution and objects (current)

Status: architecture synthesis (base `c651320`).

## Dispatch loop

`JsRealm.Run` (`JsRealm.VmLoop.cs`) is a large `switch(op)` loop (~8.4k IL
bytes, ~100+ locals) with cold groups already outlined to `NoInlining`
handlers returning a consumed-delta (`a2-hot-cold-split`, Tier1 −5.9%) and an
IL-locals diet (`a1-locals-diet`, −2.4%). Hot `JsValue` accessors carry
`AggressiveInlining` after audit. Normal builds contain no per-dispatch
profiling code; profiling uses `-p:OkojoVmProfile=true` evidence-only builds.

Durable findings (do not re-derive without new data —
`../performance/OKOJO_VM_OPTIMIZATION_INSIGHTS.md`):

- Six opcode clusters = six jump tables, not one; spreading across clusters
  beats compacting (~35% on loop-shaped streams, BTB sets).
- Function-pointer/threaded dispatch is ~3× slower on cyclic streams
  (indirect call + state spill); decided KEEP-switch in
  `../decisions/OKOJO_A7_DISPATCH_DESIGN.md`.
- The `pc`-as-`ref` pitfall: callee reseat never reseats the caller.

Methodology, tooling (`VmLoopProbe`, `capture-jit.ps1`, snapshot layout),
and the active C/V/A plan live under `../performance/`.

## Frames and calls

Direct VM-stack calls exist and frame entry avoids clearing unused registers,
but arguments are copied when the caller-side location differs from the new
frame's parameter window. The redesign direction is a shared argument window
owned jointly with register allocation — which is why the current frame
ABI is explicitly renegotiable (see
[../proposals/runtime-caches-arrays-calls.md](../proposals/runtime-caches-arrays-calls.md)).

## Objects and properties

Default model is transition shapes with dictionary fallback for churn;
numeric index keys stay out of shape transitions. The own-property cache is
monomorphic (one shape+slot per feedback location, overwritten on update);
the prototype fast path requires the holder to be the receiver's immediate
prototype. The miss path looks up, then cache installation can walk the chain
again. Redesign: small polymorphic caches with feedback on the realm-local
instance, and a miss result carrying holder/slot/cacheability (guarding
accessors, proxies, host dynamics) — proposal, not current.

## Arrays

Dense storage plus specialized builtin paths exist, but hole-safety checks
(`DenseArrayFastPath.RangeHasHole`) scan ranges. Redesign: explicit
packed/holey state with conservative packed→holey transition first; unboxed
numeric storage is a separate later experiment, not bundled with it.

## Exceptions

Whole-loop `try{while}` remains. The EH-split sketch (thin wrapper +
no-EH core, static handler tables) is an unmeasured experiment; its note
still cites a per-dispatch null-init cost that current code hoists to once
per `Run`, so re-baseline before adopting. Details:
[../proposals/exception-handling.md](../proposals/exception-handling.md).
