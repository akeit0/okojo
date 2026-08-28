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
