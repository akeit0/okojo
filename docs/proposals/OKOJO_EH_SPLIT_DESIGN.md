# EH Split Design: try/catch wrapper + no-EH dispatch core

Status: DESIGN (not implemented). Owner review required before coding.

## 1. Problem

The entire interpreter dispatch loop lives inside one `try { while (true) {
... } } catch { ... }` region. Observable JIT costs, all verified in
listings:

1. **Dead null-init store per dispatch.** `ref var opcodePc = ref
   Unsafe.NullRef<byte>()` is EH-live (the catch reads it), so RyuJIT
   re-emits the never-read initialization store on the per-dispatch join
   (`xor rdx / mov bword ptr [rbp-0x370], rdx`) before the live store
   (F-evidence; A25 item 2).
2. **EH-live locals constrain codegen**: the `op` spill at `[rbp-0x8C]`,
   register-allocation constraints across the whole loop, and the
   GC-protected-region funclet machinery for the giant method.
3. **A6 history**: restructuring `while { try }` into `try { while }` was
   measured an IL no-op - narrowing the region is not expressible while the
   loop stays inside the try. The only way out is moving the loop out.

JS exceptions ARE the VM's throw mechanism (`JsRuntimeException` ->
catch -> JS handler-table unwind -> re-enter), so the catch itself is
irremovable. The question is what lives inside the EH region.

## 2. Current structure (verified)

```
Run(stopAtCallerFp, startPc):
    managedRunDepth++; acc = this.acc
ReloadFrame:
    <derive currentFunc/bytecode/registerRef/objectPool from fp>
    while (true)
    {
        <per-iteration shared locals: num1.., uLhs/uRhs, opcodePc = NullRef>
        try
        {
            NextOp:
            opcodePc = ref pc; op = (JsOpCode)opcodePc; pc++
            if (--nextCheck == 0) { this.acc = acc; CheckExecutionSlowPath(..., ref opcodePc, ...) }
            switch (op) { ... }             // ~150 arms
            operandScale = Single
        }
        catch (Exception e)
        {
            this.acc = acc
            if (TryCatchRunCoreException(e, ref opcodePc, stopAtCallerFp,
                                         ref startPc, out newEx, ref acc))
                goto ReloadFrame            // JS handler found: resume there
            if (newEx is not null) throw newEx
            throw                           // uncaught: propagate to caller
        }
    }
finally:
    this.acc = acc; managedRunDepth--
```

`TryCatchRunCoreException`: wraps non-JS exceptions, captures exception
stack/lazy message using the **faulting opcode pc**, then
`TryHandleJsRuntimeException(Stack, stopAtCallerFp, ref fp, out startPc)`
performs the JS handler-table unwind by hand (mutates `fp`, produces the
handler `startPc`), sets `acc = ex.ThrownValue ?? error-object`, and
returns true -> `goto ReloadFrame` re-enters the loop at the handler.

Critical catch-input facts (verified):

- The catch reads only **faulting pc** and **fp** (plus stopAtCallerFp)
  from VM state. Fault-time `acc` is never read: `acc` is overwritten with
  `ex.ThrownValue ?? CreateErrorObjectFromException(ex)`, and
  `CreateErrorObjectFromException(ex)` builds the error from the exception
  alone. The pre-catch `this.acc = acc` publication exists for re-entrant
  safety, not because the value is consumed.
- `fp` changes only at `ReloadFrame` and inside
  `TryHandleJsRuntimeException` (both far rarer than dispatch).

## 3. Chosen design: thin Run wrapper + no-EH RunCore

```
[SkipLocalsInit]
private void Run(int stopAtCallerFp = -1, int startPc = 0)
{
    managedRunDepth++;
    var state = new RunState
    {
        Fp = fp, StopAtCallerFp = stopAtCallerFp, StartPc = startPc,
        Acc = this.acc, NextCheck = Agent.ExecutionCheckCountdown,
    };
    try
    {
        while (true)
        {
            RunCore(ref state);              // NO try/catch anywhere inside
            return;                          // clean host exit (Return reached)
        }
    }
    catch (Exception e)
    {
        // same logic as TryCatchRunCoreException, reading state.OpcodePc /
        // state.Fp instead of EH-live locals
        if (TryCatchRunCoreException(e, state, out var newEx))
        {
            // TryCatchRunCoreException set state.Fp (unwound) and
            // state.StartPc (handler target) and state.Acc (thrown value)
            continue;                        // re-enter RunCore at handler
        }
        if (newEx is not null) throw newEx;
        throw;
    }
    finally
    {
        this.acc = state.Acc;                // published by RunCore on exit
        managedRunDepth--;
    }
}

[SkipLocalsInit]
private void RunCore(ref RunState state)
{
ReloadFrame:
    var fp = state.Fp;
    <derive currentFunc/bytecode/registerRef/objectPool from fp>
    var startPc = state.StartPc; state.StartPc = 0;
    var acc = state.Acc;                     // A21 win preserved: local
    ref var nextCheck = ref <local copy of countdown>;   // local, synced below
    while (true)
    {
        <shared arm locals (unchanged)>
        NextOp:
        state.OpcodePc = ref pc;             // ONE store, replaces the
                                             // EH-live store; dead null-init
                                             // store disappears
        var op = (JsOpCode)pc;
        pc = ref Unsafe.Add(ref pc, 1);
        if (--nextCheck == 0)
        {
            this.acc = acc;
            var opcodePc = state.OpcodePc;   // slow path only: sync both ways
            CheckExecutionSlowPath(fullStack, fp, ref bytecode, ref opcodePc, op, ref nextCheck);
            state.OpcodePc = opcodePc; pc = ref opcodePc;
            acc = this.acc;
        }
        switch (op) { ... }                  // arms unchanged
        operandScale = Single;
    }
    // exit paths (host Return): state.Acc = acc; state.NextCheck = nextCheck;
}
```

### RunState (stack ref struct, ref fields)

```csharp
private ref struct RunState
{
    public ref byte OpcodePc;   // faulting opcode pc; written per dispatch,
                                // read by the catch (8-byte store, replaces
                                // today's EH-live store 1:1)
    public int Fp;              // written at ReloadFrame and by the catch
    public int StopAtCallerFp;
    public int StartPc;         // handler target set by the catch
    public JsValue Acc;         // in/out across RunCore invocations
    public int NextCheck;       // persisted countdown
}
```

Ref fields in a stack ref struct are GC-tracked interior pointers - the
bytecode array stays reachable through other refs, and the catch may run a
GC during exception dispatch safely.

### Boundary contract

| state      | writer (hot)        | writer (cold)         | reader        |
| ---------- | ------------------- | --------------------- | ------------- |
| OpcodePc   | per dispatch (1 st) | CheckExecutionSlowPath (slow) | catch |
| Fp         | ReloadFrame (per call) | catch (JS unwind)  | RunCore entry, catch |
| StartPc    | -                   | catch (handler pc)    | RunCore ReloadFrame |
| Acc        | RunCore clean exit  | catch (thrown value)  | RunCore ReloadFrame, finally |
| NextCheck  | RunCore clean exit  | -                     | RunCore entry |

Per-dispatch delta vs today: **zero new stores**. Today's edge stores the
EH-live `opcodePc` pointer + the dead null-init store; after the split it
stores `state.OpcodePc = ref pc` once. The dead store disappears and the
EH clause (funclet, EH-live analysis, spill constraints) disappears from
the hot method.

Fault-time `acc` intentionally does NOT cross the boundary (verified: no
consumer). This keeps `acc` a fully local enregistered value in RunCore -
the A21 win is untouched.

### What intentionally does not change

- `ReloadFrame` body, handler tables, `PushTry`/`PopTry`, generator
  dispatch (`GeneratorDispatchResult.ReloadFrame` stays inside RunCore).
- Debugger/checkpoint semantics: `CheckExecutionSlowPath` still receives
  the CURRENT opcode pc byref (slow-path-only two-way sync, rare).
- A21 accumulator publication points (before re-entrant calls, at
  checkpoints, at exit) - all inside RunCore already.
- `managedRunDepth` accounting (Run only; RunCore adds no depth).
- Tiering/PGO behavior of the probe harness (measure under pgo-off and
  tiered-off regardless).

## 4. Alternatives considered and rejected

- **B: chunked RunCore** (N opcodes per call, state ping-pong): adds
  per-chunk call overhead into the hot path, complicates OSR/tiering, and
  still needs the same state struct. Rejected.
- **C: no-throw core** (every opcode/helper returns a result status,
  JS-level try/catch simulated by manual frame walking): hundreds of
  throwing helper call sites to convert; per-opcode status branching is
  worse than the EH. Rejected.
- **D: narrower EH region inside the loop**: measured IL no-op (A6).
  Rejected.
- **E: throw-carrying exceptions** (exceptions carry the faulting pc so
  the state store can be dropped): exceptions are already slow paths, but
  every throwing helper signature changes and the per-dispatch store is
  not actually cheaper to remove than to keep. Note as a possible future
  follow-up if the store shows up in profiles; not part of this design.

## 5. Hypotheses and measurement plan

Hypotheses (each needs isolated verification):

1. The dead per-dispatch null-init store disappears (asm diff, A25.2 for
   free).
2. EH-live analysis relief improves enregistration/spills in the core -
   expect smaller Tier1 code and/or fewer spills; magnitude unknown
   (1-3% is plausible, zero possible).
3. The `op` spill may disappear (A25.3) if the cold resume readers no
   longer need EH liveness.
4. Funclet machinery disappears from the core listing.

Plan: one attempt, `capture-jit.ps1` pgo-off + tiered-off on
smi-sum-loop/dromaeo-3d-cube-modern, `compare-jit.ps1`/`analyze-jit.ps1`
(dispatch-edge + spill diff), then bench-ab medians on
dromaeo/stopwatch/smi/named-get/for-loop-sum. Full suite + Test262
`language` category are the correctness gate (exception-heavy coverage:
statements/try, expressions/throw included).

### Measured intermediate step (2026-08-28): outer try/finally elimination alone - REJECTED

Removing only the outer try/finally (explicit End-epilogue exits +
handler-owned cleanup in `TryCatchRunCoreException`, exactly as section 3
specifies) was implemented and measured standalone: full suite green,
dispatch edge byte-identical in asm, Tier1 -58 B - but bench-ab
REPRODUCED regressions (smi +13-14%, dromaeo +2-8% across two runs).
A same-code self-test established the noise floor at +/-0.9%, so the
regression is real JIT layout perturbation, not measurement noise.
Conclusion: the restructure must land as the full RunCore move (where
layout re-rolls anyway), not as a standalone finally removal. The
explicit-exit design carries over unchanged.

## 6. Risks

- **Zero gain possible**: the giant-method problem remains (RunCore is
  still ~22KB); the EH relief may not translate into better enregistration.
  Accept only on bench-ab medians.
- **State struct access cost**: the JIT must enregister the `state`
  parameter (byref - standard) and the per-dispatch ref-field store must
  not be hoisted/removed incorrectly (it cannot - the field escapes via
  the exception path, and RyuJIT treats ref-field stores as observable).
- **Exception-path GC**: RunCore frame locals are GC-reported at the
  throw point during unwind (standard requirement, unchanged from today's
  funclet behavior).
- **Profiler build**: the `OKOJO_VM_PROFILE` counters move into RunCore -
  keep the pair-state reset per frame entry.

## 7. Follow-ups enabled by this change

- A25 items 2-3 largely resolve themselves; re-measure before claiming.
- Hot/cold arm splitting (A2-style) becomes cheaper to iterate: no EH
  region boundary to preserve.
- If the remaining `state.OpcodePc` store ever shows in profiles, revisit
  alternative E (pc carried by throwing helpers).
