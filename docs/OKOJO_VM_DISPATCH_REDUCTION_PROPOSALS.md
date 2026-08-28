# Okojo VM Dispatch Reduction and Arm-Level Proposals

Related documents:

- `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` - workflow, tooling commands,
  baseline constraints, and measurement rules.
- `OKOJO_VM_ATTEMPT_LOG.md` - completed attempt history (accepted V1/A21 and
  earlier attempts) and the verdict table.
- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative findings; especially 1.14
  (profile builds are evidence-only), 1.15 (accumulator-local ceiling), and
  1.16-1.17 (accepted compiler elisions).
- `OKOJO_VM_DEEP_INSPECTION_METHOD.md` - layered model and artifact recipes
  used to produce the evidence here.
- `OKOJO_A8_A9_RESEARCH.md` - static corpus research; section 1.5 records the
  no-ISA-growth policy and its revisit trigger.

This document is the SINGLE active plan for the VM loop optimization effort:
all open proposals, the consolidated backlog, and the suggested execution
order live here. Completed attempts are recorded in
`OKOJO_VM_ATTEMPT_LOG.md`; durable conclusions in
`OKOJO_VM_OPTIMIZATION_INSIGHTS.md`.

Status: ACTIVE PROPOSALS: C3-C4, A14-A15 and A17/A19 (section 4), and
V3-V8. C1-C2, V1 (A21), A18 (`SkipLocalsInit`), V2 (A22), and A16 were
accepted and are recorded in `OKOJO_VM_ATTEMPT_LOG.md` and the insights
document. Every item below is backed by dynamic opcode profiles (T2,
`--profile-opcodes`), bytecode disassembly (OkojoBytecodeTool), or per-arm
JIT analysis (`analyze-jit.ps1` + listing reads) captured on 2026-08-28.

Policy note: none of the C/V proposals require new opcodes. The fusion
evidence in section 5 is recorded for the R3-R5 revisit trigger but is
explicitly NOT proposed here.

## 1. Evidence base

### 1.1 Dynamic opcode profiles (T2 build, `--profile-opcodes`)

The pre-C1 baseline for `smi-sum-loop` executes 13 dispatches per loop
iteration:
`Star` x3, `Ldar` x3, `LdaSmiExtraWide`, `TestLessThan`, `JumpIfFalse`,
`Add`, `Inc`, `ToNumeric`, `Jump` (each x1). Top pairs: `Star -> Ldar` 2M,
`Ldar -> Star` / `Ldar -> Add` / `Ldar -> ToNumeric` /
`ToNumeric -> Inc` / `Inc -> Star` / `Add -> Star` 1M each.

The pre-C1 checked two-property `named-get` case executes 17 per iteration,
dominated by `Star` x5 and the repeated triple
`Star -> LdaNamedProperty -> Add -> Star` (2M pairs each per probe).

`stopwatch-modern` (~1.56M inner iterations per probe): `Star` 33.5M
(~21/iteration), `Ldar` 10.1M, `LdaGlobal` 6.3M (~4/iter),
`JumpIfFalse` 6.1M, `LdaTheHole` 5.9M (~3.8/iter), `LdaUndefined` 5.7M
(~3.7/iter), `LdaNamedProperty` 5.5M, `LdaZero` 4.1M,
`ModSmi`/`TestEqual` 4.1M each, `ToNumeric`/`Inc` 1.96M each.

### 1.2 Bytecode disassembly (OkojoBytecodeTool)

After C1-C2, `smiSumLoop` emits the compound body as `Ldar r1 / Add r0 /
Star r0` and the update as `Ldar r1 / Inc / Star r1`.

V8 reference for the identical source: `Ldar r1 / Add r0 / Star r0` and
`Ldar r1 / Inc / Star r1` (no temp copy, no `ToNumeric`).

`namedGet` body after C1 is `LdaNamedProperty / Add r1 / Star r1` for each
`s += o.x`; C2 removes `ToNumeric` from its loop update as well.

`stopwatch-modern` `<script>` inner loop additionally shows:

```
0073  LdaTheHole / Star r3     ; z   <- all three initialized before
0076  LdaTheHole / Star r4     ; ms     any possible read; TDZ writes
0079  LdaTheHole / Star r5     ; rn     are dead (C3)
...
0089  LdaUndefined / Star r0   ; completion-value reset (C4)
...
0094  ModSmi imm:2 / Star r6 / LdaZero / TestEqual r6 / JumpIfFalse
```

`r0` is the script completion register; `LdaUndefined / Star r0` resets and
post-call `Star r0` completion writes occur several times per iteration.

### 1.3 Per-arm JIT evidence (tiered-off FullOpts listing, snapshot
`20260828-112717-0008-current-asm`, via `analyze-jit.ps1` + listing reads)

- **`Star` arm (IG497) and `Ldar` arm (IG504) each end in
  `movsq` + `call CORINFO_HELP_ASSIGN_BYREF`** - a checked-write-barrier
  helper call on every execution of the two hottest opcodes (~30% of the
  dispatch stream). Cause: `Unsafe.Add(ref registerRef, reg) = acc` is a
  16-byte struct copy through a byref, and `JsValue.Obj` is a GC ref.
- The same listing proves null stores skip the barrier: the
  `LdaSmiExtraWide` arm (IG536) writes `Obj = null` as
  `xor rdx, rdx / mov gword ptr [rax+0x08], rdx` - plain stores, no helper.
- **Fused-arm inner re-dispatch on hot ops**: `Star`/`StarWide` share an arm
  opening with `cmp edx, 150 / jne`; `Inc`/`Dec` open with `cmp edx, 63`;
  the `Ldar` family opens with a `lea eax, [rdx-0x94] / cmp eax, 1` range
  test. The A14 de-fusion rationale applies beyond arithmetic.
- **Residual operand-scale stack traffic after A13**: the `Add` and
  `TestLessThan` arm entries still execute
  `xor r8d / mov [rbp-0x78], r8d / mov r9d, [rbp-0x5C] / cmp r9d, 1 / je`
  before the fast decode - `operandScale` and `operandOffset` are
  stack-homed because the cold `ReadScaledUnsignedOperandSlow` call takes
  them by ref, and the cold path spills/reloads eight-plus values.
- The `Inc` arm reloads and re-masks the accumulator
  (`and rdx, qword ptr [rax]` twice off the same address) - the A15
  aliasing pattern, still present.
- The `ToNumeric` arm materializes a bool (`setne dl / movzx / test / jne`)
  instead of branching directly on the compare.
- `[rbp-0x338]` (the spilled `acc` byref home) appears in nearly every arm
  in the analyzer table - the cost the A20/A21 accumulator-local work
  removes globally.
- **Dispatch edge (IG07/IG08)**: besides the opcode fetch and the
  countdown read-modify-write (A5: rejected, ceiling <=0.4%), every
  dispatch executes four loop-invariant stack reloads plus four
  bookkeeping stack stores: `mov dword ptr [rbp-0x5C], 1` (the
  `operandScale` reset on the post-switch join), a **dead**
  `xor rdx / mov bword ptr [rbp-0x370], rdx` (the `opcodePc` null-ref
  init, immediately overwritten but not eliminated because the local is
  EH-live), `mov bword ptr [rbp-0x370], r14` (the live `opcodePc = ref pc`
  store), and `mov dword ptr [rbp-0x8C], edx` (the `op` spill kept so cold
  resume paths - `CheckExecutionSlowPath`, `ReadScaledUnsignedOperandSlow`
  returns - can reload it).
- **`LdaNamedProperty` arm entry (IG425-431)**: the narrow/wide
  distinction is computed with `sete`, spilled to `[rbp-0x88]`, reloaded,
  and re-tested three times before the IC body starts - ~8 wasted
  instructions per execution of the hottest IC opcode (test262 rank 4,
  4.83% of the stream; 5.5M/probe in stopwatch).
- **`LdaGlobal` arm (IG466-469)**: per execution it re-derives the
  global-IC entry base through a three-load chain
  (frame function `[rbp-0x58]` -> script `+0x58` -> IC entries `+0x60`)
  and performs two array bounds checks (name index and IC slot), even
  though the base pointers are frame-invariant. `LdaGlobal` is test262
  rank 2 (5.4%) and 6.3M/probe in stopwatch.
- **Prologue (IG01)**: frame is `sub rsp, 0x4B8` (1,208 bytes) with an
  init-locals zeroing loop over ~1.1KB (three 16-byte `vmovdqa` per
  48-byte step) - paid on every `Run` entry, which matters for re-entrant
  workloads (accessor invocations, host->JS callbacks, generator drives),
  not for loop-dominated cases. Tracked as A18; the A21 accumulator local
  already shrank the ceiling frame to 872 bytes.
- Checked and already optimal (recorded to avoid re-investigation):
  bytecode frame entry does NOT clear the callee register window
  (`PrepareBytecodeRegisterWindow(..., clearUnusedRegisters: false)`), so
  per-call frame setup carries no hidden zeroing cost.

### 1.4 Dump findings behind the arithmetic/operand experiments (F1-F6)

Source: tiered-off `Run` listing
`artifacts/vmloopopt/snapshots/20260828-112717-0008-current-asm/jit/smi-sum-loop.tiered-off.direct.jit.txt`.
These are concrete assembly signatures; each is a hypothesis until its
isolated bench-ab median and same-config assembly diff land.

1. **Entry zeroing (F1):** `init_locals=True` plus the `0x4B8`-byte frame
   emits a prologue clear loop (`mov rax,-0x420`, three `vmovdqa` stores,
   `add rax,48`, `jne`). About 1.1KB is cleared on every `Run` entry.
   Mostly irrelevant to a single long-running loop, but matters for
   accessor getters, `InvokeFunction` re-entry, and generator drives.
   ADDRESSED: removed by `[SkipLocalsInit]` (A18 accepted, see the attempt
   log and insights 1.19).
2. **Accumulator indirection (F2):** arms repeatedly reload
   `mov rax,bword ptr [rbp-0x338]` for the spilled `&this.acc` and then
   dereference it. Numeric results also use a `vucomisd` self-compare,
   conditional NaN canonicalization, and a second store clearing `Obj`; a
   single float add therefore pays `vucomisd`, two branches, a pointer
   reload, and two stores before the next dispatch. ADDRESSED: the pointer
   reloads were removed by the A21 local accumulator, and the
   canonicalization + Obj-clearing store by A16's in-place numeric result
   writes (see the attempt log).
3. **Arithmetic re-dispatch (F3):** the fused arm compares `op` again with
   `cmp edx,59` (`Add`) and `cmp edx,60` (`Sub`), then uses the `RWD776`
   secondary table for `Div`/`Mod`/`Exp` (IG293-IG297). The int-plus-int
   path has its own `cmp edx,59/60/68` chain (IG312-IG316). The mixed path
   pays this after the accumulator overflows to Float64.
4. **Aliasing-blocked CSE (F4):** the mixed path performs two back-to-back
   `and r9,qword ptr [rax]` mask tests in IG284. The `IsFloat64`/`IsInt32`
   reads go through a byref, so RyuJIT does not CSE them across possible
   aliasing.
5. **Cloned slow tails (F5):** `HandleArithmeticNonNumberSlowPath` is
   emitted along IG280-IG283, IG285-IG287, and IG289-IG291, each with a
   private 16-byte temporary (`[rbp-0x1C0]`, `-0x1D0`, `-0x1E0`). This is
   code-size and frame pressure from one source-level tail reached through
   three flows.
6. **Wide overflow math (F6):** int-plus-int uses sign extension, a 64-bit
   add, and two range comparisons instead of a 32-bit overflow test.

Non-goal: the `opcodePc`/`op` spills at `[rbp-0x370]`/`[rbp-0x8C]` are
EH-liveness-forced because the catch reads `opcodePc` and arithmetic arms
read `op`. P1 can reduce `op` readers, but the spills are not a target while
the catch needs that cursor.

## 2. Compiler proposals (bytecode emission; no ISA change)

### C3. TDZ hole-init elision for block lexicals

Function-top-level lexicals already skip dead hole-initialization
(`smiSumLoop` emits `LdaZero / Star` directly), but block-scoped lexicals
inside loops do not: the `stopwatch-modern` inner loop hole-initializes
three consts per iteration (5.9M `LdaTheHole` + paired `Star`s,
~7.5 dispatches/iteration) that are all definitely assigned before any
possible read.

- Gate: declaration with initializer; no closure is created between block
  entry and the initialization; no read of the binding can precede the
  initializer (straight-line dominance is sufficient for the common case).
- Verification: stopwatch disasm shows no `LdaTheHole` in the loop body;
  TDZ regression tests (read-before-init inside the block must still
  throw, including via closures created after the read site); full suite +
  test262 `let`/`const` sweeps.

### C4. Completion-value write elision for script/eval units

Script units thread a completion register through every statement:
`LdaUndefined / Star rC` resets before statements and `Star rC` after each
expression statement, inside hot loops. `stopwatch-modern` pays ~6-8
dispatches/iteration for this (5.7M `LdaUndefined` plus a large share of
the 33.5M `Star`s), and the suite benchmarks execute script units, so the
2.7x stopwatch gap includes this cost on every iteration.

V8's bytecode generator performs completion-value elision (statements are
compiled in value/effect modes and only positions whose completion can
become the script result keep the register traffic). Port that analysis:
compile statements in effect mode unless the statement's completion value
can propagate to the unit result.

- This is the largest single stream reduction for `stopwatch-modern` but
  needs its own feature note (completion semantics of loops/ifs/breaks are
  subtle - ECMA-262 "Updating Empty" rules). Reference recipe:
  `node --print-bytecode` on the same script shapes.
- Verification: `eval`-completion test262 coverage
  (`language/statements/*/cptn-*`), suite disasm before/after, bench-ab on
  the suite cases.

## 3. VM arm proposals (JsRealm.VmLoop.cs; no ISA change)

### V1 (accepted A21). Accumulator-local implementation - ACCEPTED, moved

Accepted and recorded in `OKOJO_VM_ATTEMPT_LOG.md` (A21/V1) and insights
1.15/1.18. Residual note for the proposals below: with acc as a stack local,
the `Ldar` copy destination is a plain local store (no write barrier), so V2
only needs to handle the `Star` direction, and the `[rbp-0x338]` pointer
reloads disappeared from every arm.

### V2 (accepted A22). Star/Mov write-barrier elimination - ACCEPTED, moved

Accepted together with A16 and recorded in `OKOJO_VM_ATTEMPT_LOG.md`.
Implementation note retained for the backlog: `JsValue.CopyValueTo` writes
ref-free values through a scalar `ulong`-typed byref plus a raw zero store
for the null-Obj half; a mutable overlay struct is NOT usable (CoreCLR
reorders GC-reference fields first regardless of Sequential layout -
insights 3.9).

### V3 (backlog A23). Hot-arm de-fusion beyond arithmetic

A14 planned de-fusing `Add`/`Sub`/`Mul`; the listing shows the same inner
re-dispatch on hotter arms:

- `Star`/`StarWide`: `cmp edx, 150 / jne` per execution (22-30% of all
  dispatch). Wide forms are cold (compiler emits them only past 255
  registers) - split them out.
- `Ldar`/`LdarWide`/`LdaLexicalLocal`/`LdaLexicalLocalWide`: range test +
  two conditional operand widths + a hole-check branch that plain `Ldar`
  never needs. Give `Ldar` a minimal dedicated arm.
- `Inc`/`Dec`: `cmp edx, 63` to select the delta; two trivial dedicated
  arms with a constant delta each.
- `LdaNamedProperty`/`LdaNamedPropertyWide`: the arm entry materializes,
  spills, reloads, and triple-tests the wide-form bool (IG425-431 in the
  evidence) before the IC starts. The wide form is compiler-cold; a
  dedicated narrow arm removes ~8 instructions per execution of the
  hottest IC opcode.
- `TestEqual`/`TestNotEqual`/`TestEqualStrict`: one arm, generic scaled
  decode, an `op switch` re-dispatch, and an unconditional helper call
  (`AbstractEquals`/`StrictEquals`) with no inline numeric path -
  4.1M executions per stopwatch probe from `x % k == 0` chains. Split the
  three, use narrow decode, and inline the both-int32 compare before
  falling back to the helper.

Dispatch-table shape is unchanged (each opcode already owns a table
entry), so the A7/A9 clustering findings are not disturbed. Verify per arm
with `analyze-jit.ps1 -ComparePath` (the re-dispatch `cmp` disappears, no
smearing into unrelated arms) and bench-ab.

### V4 (backlog A24). Residual operand-scale stack traffic

Post-A13, hot two-operand arms still zero `operandOffset` into
`[rbp-0x78]` and reload `operandScale` from `[rbp-0x5C]` on every
execution, because both locals are pinned to the stack by the by-ref
signature of the cold reader and (for the scale) liveness across the EH
region. Restructure so the scale-1 fast path never touches them:

```csharp
// hot arms, scale==1 guaranteed narrow layout:
if (operandScale == 1) { reg = pc; slot = Unsafe.Add(ref pc, 1); pc += 2; }
else { DecodeWideOperands(...); } // NoInlining, no refs to hot locals
```

The cold helper must take its inputs by value and return a packed result
(or re-read from `pc`) so no hot local is address-taken. Verify: the
`xor/mov/cmp` prologue disappears from `Add`/`TestLessThan`/`TestEqual`
arm entries; `run-locals.txt` local count stable; bench-ab.

### V5. Arm micro-audit follow-ups (fold into A15/A16 passes)

- `Inc` re-masks the accumulator twice through the byref
  (`and rdx, qword ptr [rax]` x2): copy `acc.U` to a local before the tag
  tests (A15 pattern; mostly obsoleted by V1, re-check after).
- `ToNumeric` materializes `setne dl / movzx / test / jne` instead of a
  direct branch; restructure the source condition so the JIT emits one
  conditional jump.

### V6 (backlog A25). Dispatch-edge store diet

The dispatch edge executes four bookkeeping stack stores per opcode
(section 1.3). Three independent experiments, each one attempt:

1. **Move the `operandScale` reset off the common edge.** Only the
   `Wide`/`ExtraWide` prefix arms ever change the scale, so let the
   post-wide-op path jump to a small "reset scale, then NextOp" stub while
   the ~100% common narrow path jumps straight to the fetch. Saves one
   store per dispatch. Risk: a second join target changes layout/BTB
   behavior - exactly the effect class insights 1.2 warns about; accept
   only on bench-ab.
2. **Kill the dead `opcodePc` null store.** The
   `ref var opcodePc = ref Unsafe.NullRef<byte>()` per-frame init is
   re-emitted on the per-dispatch join because the local is EH-live and
   the JIT will not dead-store it. Restructure the declaration/init so no
   null store lands on the hot join (e.g. initialize `opcodePc = ref pc`
   once at frame reload; every `NextOp` immediately overwrites it anyway).
   Verify in the diff that the `xor + mov [rbp-0x370]` pair disappears
   and the catch path still resolves the faulting pc.
3. **Re-derive `op` in cold resume paths.** The `[rbp-0x8C]` spill exists
   so `CheckExecutionSlowPath` and the wide-decode cold paths can reload
   `op` after their calls. Re-reading `(JsOpCode)opcodePc` in those cold
   paths instead removes the hot-edge store; cold paths pay one extra
   load only when they actually run.

Calibration: A5 measured the whole countdown RMW at <=0.4%, so each single
store here is small; the three together are plausibly 0.5-1% on
dispatch-bound cases. Cheap attempts, but accept strictly on medians.

### V7 (backlog A26). Frame-scoped global-IC base caching

Give `LdaGlobal`/`StaGlobal` the same treatment `registerRef` and the
named-IC arrays already get: derive the global-IC entries base (and the
name-atom table base) once per frame at `ReloadFrame`, keep them in
dedicated locals, and drop the per-execution three-load chain. Fold in an
A3-class removal of the name-index bounds check (operands are
compiler-validated against the atom table at emit time).

- Evidence: IG466-469 (section 1.3); `LdaGlobal` is 5.4% of the
  test262-wide stream and ~4/iteration in the stopwatch inner loop.
- Risk: two more live-across-dispatch locals raise register pressure in a
  method that already spills loop state; watch the frame size and the
  unrelated-arm diff (insights 1.11), and be prepared to trade this
  against A21's frame shrink.
- Verify: arm instruction count via `analyze-jit.ps1`, bench-ab on
  stopwatch + a global-heavy microcase (add one to
  `benchmarks/Okojo.Benchmarks/scripts/` if none isolates `LdaGlobal`).

### V8. Call-path investigation (next evidence target, not yet a proposal)

`stopwatch-modern` executes 1.58M `CallProperty` + 857k `Construct` per
probe and remains 2.7x behind Jint after the C/V items above are
accounted for; `math-call` ties Jint on host-call overhead. The frame
enter path is already lean on the checked axes (no register-window
clearing), so the next step is attribution, not guessing: use
`VmLoopIlMap` + the sampled-heatmap chain (deep-inspection method §5.3)
on `stopwatch-modern` to split time between the call arms,
`TryDispatchVmStackInvocation`, argument-window setup, `Construct`
machinery, and the `Date` host constructor, then write the follow-up
proposals against that profile.

## 4. Arithmetic and operand experiments (backlog A14-A19)

Dump-driven experiments from section 1.4; one hypothesis per attempt, with
isolated bench-ab medians and same-config assembly diffs before acceptance.

- **A14 (P1) - arithmetic de-fusion:** give `Add`, `Sub`, and `Mul` separate
  arms (F3). The top-level `RWD00` table already has separate entries, so
  the dispatch target set and BTB shape should remain unchanged; only the
  arm bodies specialize. Expected effect: remove 1-3 inner compares and a
  second indirect jump on the mixed path, and potentially collapse F5's
  cloned slow tails.
- **A15 (P2) - operand snapshots:** read `acc` and `slotRef` into locals
  once before multi-testing their tags (F4). A 16-byte `JsValue` local
  whose `Obj` half is unused on the numeric path may promote to one GPR
  and remove the aliasing barrier. (Partially addressed by A16: numeric
  results are written in place, so the result-side tag tests are gone.)
- **A17 (P4) - 32-bit overflow:** compare `int r = a + b` with
  `((a ^ r) & (b ^ r)) < 0` (or the smaller `(int)res == res` form)
  against current semantics (F6). Tiny innermost-loop experiment; needs
  exact Smi-to-Float64 promotion tests.
- **A19 (P6) - three-operand superinstructions (DEFERRED):** after T2 pair
  frequencies and the A21 headroom result, fuse patterns such as
  `Ldar rA; Add rB; Star rC` into `AddRR rA,rB -> rC`, bypassing the
  accumulator and two dispatches. This follows the register-machine shape
  used by LuaJIT/JSC; V8 Ignition avoids the same cost with a physical
  accumulator that the current C# loop cannot provide per dynamic opcode.
  Adding bytecode entries changes the BTB target set, so re-check the
  dispatch evidence (insights 1.2). Deferred until A14-A15/A17 results
  justify an opcode-contract change.

(A16 / P3, integer numeric canonicalization with in-place result writes,
was accepted and moved to `OKOJO_VM_ATTEMPT_LOG.md`; the `BoxMask` idea
from the original proposal was refined to a full NaN predicate
`(bits & 0x7FFF...) > 0x7FF0...` because `BoxMask` misses signaling NaNs.
A18 / P5, the `SkipLocalsInit` entry-clear probe for F1, was also accepted
and moved.)

## 5. Fusion revisit-trigger evidence (recorded, not proposed)

R3-R5 were closed with the trigger "a specific adjacent pair dominating
real workload time AND explicit owner approval". The T2 dynamic pair data
now exists:

| pair | case | count / probe | share of stream |
| ---- | ---- | ------------- | --------------- |
| `Star -> Ldar` | smi-sum-loop | 2M | 15.4% |
| `TestLessThan -> JumpIfFalse` | every loop head | 1M (1/iter) | 7.7% |
| `Add -> Star`, `Inc -> Star` | smi-sum-loop | 1M each | 7.7% each |
| `Star -> LdaNamedProperty`, `LdaNamedProperty -> Add` | named-get | 2M each | 15.4% each |
| `ModSmi -> Star -> LdaZero -> TestEqual` (4-chain) | stopwatch-modern | 4.1M chains | ~15% of inner-loop stream |

The `ModSmi` chain is the strongest single-shape candidate: `x % k == 0`
costs five dispatches because no compare-accumulator-with-immediate form
exists; a `TestEqualSmi imm` family would collapse it to two. Recorded for
the policy call only.

C1-C2 removed several of these pairs without ISA growth. Re-collect this table
after the remaining compiler proposals land; only the residual table is
decision-grade input for the R3-R5 policy call.

## 6. Tooling gaps found while collecting this evidence

- `VmLoopProbe` cannot run the dromaeo suite cases: they crash on the
  missing `startTest` harness global (CLR exit 0xE0434352). A built-in
  no-op stub set (`startTest`/`test`/`endTest`/`prep` as the benchmark
  host defines them) would open the 1.7x-loss `dromaeo-3d-cube-modern`
  case to opcode profiling and probe timing.
- The T2 profiler and `analyze-jit.ps1` both worked as designed; every
  finding above localized in one probe or one analyzer pass.

## 7. Consolidated backlog

All open work items in one table. Completed items live in
`OKOJO_VM_ATTEMPT_LOG.md`; do not duplicate them here.

| ID | Item | Status | Detail |
| -- | ---- | ------ | ------ |
| A8 | Per-op implementation changes (smi fast paths etc.) | open | V8/Node reference observations per AGENTS tooling rules |
| A9 | Opcode set streamlining | open | compiler-contract change; needs OkojoBytecodeTool evidence first |
| A11 | Tree-walk interpreter alternative | open (last resort) | only if the bytecode path plateaus; requires its own feature note |
| A14 | Arithmetic arm de-fusion | PLANNED | section 4 (P1) |
| A15 | Operand snapshots before tag tests | PLANNED | section 4 (P2) |
| A17 | 32-bit Smi overflow check | PLANNED | section 4 (P4) |
| A19 | Three-operand arithmetic superinstructions | DEFERRED | section 4 (P6) |
| A23 | Hot-arm de-fusion beyond arithmetic | PROPOSED | V3 (extends A14) |
| A24 | Residual operand-scale stack traffic | PROPOSED | V4 |
| A25 | Dispatch-edge store diet | PROPOSED | V6 |
| A26 | Frame-scoped global-IC base caching | PROPOSED | V7 |
| C3 | Block-lexical TDZ hole-init elision | PROPOSED | section 2 |
| C4 | Completion-value write elision | PROPOSED | section 2 |
| V8 | Call-path attribution investigation | next evidence target | section 3 |

## 8. Suggested order

1. C3 (block-lexical TDZ elision; `stopwatch-modern` has direct evidence).
2. V3 `TestEqual`/`LdaNamedProperty`/`Star` de-fusion (small VM patches
   with direct arm-level acceptance criteria; the Star-side barrier part
   of the original plan is already covered by the accepted V2).
3. A14-A15 and A17 (arithmetic/operand experiments from the F1-F6 dump
   findings).
4. V4, V5, V6 (after V1, since it changes the acc addressing and frame
   pressure they interact with).
5. V7 global-IC base caching (after V1 frees frame budget).
6. C4 (needs its own feature note before implementation).
7. V8 call-path attribution run; write the next proposal batch from it.
8. Re-collect the section 5 pair table; owner decides on R3-R5 revisit.

Each item follows the standing workflow: one attempt per change, first compare
the relevant artifact (bytecode for compiler work, JIT/IL for VM work), then
run only a focused timing sanity check when the artifact changes; expand to
`bench-ab` alternating medians only when the result is ambiguous. Record the
artifact and outcome in `notice.md` + the insights entry regardless of outcome.
