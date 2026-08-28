# Okojo VM Optimization Attempt Log

Related documents:

- `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` - workflow, tooling commands, and
  the active attempt backlog.
- `OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md` - active proposals (C3-C4, V2-V8).
- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative technical findings distilled
  from the attempts below.
- `OKOJO_VM_DEEP_INSPECTION_METHOD.md` - investigation method and artifact
  recipes.

Scope: historical record of completed VM/compiler optimization attempts
(accepted, rejected, prepared, or deferred). This log is append-only history;
active plans live in the foundation and proposals documents, and durable
technical conclusions live in the insights document. Rejected attempts stay
recorded here with reasons instead of being retried silently (AGENTS.md: no
old fast-path experiments without profiling evidence).

## Attempt Log

### a2-hot-cold-split - ACCEPTED (branch vmopt-a2-hot-cold-split)

Extracted 7 cold opcode arm groups into NoInlining handlers returning the
consumed-delta (CreateClosure, CreateFunctionContext family, context-slot
families, StaGlobal family, GetNamedPropertyFromSuper, CreateObjectLiteral).

- Tier1 code size 22373 -> 21058 (-5.9%), Tier0 -10.1%.
- bench-ab (5 alternating rounds, pgo-off): for-loop-sum -4.1%,
  closure-heavy -1.6%, pure-function-call -1.2%, others within noise.
  Degradable cases show no handler-call regression.
- Full suite green. Merged --no-ff into vm-opt.

Knowledge produced by this attempt:

1. **C# ref-reassignment pitfall**: `pc = ref Unsafe.Add(ref pc, n)` inside a
   callee rebinds only the callee's ref slot; the caller's ref local is never
   reseated. A pc cursor cannot be advanced through-writes (it would
   overwrite bytecode). Extracted handlers MUST return the consumed delta and
   arms apply `pc = ref Unsafe.Add(ref pc, Handler(...))`. This is WHY the
   pre-existing Handle* helpers use that convention. Documented in
   JsRealm.VmLoop.cs above `HandleCreateClosure`.
2. **Diagnosis workflow for VM dispatch bugs** (worked end-to-end):
   probe mode fallback -> minimal repro app -> targeted `[diag]` lines in
   handler/store path -> per-dispatch `[optrace]` line comparing working vs
   broken -> raw bytecode byte dump to disprove misleading disasm listing.
   The OkojoBytecodeTool listing showed operand bytes as separate pseudo-
   instructions; raw byte dump settled it.
3. **A1 implementation hint**: reduce locals C-style - declare shared temps
   once at method top (the loop-head already does this for num/intNum/reg);
   extend that pattern rather than adding per-arm locals when extracting.

### a1-locals-diet - ACCEPTED (branch vmopt-a1-locals-diet)

C-style shared-temp conversion of cold-arm decodes (module vars, Mov,
LdaGlobal decode, rest/array/object literal indices, typed const, Smi-imm
arith) plus one shared `operandOffset` replacing 10 per-arm declarations.

- IL locals 117 -> 93 (-20%), Int32 slots 47 -> 23.
- Tier1 code size 21058 -> 20562 vs A2 (-2.4%); -8.1% cumulative vs baseline.
- bench-ab vs vm-opt: all six cases within noise; no regressions
  (pure-function-call +2.2% is inside this machine's variance band).
- Hygiene fix bundled: deleted six dead v1 handler overloads left by the A2
  bulk replace (void/ref-reseat variants were unreachable but silently
  legal as overloads). Lesson: after bulk method replacement, grep the old
  signature shape to confirm zero leftovers before merging.

### a4-inline-audit - ACCEPTED (branch vmopt-a4-inline-audit)

Dasm call-site audit: extracted every `call` target from Run's Tier1 listing
and classified them. Tiny hot accessors on the 16-byte JsValue struct were
left un-inlined by JIT heuristics (IsNumber x9, IsInt32 x8, FastNumberValue
x7, IsFloat64 x4, ctor(double) x12, IsDynamic x6, TryGetObject x2).
Forced AggressiveInlining (accessor-target for properties) removed all of
them at ~+150B Tier1 code size; bench-ab neutral; suite green.

Knowledge:

1. Audit recipe: `rg 'call.*\[Okojo' <tier1 dasm>` grouped by target -
   anything tiny and hot that still shows up is an inline candidate;
   big/slow helpers showing up confirm NoInlining is doing its job.
2. MethodImpl(AggressiveInlining) cannot annotate a property directly; it
   must go on the get accessor.
3. RyuJIT refusing 1-line struct accessors in huge methods is real; do not
   assume trivial members inline themselves under register pressure.
4. ThrowInvalidOperandScale appears as 21 cold throw-tails (one per arm with
   scale checks); harmless footprint, revisit only if code size matters.

### a5a6-dispatch-overhead - REJECTED (branch vmopt-a5a6-dispatch-overhead, preserved)

Two hypotheses killed with evidence; branch kept unmerged.

- A5 countdown placement/width: ceiling measurement (check fully disabled)
  gained only -0.4% on the dispatch-heaviest case. dec+jz is free. Moving
  the check would also break debugger checkpoint precision (slow path
  receives the current opcode's pc). Do not revisit without profiler
  evidence.
- A6 EH scope: `while { try {} catch {} }` already emits ONE IL EH region
  covering the loop; restructuring to try-around-while is an IL no-op
  (+3B IL, +-1.2% noise). There was never a per-iteration region to narrow.

Methodology note: the "ceiling measurement" (disable the feature entirely,
measure max possible win before designing any clever version) is the cheap
way to kill speculative micro-optimizations and is now standard here.

### a3-unsafe-checks - ACCEPTED (branch vmopt-a3-unsafe-checks)

Audit of overflow/bounds checks in the execution core. Removed the single
provably-dead one: `GetPcOffset`'s `checked((int)Unsafe.ByteOffset(...))`
(same-array offsets always fit int; dead `jo` at ~23 inlined call sites).
Everything else classified and recorded in the snapshot notice:

- KEEP: all semantic checked math (host conversion contracts, typed-array
  spec throws).
- SKIP deliberately: trusted-index array bounds checks in handlers - safe to
  remove only under full compiler-trust, payoff sub-noise, and raw-pointer
  access converts diagnosable exceptions into memory corruption on future
  engine bugs.

Result: Tier1 -4B, timings noise, suite green. Value = the classification
table + removed dead branch pattern.

### a10-ic-devirt - ACCEPTED (branch vmopt-a10-ic-devirt)

Removed hidden runtime type tests from the IC hot path and constant-pool
consumers. `JsObject.Shape` castclass -> Unsafe.As (invariant proven: both
layout classes sealed; !IsDynamic => exactly static layout; both Shape
callers sit behind the UsesDynamicNamedProperties guard). Closure and
object-literal handler pool casts likewise.

- named-get Tier1 ISINSTANCEOF/CHKCAST helper calls 7 -> 3 (remaining are
  genuine type tests as control flow).
- bench-ab pgo-off: named-get -0.9%, pure-function-call -2.8%, rest noise.
  pgo-on reference pass showed masking (+0.4%), confirming decisions stay
  pgo-off.

Knowledge:

1. House style for guaranteed heap casts: `Unsafe.As<T>(object)` (since
   .NET 5) - same codegen as the byref form, simpler call sites.
   The byref `Unsafe.As<TFrom,TTo>(ref ...)` remains for byref/field
   reinterpretation (e.g., JsValue <-> double).
2. Before Unsafe.As anywhere: write down the invariant + audit ALL consumers;
   an unguarded path turns loud InvalidCastException into silent corruption.
3. Virtual-dispatch scan of hot arms found none (IC predicates were already
   AggressiveInlining statics; Get/SetNamedByCachedSlotInfo non-virtual) -
   A10's win was hidden casts, not devirtualization per se.

### a12-run-local-sharing - ACCEPTED (working-tree attempt)

Added `VmLoopProbe --inspect-run` with portable-PDB source names, then
converted avoidable per-arm C# temporaries to method-loop shared locals.
Frame/operand state and the machine stack remain unchanged; stack-machine
references and already-shared locals were preserved.

- Fresh `Run` report: **98 -> 44 IL locals**, **7,880 -> 7,810 IL bytes**.
- Int32 locals: 23 -> 5; JsObject locals: 6 -> 1; SlotInfo locals: 4 -> 1;
  Boolean locals: 12 -> 4.
- Five-round short pgo-off A/B (`100` samples, `200` warmup) improved the
  main cases: smi-sum-loop -6.4%, for-loop-sum -5.5%, named-get -8.8%.
  Arithmetic and prototype/closure checks were within noise.
- A failed Date fast-path alias attempt was caught by the focused test; the
  helper now uses `ref` output and does not write on a failed guard, keeping
  accumulator/RHS semantics intact.

### a13-scaled-reader-cold-split - ACCEPTED (working-tree attempt)

`ReadScaledUnsignedOperand` keeps the `Single` byte read inline and routes
wide, extra-wide, and invalid scales to one shared NoInlining helper. This
removes repeated cold decode blocks from `Run` without changing opcode
operands or frame layout.

Compared with the fresh pre-attempt snapshot:

- Tier1: **22,503 -> 21,925 bytes** (-578); Tier1-OSR:
  **22,779 -> 22,281 bytes** (-498).
- Tier1 calls: 228 -> 222; Tier1-OSR calls: 229 -> 222.
- Tier1 stack reservation: 1,464 -> 1,272 bytes; OSR reservation changed
  992 -> 1,008 bytes.
- Final short pgo-off A/B against `HEAD`: smi -3.6%, for -11.0%, named
  -6.1%, pure-call -3.6%, prototype -1.1%; closure +1.5% (noise).
- A shorter pgo-on sanity pass showed profile sensitivity (main loops near
  neutral-to-better, named-get +4.9%, pure-call/closure much faster); it is
  not used to override the pgo-off/Tier1 decision.
- The full BenchmarkDotNet matrix was stopped because its fixed 15-case run
  was too slow for this iteration; no benchmark-project source was changed.

### t2-opcode-pair-profile - PREPARED

`OkojoVmProfile=true` adds a compile-time-gated profiler to `JsRealm.Run` and
`VmLoopProbe --profile-opcodes` prints sorted opcode and adjacent-pair rows.
Opcode counts include every fetched opcode, including width prefixes. Pair state
resets at each frame reload, excluding caller/callee boundary pairs from fusion
screening. The default build has no profile counters or dispatch branch.

Initial smoke checks:

- `smi-sum-loop` produced the expected high-volume `Star`, `Ldar`, `Add`, and
  loop-control counts plus repeated loop pairs.
- `pure-function-call` exposed `JumpIfFalse -> CallUndefinedReceiver` inside
  the caller while excluding the false `CallUndefinedReceiver -> <callee entry>`
  cross-frame pair.
- A normal build rejected `--profile-opcodes` with the explicit profile-build
  command, and the uninstrumented probe still executed normally.

### C1 compound-assignment temp elision - ACCEPTED

The compiler now emits the RHS directly into the accumulator and reads the
uncaptured current-frame local from the arithmetic operand register, removing
the compound-assignment `Ldar`/`Star` temporary sequence. The conservative
gate keeps captured, context, uninitialized, and RHS-aliasing cases on the
original ordered path.

- `smi-sum-loop`: 13 -> 11 dispatches/iteration; pgo-off A/B median -19.1%.
- The checked two-property `named-get`: 17 -> 14 dispatches/iteration;
  pgo-off A/B median -17.5%.
- Unchanged controls stayed within noise (`for-loop-sum` -0.9%,
  `lexical-block` +0.9%).
- OkojoBytecodeTool matches the V8 operand order; AssignmentTests 47/47 and
  the full suite 2,161 passed with 4 skips.
- `stopwatch-modern` has identical opcode sequences and register counts in
  all 8 baseline/current units, so its timing sample is not attributed to C1.

### C2 statement-position ToNumeric elision - ACCEPTED

The compiler now uses effect mode for expression statements when no script
completion sink is active. This lets identifier and member `Inc`/`Dec` consume
their own numeric coercion when the update result is discarded, while script
completion and value-producing updates retain the explicit `ToNumeric`.

- `smi-sum-loop`: 11 -> 10 dispatches/iteration after C1.
- `stopwatch-modern`: the script unit loses `ToNumeric` before both loop
  increments; the other 7 units are opcode-equivalent and register-equivalent.
- Focused AssignmentTests 49/49 and the full suite 2,163 passed with 4 skips.
- The opcode diff was unambiguous, so the broad timing run was stopped and no
  timing improvement is claimed for C2.

### A21 / V1 accumulator-local implementation - ACCEPTED

`Run` now copies `JsRealm.acc` into a value local before entering the EH
region and publishes it back in `finally`. Helpers that mutate accumulator
state receive `ref JsValue`; residual realm publication occurs only before
re-entrant calls/runtime helpers, debugger/constraint checkpoints, exception
routing, and method exit. No stack pointer, pinning, or `GCHandle` is used.
The local is declared before `try` so exception-safe exit publication is
explicit (insights 1.18).

Staged plan that was executed (from proposal V1):

1. Inventory every `this.acc` / `realm.acc` reader under `Execution/`
   (the ceiling build's 244 failures were the checklist: `instanceof`,
   spread, host calls, argument handling, generator drives).
2. Stage A (semantic no-op): convert helpers that can take
   `ref JsValue acc` parameters; call sites passing `ref this.acc` bind to
   the local for free. Full suite green.
3. Stage B: flip `Run` to a local `JsValue acc`, synchronizing
   `this.acc = acc` before / `acc = this.acc` after the residual escape
   arms only (call/construct/`CallRuntime`/generator/await/throw,
   catch-entry). Numeric hot arms never synchronize.
4. Acceptance: full suite + non-staging test262 sweep + bench-ab; the
   ceiling (insights 1.15) bounded the win.

Results:

- Okojo.Tests: 2,165 passed, 4 skipped.
- Non-staging Test262: 41,499 passed, 0 failed, 9,239 intentionally skipped.
- Five-round pgo-off A/B: smi -14.8%, for-loop -15.9%, Date subtraction
  -8.3%, named-get -10.2%. The noisy call control was repeated for nine
  rounds and improved 5.8%.
- Tier1: 21,927 -> 21,084 B; frame 1,208 -> 904 B; calls 218 -> 198.
  Tier1-OSR: 22,055 -> 20,872 B; frame 960 -> 624 B.
- The sweep exposed and fixed two independent correctness bugs: immediate
  compound assignments had elided their required LHS load, and failed keyed
  element probes overwrote the key through `out acc` before slow fallback.

Bonus effect visible in the arm evidence: with acc as a stack local, the
`Ldar` copy destination is a local (plain stores, no write barrier), so
proposal V2 only needs to handle the `Star` direction, and the
`[rbp-0x338]` pointer reloads disappear from every arm.

Evidence: `artifacts/vmloopopt/snapshots/20260828-164803-a21-acc-local-final/`.

### A18 / P5 `SkipLocalsInit` entry probe - ACCEPTED

`[SkipLocalsInit]` on `JsRealm.Run` removes the prologue init-locals zeroing
loop (F1, proposals doc section 1.4): ~1.1KB of frame clear per `Run` entry.
Managed-reference initialization was audited first - every ref local is
assigned before any read along all entry/resume paths (frame reload, catch
resume, cold slow-path returns), so skipping the CLR zeroing is safe here.
The attribute pays off for re-entrant workloads (accessor invocations,
host->JS callbacks, generator drives), which re-enter `Run` frequently;
loop-dominated cases never pay the prologue more than once anyway.

Results (2026-08-28, on top of A21/V1):

- Tier1 code size 21,084 -> 20,861 bytes (-223 B); the prologue clear loop
  is gone from the assembly.
- VsJintBenchmarks (BDN, pgo-off): no regressions. Improved medians beyond
  noise: arith 1.405 -> 1.240 us (-11.7%), smi-sum-loop 2,432 -> 2,229 us
  (-8.4%), lexical-block 4,979 -> 4,558 us (-8.5%), named-get 3,424 ->
  3,281 us (-4.2%), for-loop-sum 238 -> 225 us (-5.2%). Others within
  noise (closure-heavy, pure-function-call, math-call, object, many-object,
  indexing all stable or slightly better).
- Full Okojo.Tests suite passed.

Lesson: F1's ceiling estimate was conservative ("mostly irrelevant to a
single long-running loop"); the measured wins on loop-dominated cases
(arith, smi-sum-loop) show the prologue also perturbed code layout and
register allocation beyond the clear loop itself. Re-entrant entry cost
removes for free.

Evidence: working-tree attempt; numbers from the user-supplied
VsJintBenchmarks before/after run and the Tier1 code-size report.

### A22 / V2 + A16 write-barrier elimination and in-place numeric results - ACCEPTED

Joint attempt (2026-08-28) implementing proposals V2 (A22) and A16 (P3),
extended by user direction: arithmetic arms write numeric results in place
(a numeric `acc` guarantees `Obj == null`, so only the bits need updating -
same `Unsafe.As<JsValue, double>(ref acc)` idiom as the existing AddSmi
float path), skipping both the 16-byte construction and the Obj-clearing
store.

Implementation:

- `JsValue.CopyValueTo(ref dst, in src)`: null-Obj values are written as
  two plain stores (`U` via a scalar `ulong`-typed byref at offset 0, raw
  zero at offset 8 for the Obj half); ref-carrying values keep the checked
  copy. Applied to `Star`/`StarWide`, `Mov`/`MovWide`, and
  `StaLexicalLocal`/`StaLexicalLocalWide`.
- `JsValue.CanonicalizeNumericResult(double)`: full NaN predicate
  `(bits & 0x7FFF_FFFF_FFFF_FFFF) > 0x7FF0_0000_0000_0000` via
  `Unsafe.BitCast` (the original `BoxMask` idea was insufficient - it
  misses signaling NaNs, whose top 16 bits are `0x7FF0`/`0xFFF0`, not
  `0x7FF8`). Used by the generic arithmetic result, `Inc`/`Dec` float,
  `MulSmi`, `ModSmi`, and `ExpSmi`, which now update the result bits in
  place.

Arm evidence (tiered-off FullOpts; snapshots
`20260828-175121-0000-pre-v2-a16-baseline` and
`20260828-181117-0001-v2-a16-barrier-numeric`):

- `Star` arm (opcodes 18/150): hot null path is
  `mov rdx,[rbp-0x60]; mov [rcx],rdx; xor edx,edx; mov [rcx+8],edx` - zero
  calls, where the baseline executed `movsq` +
  `call CORINFO_HELP_ASSIGN_BYREF` per execution of the hottest opcode.
  Same split in `StaLexicalLocal` (19/151).
- `ExpSmi`/generic-arith results: `vmovq` + AND 0x7FFF... + CMP/JA integer
  NaN test, single in-place `vmovsd qword [acc], xmm0`; the baseline's
  `vucomisd xmm0,xmm0` self-compare, `jp`/`jne`, and separate
  Obj-clearing store are gone on those arms.
- Whole method: Tier1 20861 -> 20980 B (+119 B from the dual-path
  branches), Tier0 -207 B; calls 198 -> 198 (cold-side barriers remain);
  stack 904 B and IL locals 45 unchanged.

Tests: focused Arithmetic/Assignment/NumberPrototype 60/60; full suite
2,165 passed, 4 skipped.

Benchmark confirmation (user-supplied BDN before/after, pgo-off): no
regressions; improved beyond noise - arith 1.240 -> 1.136 us (-8.4%),
smi-sum-loop 2,229 -> 2,123 us (-4.8%), named-get 3,281 -> 3,055 us
(-6.9%), indexing 14,587 -> 13,084 us (-10.3%), for-loop-sum, closure-heavy,
math-call, pure-function-call, object, many-object within noise. All
Okojo:Jint ratios improved or held (arith 0.42 -> 0.39, indexing
0.38 -> 0.35, named-get 0.70 -> 0.63).

Bench-ab/BDN intentionally not run yet (user instruction: artifact first).
Single probe medians, non-decisional: smi-sum-loop tiered-off
2436.8 -> 2079.5 us, pgo-off 2302.7 -> 2155.8 us.

Knowledge produced:

1. **Sequential overlays with GC refs are silently reordered** - see the
   new insights 3.9. The first implementation used a mutable overlay
   struct through `Unsafe.As<JsValue, Overlay>`; CoreCLR moved the
   reference field first, the two half-stores swapped, a float bit pattern
   landed in a live GC slot, and the suite failed with unrelated-looking
   NaNs and an AccessViolation in the standalone repro. Bisecting the
   arms (revert Star -> tests pass; overlay probe -> inverted offsets)
   settled it. Scalar-typed byrefs are the only safe field-write route.

Evidence: snapshot `20260828-181117-0001-v2-a16-barrier-numeric/notice.md`.

### A14 / P1 arithmetic de-fusion (Add/Sub/Mul) - ACCEPTED

Implemented 2026-08-28 with three granularity variants compared against
HEAD (5-round bench-ab medians, pgo-off, plus FullOpts/Tier1 code size):

| variant | arith | smi-sum | for-loop-sum | named-get | Tier1 asm | IL |
|---|---|---|---|---|---|---|
| C: {Add,Sub,Mul} + {Div,Mod,Exp} fused | -1.7% | -2.3% | -0.9% | -1.1% | +622 B | +244 |
| A: Add/Sub/Mul split, DME fused (chosen) | -4.1% | -1.0% | -14.6% | -4.2% | +1,906 B | +801 |
| B: all six split | +0.5% | -1.2% | -8.7% | -3.2% | +3,164 B | +1,254 |

- B rejected: dominated by A (larger and slower; arith regressed via
  layout perturbation, insights 1.11).
- C rejected: wins near noise for its size cost.
- A accepted on user decision weighing bench against code size (user
  rule: asm/IL size is part of total performance; small bench wins alone
  do not justify landing). A's wins reproduced across two independent
  runs (arith -7.5%/-4.1%, for-loop-sum -16.5%/-14.6%,
  named-get -6.7%/-4.2%). The +1.9KB Tier1 cost is mostly duplicated
  cold/slow-path code; the hot int fast paths shrink because the
  `cmp edx,59/60/68` re-dispatch chain and inner switches are gone
  (opcodes 59/60/68 now have dedicated arm targets).

Implementation notes:

- Straight-line operand resolution duplicated per arm; NO
  `AggressiveInlining` helper - the user rule "cannot trust
  AggressiveInlining in the large Run method" (insights 1.5).
- Sub keeps its Date-subtraction check; slow-path calls pass the
  opcode as a literal for devirtualization.
- F5 (cloned slow tails) got WORSE, not collapsed: 9 slow-path call
  sites vs 3. Accepted anyway on medians; a shared cold slow-path entry
  is future work if code size matters.
- Full suite 2,165 passed, 4 skipped.

Snapshots: `0002-a14-variant-c-asm` (C), `0003-a14-variant-a-asm` (A,
accepted), `0004-a14-variant-b-asm` (B).

### A15 / P2 operand bit snapshots with shared locals - ACCEPTED

Implemented 2026-08-28: every hot arm reads `slotRef.U` / `acc.U` once into
two shared `ulong` locals (`uLhs`/`uRhs`, declared at the loop head per the
A12 C-style pattern); all tag tests and value extraction run off the raw
bits via `JsValue.TryGetNumberValueFromUlong` (verified inlined: zero call
sites) or direct `Top32Mask`/`BoxMask` tests. Register-file byref reads per
arm drop from 2-4 to one (F4 addressed). Arms converted: Add/Sub/Mul,
Div/Mod/Exp, AddSmi/SubSmi, Inc/Dec, MulSmi, ModSmi, ExpSmi, Test<>/Test<>Smi,
Bitwise/Shift (including BigInt tag checks off the bits).

Two-iteration path to the accepted shape:

1. Per-arm snapshot locals: IL locals 44 -> 57, stack +48 B, and bench-ab
   showed consistent named-get/for-loop-sum regressions. User direction:
   try locals sharing.
2. Two shared `ulong` locals (lifetimes exclusive across arms): locals 45,
   stack 888 B (=), Tier1 21,855 B (-1,031 vs A14), calls 205 (-8),
   IL 8,048 B (-299). Accepted.

Two semantic traps found by focused tests (recorded as insights 3.10/3.11):

1. A "single-OR" both-int32 test `((a|b) & Top32Mask) == JsInt32Top32Bits`
   is unsound - subset false positives (2^31 = 0x41E0..., NaN patterns)
   - caught by TestMixedNumberArithmeticAfterInt32Overflow; reverted to two
   independent register-local tests.
2. `x - imm` must not be rewritten as `x + (-imm)`: IEEE signed zeros make
   `-0.0 + 0.0 = +0.0` while `-0.0 - 0.0 = -0.0` (TestSubSmi, bytecode
   LdaZero/ToNumeric/Negate/SubSmi/Div).

Results:

- BDN (ShortRun, pgo-off) vs the user's last confirmed table (post-A22/A16,
  including A14): arith -9.8%, indexing -10.4%, named-get -9.6%,
  lexical-block -9.5%, for-loop-sum -7.1%, smi-sum-loop -5.9%, object
  -6.6%, pure-call -2.8%; math-call/many-object flat; closure-heavy +4.9%
  (inside its noise band).
- bench-ab was not decision-grade this session (for-loop-sum medians swung
  -16.5%..+12.1% across invocations, base drift ~10%; its regressions did
  not reproduce under BDN). Structural evidence plus BDN carried the
  acceptance.
- Full suite 2,165 passed, 4 skipped.

Evidence: snapshots `0005-a15-bit-snapshots` (per-arm locals variant) and
`0006-a15-shared-locals` (accepted).

### Numeric constant bit table (LdaNumericConstant direct U/Obj set) - ACCEPTED

Follow-up to A15/A16 (2026-08-28, user direction): the numeric constant
table is now `ulong[]` of raw `JsValue.U` bit patterns instead of
`double[]` - one table, BitCast at use, no parallel array. The builder
(`AddNumericConstant`) canonicalizes NaN to `JsValue.JsNan` at emission, so
`LdaNumericConstant`/`Wide` arms load bits straight into `acc` with two
plain stores and no per-execution NaN check (the fused `vucomisd xmm0,xmm0`
self-compare is gone from the arm). A NaN with top16 == BoxHdr must never
enter the table (it would alias a tagged value); emission-time
canonicalization enforces this.

Finding: the six simple constant arms (LdaZero/LdaUndefined/LdaNull/
LdaTheHole/LdaTrue/LdaFalse) were NOT changed - the JIT already folds their
ctors to the identical direct two-store form (0 loads, 2 stores per arm),
so source-level conversion would be pure churn.

Safety: the `Unsafe.Add(ref accBits, 1) = 0` null store into the Obj half
is safe - null stores need no write barrier, `acc` is a stack local
(barriers apply only to heap slots), and the torn intermediate state is
GC-consistent (same reasoning as the CopyValueTo null path).

Disassembler output verified (`Number(1.5)`); full suite 2,165 passed,
4 skipped; BDN no regressions (smi-sum-loop 1,992 us best so far).

Evidence: snapshot `20260828-204605-0007-numeric-bits-table`.

### C3 block-lexical TDZ hole-init elision - ACCEPTED

Compiler emission change (2026-08-28, feature note
`OKOJO_C3_TDZ_ELISION_NOTE.md`): block scopes now run per-binding
hole-initialization elision (`PrepareBlockLexicalHoleInitializationSkips`
from `EmitBlockStatementCore`). A binding's hole-init is elided when the
initializer does not reference the binding itself, contains no function/
class node (IIFE risk), no preceding statement references the binding or
creates a closure, and - for captured bindings (`Planned.IsCaptured`) - the
block contains no hoisted `FunctionDeclaration` (closures exist from block
entry). Storage-independent (register or context slot): the gate proves the
slot's prior content is never read.

Artifact: stopwatch-modern's inner loop emits zero `LdaTheHole` (was 3 per
iteration, ~6 dispatches); V8 reference shows the identical shape
(`node --print-bytecode`: no hole-init for computed const initializers).
Kept cases (read-before-decl, self-reference, IIFE, closure-before-decl,
assignment-before-decl, captured+hoisted function) verified to retain the
hole-init.

- bench-ab (5 rounds, pgo-off): stopwatch-modern -5.8%, named-get -5.4%,
  smi-sum-loop -1.3%, lexical-block -0.3%. No regressions.
- Full Okojo.Tests 2,176 passed (11 new C3 TDZ tests), 4 skipped.
- Test262 language category: 22,200 passed, 0 failed (let/const/
  block-scope subsets green).

Also fixed in this attempt: test files constructing `JsScript` directly
updated for the `ulong[]` numeric constant table (FrameAbi/BigIntBytecode/
GeneratorAbi/VirtualMachine/Tooling tests; the earlier numeric-bits commit
had left them stale - insights 3.5 staled-binary trap hit again), and
ToolingTests NaN-dedup expectation updated to the canonicalization contract
(distinct NaN payloads dedup to one JsNan slot; payloads unobservable
through constants).

Evidence: snapshot `20260828-212832-0008-c3-tdz-elision`.

### A23 / V3 hot-arm de-fusion beyond arithmetic - ACCEPTED

Implemented 2026-08-28 against the dromaeo-3d-cube-modern opcode mix (Star
26%, Ldar 20% of stream; harness stubs added to VmLoopProbe to make dromaeo
cases runnable - proposals doc tooling gap closed).

- `Star`(18)/`StarWide`(150): dedicated arms (narrow vs wide operand reads);
  the per-execution `cmp edx,150` re-dispatch is gone.
- `Ldar`(16): minimal dedicated arm (no hole check, no width tests);
  `LdaLexicalLocal`(17): own arm keeping the TDZ hole check;
  `LdarWide`/`LdaLexicalLocalWide`: shared cold arm.
- `Inc`(63)/`Dec`(64): separate arms with constant delta; overflow checks
  specialize (Inc upper bound only, Dec lower bound only).
- `TestEqual`+`TestEqualStrict`: one arm with an inline both-number fast
  path (double `==` is exact for number equality); slow path branches on op
  for StrictEquals vs AbstractEquals. `TestNotEqual`: own arm with inline
  `!=` and negated fallback.

Arm evidence: opcodes 16/17/18/63/64/150 all dedicated targets (analyze-jit),
no inner re-dispatch compares. Code size FullOpts 21,742 -> 23,043 B
(+1,301 B: Inc/Dec duplication + TestEqual fast paths), justified by the
measured wins.

bench-ab (5 rounds, pgo-off, medians):

| case | base | attempt | delta |
| ---- | ---: | ------: | ----: |
| dromaeo-3d-cube-modern | 3,195,542 | 2,906,195 | -9.1% |
| smi-sum-loop | 2,070,780 | 1,840,398 | -11.1% |
| stopwatch-modern | 85,675,513 | 76,129,708 | -11.1% |
| for-loop-sum | 205,890 | 182,630 | -11.3% |
| named-get | 3,083,458 | 2,997,960 | -2.8% |

Full Okojo.Tests: 2,176 passed, 4 skipped. T2 dromaeo profile captured
(Star 1.48M, Ldar 1.16M, LdaKeyedProperty 725k, Add 440k, Mul 181k,
Inc 136k per probe) - LdaKeyedProperty/StaKeyedProperty are the remaining
hot targets for a future keyed-op pass.

Evidence: snapshot `20260828-221259-0009-v3-defusion`.

## Attempt Log Status

| ID | Verdict |
| -- | ------- |
| A1 locals diet | ACCEPTED (merged) |
| A2 hot/cold split | ACCEPTED (merged) |
| A3 unsafe checks | ACCEPTED (merged; audit table in notice) |
| A4 inline audit | ACCEPTED (merged) |
| A5 countdown | REJECTED (ceiling: <=0.4%) |
| A6 EH scope | REJECTED (IL no-op) |
| A7 dispatch table | REJECTED (design + E1 microbench; see OKOJO_A7_DISPATCH_DESIGN.md) |
| A10 IC devirt friendliness | ACCEPTED (merged) |
| A12 Run local sharing | ACCEPTED (short A/B; BDN deferred) |
| A13 scaled operand reader | ACCEPTED (short A/B; BDN deferred) |
| T1 listing analyzer | PREPARED (`analyze-jit.ps1`; validate against current `FullOpts`/`Tier1` dumps) |
| T2 opcode/pair profiler | PREPARED (`OkojoVmProfile=true` + `--profile-opcodes`; gates superinstruction selection) |
| A18 `SkipLocalsInit` entry probe | ACCEPTED (prologue clear removed; Tier1 -223 B; suite green) |
| A22 Star/Mov write-barrier elimination | ACCEPTED (BDN-confirmed: arith -8.4%, indexing -10.3%, smi -4.8%, named-get -6.9%; hot Star path barrier-free) |
| A16 numeric result canonicalization | ACCEPTED (integer NaN test + in-place result writes; BDN-confirmed, no regressions) |
| A14 arithmetic arm de-fusion | ACCEPTED (variant A: A/S/M split, DME fused; bench-ab 5 rounds; for-loop-sum -14.6%, arith -4.1%; +1.9KB Tier1) |
| A15 operand bit snapshots | ACCEPTED (shared ulong locals; byref reads 2-4 -> 1 per arm; Tier1 -1KB vs A14; BDN-confirmed broad wins) |
| A20 accumulator-local ceiling | POSITIVE CEILING (probe-only; implementation deferred after 244 semantic failures) |
| A21 accumulator-local implementation | ACCEPTED (full suite + non-staging Test262 green; measured JIT/frame and pgo-off wins) |
| C1 compiler emission elision | ACCEPTED (compiler/test change; recorded in insights 1.16) |
| C2 compiler emission elision | ACCEPTED (compiler/test change; recorded in insights 1.17) |

Open/planned/proposed items are tracked in the foundation backlog table and
the proposals document, not duplicated here.

Cumulative historical baseline: Tier1 22373 -> 20562 (-8.1%) through
A1-A13. The fresh 20260828 comparison for the local-sharing + cold-split
attempt is 22503 -> 21925 Tier1 bytes (-2.6%), 98 -> 44 IL locals, with the
full suite green. Local count is recorded as a means to improve generated
code, not as an acceptance criterion by itself.
