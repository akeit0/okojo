# VM Optimization Insights - Cumulative Knowledge Base

Companion to `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` (workflow/backlog)
and `OKOJO_A8_A9_RESEARCH.md` (bytecode/compiler research). This collects the
durable technical insights produced by the optimization campaign so far,
organized by layer. Each entry states the finding, the evidence that
established it, and where it applies. Nothing here should be re-derived from
scratch; challenge entries only with new measurements.

Machine/runtime context for all numbers unless stated otherwise:
win-x64, .NET 10.0.11 Release RyuJIT, i7-13700F.

## 1. RyuJIT / codegen layer

### 1.1 Dense switches become CLUSTERED jump tables

A 153-case byte-valued switch (`switch (op)` over `JsOpCode`) does not lower
to one jump table. Observed in `Run` Tier1: range check
(`cmp ecx,152; ja DEFAULT`) feeding SIX separate table clusters
(reloc `RWD00/612/644/660/720/760`), i.e. six distinct indirect-jmp sites,
each shaped `lea table; mov r32,[table+idx*4]; lea base; add; jmp reg`.

Evidence: smi-sum-loop.pgo-off Tier1 listing (post-A10 snapshot).

Consequence: never assume "switch == one indirect branch". Count actual
`jmp reg` sites in dasm before reasoning about branch prediction.

### 1.2 More dispatch sites can be FASTER (per-site BTB target sets)

Measured twice with identical handler bodies (E1 A9probe,
tools/VmDispatchMicrobench): a cyclic 8-value stream through the 153-case
switch runs at 0.32 ns/op when values SPREAD across sub-table clusters vs
0.49 ns/op when COMPACTED into one cluster. Spread is ~35% faster.

Interpretation: each indirect-jmp site owns predictor resources. Spread
values give every site only 1-2 recurring targets (perfect prediction);
compact values make one site alternate among many targets (thrash).

Consequence: "reduce case count / merge clusters" is NOT automatically good.
For cyclic interpreter streams the multi-table split may be helping. Any
opcode-set change must be re-measured with the A9probe pattern.

### 1.3 Function-pointer dispatch loses ~3x on loop-shaped streams

E1 microbench, identical bodies, ns per dispatched opcode:

| stream              | S switch | F fptr-call    | H hybrid       |
| ------------------- | -------- | -------------- | -------------- |
| cycle (8-op repeat) | 0.50     | 1.45-1.60 (+~2x) | 1.28 (+~160%) |
| mixed (uniform rnd) | 5.70     | 5.28 (-7%)     | 6.08 (+7%)     |

Why F loses on loops: indirect CALL+RET per opcode plus state round-trips.
.NET has no interprocedural register allocation, so acc/pc cannot stay
enregistered across a call boundary - they spill to a state struct and
reload in the next handler even when the "handler" is three instructions.
H hybrid inherits the same penalty for every opcode outside its inline set;
coverage over general JS is always partial.

Why F slightly wins on random streams: both styles are dominated by
indirect-mispredict stalls there; small handlers have better I-cache
locality and decode footprint. Real JS hot loops look like `cycle`, not
`mixed`, so this does not translate to engine wins.

### 1.4 Direct/threaded dispatch is unexpressible in C#

V8 Ignition's dispatch speed comes from direct threading: each handler ends
with its own inline fetch+jump so the BTB learns (prevOpcode,nextOpcode)
pairs individually. C# has no labels-as-values and no way to emit a jump to a
computed address without going through a call. Replicated dispatch
(duplicating switches) doubles hot-arm code for partial benefit. Accept the
jump-table switch as the platform's best available lowering.

### 1.5 Large functions degrade JIT quality in specific, measurable ways

At 8468 IL bytes / 137 locals (`Run` pre-A1/A2):

- Trivial one-line accessors on the 16-byte JsValue struct were left as real
  call+ret pairs inside Run: get_IsNumber x9, get_IsInt32 x8,
  get_FastNumberValue x7, get_IsFloat64 x4, ctor(double) x12.
  Root cause: 16-byte struct `this` copies skew inline cost heuristics under
  the giant method's register pressure. Forcing AggressiveInlining removed
  all of them for ~+150B of duplicated code.
- Dispatch-critical values were spilled: the opcode lived in `[rbp-0xDC]`
  across the range check, frame refs at [rbp-0xD80..]; Run stack frame ~3.4KB.

After A1+A2 (93 locals / 7600B IL): fewer GC-report slots, less pressure,
and the accessor calls stayed eliminated. Lesson: local-count reduction and
hot/cold splitting are enablers - they make other JIT optimizations stick.

### 1.6 checked() emits dead overflow branches when the proof is structural

`GetPcOffset` used `checked((int)Unsafe.ByteOffset(ref bytecode, ref pc))`.
Both refs point into one managed byte[] (<2^31 elements), so the cast can
never overflow - yet checked emitted a `jo` at all ~23 inlined call sites.
Removed (A3). Contrast: host-conversion and typed-array length `checked`
math is SEMANTIC (spec requires throws) and must stay. Classification table
lives in the a3 snapshot notice.

## 2. CPU / microarchitecture layer

### 2.1 The dispatch sequence anatomy (current, post-A10)

Per opcode the Tier1 dispatch executes roughly:

```
mov  eax, [rbp-0xDC]        ; opcode from stack slot
cmp  eax, 152               ; range check per cluster
ja   DEFAULT
lea  rcx, [table]           ; cluster table
mov  ecx, [rcx+rax*4]       ; rel32 offset
lea  rdx, BASE
add  rcx, rdx
jmp  rcx                    ; indirect jump
```

~7 uops, two L1 loads, one indirect branch. With six clusters, a hot loop's
opcodes distribute across sites; E1 showed this distribution is beneficial
for cyclic streams (see 1.2).

### 2.2 dec+jz countdown checks are free

The execution-interrupt check (`--nextCheck == 0` before every dispatch)
costs nothing measurable: with the check ENTIRELY REMOVED (illegal,
ceiling-measurement only) the most dispatch-heavy case improved just -0.4%,
inside noise. The decrement is off any dependency chain and the branch
predicts perfectly (almost never taken). Do not attempt to fuse, widen, or
relocate it. Additionally, relocating it would break debugger checkpoints:
the slow path receives the CURRENT opcode pc and stepping semantics depend
on that.

Methodology: ceiling measurement - disable the feature completely and
measure the maximum possible win BEFORE designing any clever version. If the
ceiling is noise, stop.

### 2.3 Exception-handling region scope was never per-iteration

`while (true) { try { body } catch { } }` compiles to ONE IL EH clause
covering the whole loop body. Restructuring to `try { while ... } catch`
changed IL by +3 bytes and timing by nothing. EH-scope narrowing for this
loop shape has no headroom; cold throwing arms leaving the loop entirely
(A2-style) is the only EH-adjacent win observed.

## 3. C# language / runtime pitfalls (engine-specific)

### 3.1 ref-reassignment does not propagate to callers (A2 bug)

`pc = ref Unsafe.Add(ref pc, n)` inside a method rebinds ONLY the callee's
ref slot. On return, the caller's ref local points where it did before. A pc
cursor cannot be advanced via through-writes either (that would overwrite
bytecode bytes). Therefore extracted opcode handlers MUST return consumed
delta; arms apply `pc = ref Unsafe.Add(ref pc, Handler(...))`.
This is why every pre-existing Handle* helper uses that convention.
Failure signature: execution resumes mid-operands, decoding operand bytes as
phantom opcodes (we saw two phantom LdaUndefined after CreateClosure).
Documented in JsRealm.VmLoop.cs above HandleCreateClosure.

### 3.2 MethodImpl attributes target accessors, not properties

`[MethodImpl(AggressiveInlining)]` cannot annotate an expression-bodied
property directly; it goes on the `get`. Verbose but required for the JsValue
tag/bit getters.

### 3.3 Unsafe.As house style

For compiler-guaranteed heap-object casts prefer `Unsafe.As<T>(obj)`
(object overload, .NET 5+): identical codegen to the byref form (plain
reference move, no castclass), simpler call sites. Byref form
`Unsafe.As<TFrom,TTo>(ref ...)` remains for raw storage reinterpretation
(JsValue <-> double, JsValue -> CallFrame overlays).
Preconditions: write down the invariant and audit ALL consumers first -
an unguarded path turns loud InvalidCastException into silent corruption.
Applied to: JsObject.Shape (guarded by !UsesDynamicNamedProperties; both
layout classes sealed), CreateClosure/CreateObjectLiteral constant-pool
slots (compiler-typed).

### 3.4 Bulk replacements leave dead overloads silently

A bulk method rewrite left six void-returning v1 handlers next to their
int-returning v2 replacements - legal overloads, zero warnings, dead buggy
code shipped inside a merge. After any signature-changing refactor, grep for
the old signature shape and confirm zero leftovers before merging.

### 3.5 Stale binaries mask edits

Twice, correct edits appeared ineffective because a build was skipped or a
copy raced (AGENTS-known Okojo.dll lock issue). Rule: if behavior does not
change after an edit, rebuild --no-incremental before doubting the edit.

## 4. Measurement methodology

| rule | detail |
| ---- | ------ |
| Ceiling measurement | disable feature entirely; max possible win = go/no-go for clever designs (killed A5 at ≤0.4%) |
| bench-ab | git-worktree isolation, alternating rounds, medians; single probe runs are not decisions |
| pgo-off decides | Dynamic PGO specializes one route and can hide engine changes; compare attempts pgo-off vs same-config baseline; pgo-on is a shipping sanity check |
| dasm determinism | diffable JIT dumps are byte-identical for identical code (validated); any nonzero compare-jit diff = your change |
| tier awareness | short scripts may never reach final Tier1 in probe runs; OSR code dominates; note which listing you read |
| sub-10us cells | flip sign between processes; BDN or bench-ab medians required |
