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

### 1.7 C# local count is a pressure signal, not the objective

The PDB-backed `VmLoopProbe --inspect-run` report distinguishes persistent
frame/operand state from short-lived per-op declarations. Preserve the
machine-stack state (`fullStack`, `pc`, `acc`, `fp`, bytecode/register refs,
and `slotRef`); share same-type temporaries only when their lifetimes do not
overlap.

On the 20260828 attempt, `Run` changed from 98 to 44 IL locals and from
7,880 to 7,810 IL bytes. The first sharing pass made Tier1 code slightly
larger while reducing frame pressure, but short alternating A/B improved
the main smi/for/named cases by 5.5-8.8%. Therefore the acceptance order is
benchmark, generated assembly, then IL/local count. A lower local count with
slower benchmark output is a rejection.

### 1.8 Keep common operand decoding inline; share only the cold widths

`ReadScaledUnsignedOperand` originally contained single-, wide-, extra-wide,
and invalid-scale branches at every inline call site. Keeping the single-byte
read in the inline helper and routing the other cases through one
NoInlining helper reduced Tier1 by 578 bytes and Tier1-OSR by 498 bytes,
with six/seven fewer call instructions in the Run listings. The common path
kept its original byte read; only rare wide/extra-wide code pays a call.

This is the useful fast-path/cold-path split shape for this C# interpreter:
share cold implementation, do not add a call to the hot bytecode path.

### 1.9 Use immediate operands for compiler-known literal metadata

The compiler already knows the length of a non-empty array literal. Encoding
that length in a dedicated two-byte operand avoids putting an `int` in the
object pool and avoids the hot handler's repeated pool indexing and type
tests. The old pool-backed opcode remains for compatibility and routes its
rare forms through a `NoInlining` helper.

In an isolated 11-round pgo-off A/B from `27910a5`, the focused literal
workload moved from 468,227 ns to 462,843 ns (-1.1%). `Run` IL fell from
7,810 to 7,757 bytes; Tier0 code fell from 18,764 to 18,462 bytes and
Tier1-OSR from 22,281 to 22,104 bytes. The local count stayed at 44.
This is a scoped array-literal win, not evidence that adding an opcode is a
general dispatch win.

### 1.10 Decode fixed-width store operands with one width split

`StaNamedProperty` and `StaNamedPropertyWide` previously selected the width
once and then branched again for each of the two remaining operands. Decoding
all three operands inside one narrow/wide split removes those repeated
branches while preserving the existing three-byte and six-byte ABI.

In an isolated 11-round pgo-off A/B from `27910a5`, the focused named-store
workload moved from 2,821,020 ns to 2,695,137 ns (-4.5%). `Run` IL fell from
7,810 to 7,787 bytes; Tier0 code fell from 18,764 to 18,581 bytes. Tier1
and Tier1-OSR code changed by +37 and +40 bytes respectively, and the local
count stayed at 44. Treat this as a scoped decoder improvement: source-level
branch removal does not guarantee a whole-program win when it perturbs the
layout of the large `Run` method.

### 1.11 Independently good `Run` edits are not necessarily additive

The named-store and array-literal edits were each positive in their focused
workloads, but their combined pgo-off build was not a universal improvement.
One 11-round comparison moved `stopwatch-modern` by +4.5%, `array-stress` by
+4.4%, and `arith` by +1.2%, while the focused named-store result was +0.9%
relative to the named-only build. The JIT changed code placement and frame
layout even though the semantic handlers were unrelated.

Consequence: isolate each `Run` edit, commit confirmed scoped changes
separately, and re-run a stack comparison before describing a total
improvement. Benchmark medians remain the decision gate; assembly and IL
explain the result but do not override it.

### 1.12 One hot case is enough for PGO-off `Run` assembly

`JsRealm.Run` is a non-generic method whose PGO-off code generation does not
depend on which JavaScript case invokes it. Multi-case PGO-off captures showed
the same Tier0 and Tier1-OSR bodies across the cases; in the stacked snapshot,
the common sizes were 18,334 and 22,097 bytes. The final Tier1 body was also
the same for every case that reached that tier. Cases that did not show a
final Tier1 dump simply did not trigger that compilation during the probe.

Consequence: use one sufficiently hot representative case when comparing
`Run` assembly. Keep multiple cases for timing medians, semantic coverage,
checking that a desired tier is reached, or studying PGO-on/tiering behavior.
If a tier is missing, increase warmup/iterations before adding unrelated
cases.

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

### 3.6 Prototype data ICs need a cold side table; leaf calls need a strict fallback

The accepted prototype-property experiment caches only a direct
receiver-to-prototype own data property for plain objects and arrays. The
guard checks receiver shape, prototype/holder identity, holder shape, and
static data-slot flags; accessors, dynamic objects, proxies, and deeper chains
use the existing lookup path. Prototype metadata lives in a parallel feedback
array so the existing own-property IC entry remains 16 bytes. An early version
that enlarged that hot entry regressed `named-get` by 6-9%.

The accepted host-call experiment adds numeric-only leaf bodies for the hot
Math functions. Non-number arguments return to the existing host-call path,
so coercion and user-code re-entry remain unchanged.

Seven-round alternating pgo-off A/B medians against commit `5ff3c4c`:

| case | base | attempt | delta |
| ---- | ---: | ------: | ----: |
| prototype-get | 5.191 ms | 4.488 ms | -13.6% |
| math-call | 1.230 ms | 0.854 ms | -30.6% |
| named-get | 4.756 ms | 4.786 ms | +0.6% |

The pgo-on sanity run also completed without semantic failures. PGO-off
remains the stable assembly comparison; pgo-on and tiered-off expose
profile/tiering sensitivity.

### 3.7 `in`/`out` aliasing can invalidate a failed fast-path probe

Passing the accumulator as both an `in` RHS and an `out` result is only safe
when the helper leaves the result untouched on failure. The Date subtraction
probe initially wrote `default` on a failed guard, which changed generic
subtraction semantics. The internal helper now takes `ref` output and writes
only on success; the focused regression test caught the issue before the
benchmark run.

### 3.8 Do not move a rare Date fast path on source intuition alone

Moving the Date subtraction probe ahead of numeric handling and adding
object-tag guards looked cheaper in source, but the isolated pgo-off results
did not establish a stable gain: the Date-focused case was only -0.3% while
the numeric arithmetic control moved +3.9% in the same seven-round run.
The change was rejected. Keep the existing arithmetic order until a guarded
Date specialization has a stable benchmark and a clear Tier1 shape.

## 4. Measurement methodology

| rule | detail |
| ---- | ------ |
| Ceiling measurement | disable feature entirely; max possible win = go/no-go for clever designs (killed A5 at ≤0.4%) |
| bench-ab | git-worktree or working-tree isolation, alternating rounds, medians; single probe runs are not decisions |
| decision order | benchmark median first, then PGO-off Tier1/OSR assembly, then IL/local count; locals alone never accept an attempt |
| JIT configurations | PGO-off/Tier1 is the stable codegen comparison; pgo-on and tiered-off are sanity checks for profile/tiering sensitivity |
| dasm determinism | diffable JIT dumps are byte-identical for identical code (validated); any nonzero compare-jit diff = your change |
| tier awareness | short scripts may never reach final Tier1 in probe runs; OSR code dominates; note which listing you read |
| sub-10us cells | flip sign between processes; BDN or bench-ab medians required |
| Run edits | independently benchmark each source change, then benchmark the intended stack; JIT layout can defeat additive source wins |
| PGO-off Run asm | one hot representative case is sufficient; use multiple cases for timing, tier reach, or PGO-on specialization |
