# Okojo VM Loop Optimization Foundation

Related documents:

- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative technical knowledge base
  (JIT/codegen, CPU, C# pitfalls, measurement methodology).
- `OKOJO_VM_DEEP_INSPECTION_METHOD.md` - investigation method: layered
  model, artifact-reading recipes, IL-to-native mapping tool design.
- `OKOJO_A7_DISPATCH_DESIGN.md` - dispatch-structure analysis and E1
  microbench evidence.
- `OKOJO_A8_A9_RESEARCH.md` - bytecode/compiler research: corpus profile,
  fusion candidates, let-loop context lowering.

Scope: establish a repeatable methodology for optimizing the interpreter
dispatch loop (`JsRealm.Run`, `src/Okojo.JavaScript/Execution/JsRealm.VmLoop.cs`)
beyond wall-clock benchmarking: IL/JIT-assembly comparison, Dynamic PGO A/B,
and per-attempt evidence capture.

Priority order per AGENTS.md still applies: correctness, observability,
measured optimization.

## Tooling

### 1. VmLoopProbe (`tools/VmLoopProbe`)

Minimal console probe for JIT-dump capture and quick timing:

```powershell
dotnet build tools/VmLoopProbe/VmLoopProbe.csproj -c Release
dotnet tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll <case> [iterations] [warmup]
```

- Cases are shared with BenchmarkDotNet and resolve from
  `benchmarks/Okojo.Benchmarks/scripts/*.js`.
- A case is a JS file whose last expression evaluates to a function; the probe
  compiles it once, then repeatedly calls `realm.Execute(function)` so `Run`
  tiers up.
- Prints `[env]`, `[mode]`, `[result]` lines; `[result]` carries
  mean/median/min/max ns per execution.
- `--inspect-run` reports `Run` IL bytes, maxstack, local count, per-type
  counts, and portable-PDB source-local names. This identifies which C#
  declarations become dedicated IL locals.

### 2. JIT capture (`tools/VmLoopProbe/capture-jit.ps1`)

```powershell
pwsh tools/VmLoopProbe/capture-jit.ps1 -AttemptId 0001-my-attempt -Cases smi-sum-loop,for-loop-sum
```

Creates `artifacts/vmloopopt/snapshots/<yyyyMMdd-HHmmss>-<AttemptId>/`
(gitignored) containing:

| File | Content |
| ---- | ------- |
| `notice.md` | findings, filled in by the attempt author |
| `patch.diff` | working-tree diff at capture time (the "attempt code") |
| `commit.txt` / `status.txt` | exact source state |
| `results.txt` | one `[result]` line per case x config |
| `run-locals.txt` | reflected IL locals and PDB source-local mapping |
| `jit/<case>.<config>.jit.txt` | diffable JIT disassembly |
| `jit/<case>.<config>.stdout.txt` / `.result.txt` | probe program output |

Configs: `pgo-on` (`DOTNET_TieredPGO=1`), `pgo-off` (`DOTNET_TieredPGO=0`),
`tiered-off` (`DOTNET_TieredCompilation=0`). Diffable dumps are enabled via
`DOTNET_JitDisasmDiffable=1`; JIT output is separated from program output via
`DOTNET_JitStdOutFile`.

**Default config set is `pgo-off` only.** Without profile-guided
recompilation the tiered timing/code-shape comparison is stable. Use
`tiered-off` when the artifact under inspection must be one deterministic
`FullOpts` body, and add `pgo-on` when studying profile specialization. Config
lists may be comma-separated, for example `-Configs tiered-off,pgo-off`.

The default workflow is deliberately light (probe + JIT dumps only, about a
minute). BenchmarkDotNet never runs automatically; pass `-Benchmark` once an
attempt looks promising to add a confirmation run and copy its reports into
the snapshot's `bench/` directory.

### Arm-level JIT analysis (`tools/VmLoopProbe/analyze-jit.ps1`)

Use the small T1 analyzer before reading a 6,000-line listing by hand:

```powershell
pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path <listing>.jit.txt -Tier FullOpts

pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path <attempt>.jit.txt -ComparePath <baseline>.jit.txt `
  -Tier Tier1 -ChangedOnly
```

It parses the `RWD00` jump table, maps each opcode to its IG arm, groups
opcodes sharing a target, and reports instruction, memory-operation, call,
indirect-jump, private-stack-slot, and (when available) per-arm code-byte
counts. Comparison is keyed by opcode, so IG-label movement is visible instead
of being mistaken for a semantic change. `-Tier` is required when a dump has
multiple compilations.

The normal diffable capture provides all counts except per-arm byte spans. To
add those, pass a same-method, same-tier listing captured with
`DOTNET_JitDisasmDiffable=0` as `-AddressPath`; its `;; offset=...` annotations
are used only to calculate block sizes. The report is structural evidence, not
a hotness profile; accept or reject an optimization only after benchmark and
relevant-assembly checks.

### IL-to-native inspection (`tools/VmLoopIlMap`)

For source/IL/native correlation, build the CLRMD mapper and hold a warmed
probe at the intended tier:

```powershell
dotnet build tools/VmLoopProbe/VmLoopProbe.csproj -c Release
dotnet build tools/VmLoopIlMap/VmLoopIlMap.csproj -c Release

$env:DOTNET_TieredCompilation = "0"
$env:DOTNET_TieredPGO = "0"
dotnet tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll `
  smi-sum-loop 1 400 --hold

# use the [hold] pid printed above
dotnet tools/VmLoopIlMap/bin/Release/net10.0/VmLoopIlMap.dll <pid> `
  --output artifacts/vmloopopt/snapshots/<snapshot>/run.ilmap.txt
```

`VmLoopIlMap` accepts either a PID or a dump path. CLRMD reads the current
jitted `JsRealm.Run` method, its hot/cold regions, and `ILOffsetMap`; Iced
decodes x86/x64 native bytes; and the portable PDB adds source file/line
locations. The output contains `[map]` ranges and `[asm]` instruction lines.
`--hold` blocks on stdin after warmup; press Enter after inspection. This is a
mapping/disassembly artifact, not a timing result, and the `capture-jit.ps1
-IlMap` automatic integration plus per-line/per-arm rollups remain follow-up
work.

### Comparing snapshots

```powershell
pwsh tools/VmLoopProbe/compare-jit.ps1 -Case smi-sum-loop
```

Diffs `jit/<case>.<config>.jit.txt` between two snapshots (defaults: newest
vs newest baseline), prints per-listing code-size deltas, saves a unified
diff into the newer snapshot as
`jit/<case>.<config>.vs-<fromSnapshot>.diff.txt`, and shows the first diff
hunk inline. It also prints Tier1/Tier1-OSR code bytes, stack reservation,
and call-count deltas, plus the `Run` IL/local summary when both snapshots
contain `run-locals.txt`.

JIT dump knobs follow the dotnet/runtime document
"Viewing JIT disassembly and dumps"
(`docs/design/coreclr/jit/viewing-jit-dumps.md`). Important gotchas learned
here:

- `DOTNET_JitDisasm` takes **wildcard method lists (`*`, `?`), not regex**.
  Patterns containing `:` match class-qualified names; use e.g.
  `*JsRealm:Run*`. Exact names must match the printed full name exactly
  (compiler-generated names like `<<Main>$>g__Add|0_0` will not match).
- `DOTNET_JitDisasmAssemblies`, `DOTNET_JitPrintInlinedMethods`,
  `DOTNET_JitDisasmWithGC`, `DOTNET_JitDisasmWithDebugInfo` are only honored
  by Debug/Checked runtime builds; the product Release runtime silently
  ignores them.

### 3. BenchmarkDotNet confirmation (opt-in)

`benchmarks/Okojo.Benchmarks/VmLoopDispatchBenchmarks.cs` encodes the same
configs as BDN jobs (`DynamicPgoOn`, `DynamicPgoOff`, `TieredOff`; 3 warmup +
10 measured iterations each) over the shared scenarios. Run it only at
attempt end for go/no-go:

```powershell
pwsh tools/VmLoopProbe/capture-jit.ps1 -AttemptId 0002-x -Benchmark
# or standalone:
dotnet run -c Release --project benchmarks/Okojo.Benchmarks --no-build -- --filter *VmLoopDispatchBenchmarks*
```

For an uncommitted working tree, use the short alternating probe first:

```powershell
pwsh tools/VmLoopProbe/bench-ab.ps1 -BaseRef HEAD -AttemptWorkingTree `
  -Config pgo-off -Iterations 75 -Warmup 150 -Rounds 3
```

`bench-ab.ps1` also accepts `pgo-on` and `tiered-off` for sanity checks.
BenchmarkDotNet numbers are the final go/no-go signal when its runtime is
acceptable; the short A/B probe is the first-check alternative for a slow
microbenchmark matrix.

## Baseline Findings (attempt 0000-baseline)

Snapshot: `artifacts/vmloopopt/snapshots/20260823-015059-0000-baseline`
(commit cda0f30, .NET 10.0.11 win-x64).

1. **Dispatch is already jump-table based** in all optimized tiers:
   range check (`cmp ecx,152`) -> 32-bit offset table -> indirect `jmp`,
   with a second sub-range table (`lea edx,[rcx-0x64]`). Reordering switch
   cases is unlikely to help while this lowering holds.
2. **`Run` is ~22-24KB of machine code** at optimizing tiers. I-cache
   pressure from slow-path handlers inline in the loop body is a real
   candidate for splitting hot core vs cold handlers.
3. **Dynamic PGO must not be read as a plain engine win**: it measures
   faster on these single-route micro loops (~32% smi-sum-loop, ~21%
   for-loop-sum vs pgo-off), but that is exactly why it can *hide* code
   optimizations: in a heavy same-route loop the profile-guided recompile
   specializes the one taken path, masking general improvements (or
   regressions) made to the shared dispatch code. Every attempt must be
   compared against a same-config baseline under BOTH `pgo-on` and
   `pgo-off`; gains visible only under one config are likely
   specialization effects, not engine improvement.
4. **Short scripts may never reach final Tier1 within a probe run**: several
   cases produced only Instrumented Tier0 + Tier1-OSR listings, so measured
   time ran on OSR code. Attempt comparisons must note which tier listing
   they are reading.
5. Probe cells below ~10us flip sign between processes; do not decide
   anything from them.

## JIT Constraints Observed on `Run`

Measured on the baseline build (reflection over the compiled assembly):

- `JsRealm.Run`: **8,468 bytes of IL, 137 local variables** (56 Int32, 14
  Boolean, 10 JsValue, plus assorted refs/spans), maxstack 10.
- Resulting machine code at Tier1: ~22.4KB (see Baseline Findings).

Two consequences drive the attempt backlog:

1. **Large-function JIT limits.** RyuJIT applies size/complexity heuristics
   (inlining budgets, optimization cutoffs, block ordering) that disadvantage
   one enormous method; hot opcodes do not get "small function" treatment,
   and cold handlers bloat every dispatch path's code footprint.
2. **IL locals vs precise GC.** Every IL local that can hold a reference is a
   GC-reporting slot; a large declared-local count constrains register
   allocation and forces stack-slot bookkeeping across the whole method even
   when most locals live only in a few opcode arms. Reducing declared locals
   in the loop head (and per-arm temps) measurably changes frame setup and
   reg pressure.

## Next plan: dump-driven arithmetic and entry costs

The current tiered-off `Run` listing is 21,924 bytes with 44 IL locals. The
reference artifact is
`artifacts/vmloopopt/snapshots/20260828-112717-0008-current-asm/jit/smi-sum-loop.tiered-off.direct.jit.txt`.
The following observations are concrete assembly signatures, but their
performance meaning is still a hypothesis: each must get an isolated bench-ab
median and a same-config assembly diff before it becomes an accepted
optimization.

### Working dump findings

1. **Entry zeroing (F1):** `init_locals=True` plus the `0x4B8`-byte frame emits
   a prologue clear loop (`mov rax,-0x420`, three `vmovdqa` stores, `add rax,48`,
   `jne`). About 1.1KB is cleared on every `Run` entry. This is mostly
   irrelevant to a single long-running loop, but matters for accessor getters,
   `InvokeFunction` re-entry, and generator drives.
2. **Accumulator indirection (F2):** arms repeatedly reload
   `mov rax,bword ptr [rbp-0x338]` for the spilled `&this.acc` and then
   dereference it. Numeric results also use a `vucomisd` self-compare,
   conditional NaN canonicalization, and a second store clearing `Obj`; a
   single float add therefore pays `vucomisd`, two branches, a pointer reload,
   and two stores before the next dispatch.
3. **Arithmetic re-dispatch (F3):** the fused arm compares `op` again with
   `cmp edx,59` (`Add`) and `cmp edx,60` (`Sub`), then uses the `RWD776`
   secondary table for `Div`/`Mod`/`Exp` (IG293-IG297). The int-plus-int path
   has its own `cmp edx,59/60/68` chain (IG312-IG316). The mixed path pays this
   after the accumulator overflows to Float64.
4. **Aliasing-blocked CSE (F4):** the mixed path performs two back-to-back
   `and r9,qword ptr [rax]` mask tests in IG284. The `IsFloat64`/`IsInt32`
   reads go through a byref, so RyuJIT does not CSE them across possible
   aliasing.
5. **Cloned slow tails (F5):** `HandleArithmeticNonNumberSlowPath` is emitted
   along IG280-IG283, IG285-IG287, and IG289-IG291, each with a private 16-byte
   temporary (`[rbp-0x1C0]`, `-0x1D0`, `-0x1E0`). This is code-size and frame
   pressure from one source-level tail reached through three flows.
6. **Wide overflow math (F6):** int-plus-int uses sign extension, a 64-bit
   add, and two range comparisons instead of a 32-bit overflow test.

Non-goal: the `opcodePc`/`op` spills at `[rbp-0x370]`/`[rbp-0x8C]` are
EH-liveness-forced because the catch reads `opcodePc` and arithmetic arms read
`op`. P1 can reduce `op` readers, but the spills are not a target while the
catch needs that cursor.

### Planned experiments

1. **P1 / A14 - de-fuse arithmetic:** give `Add`, `Sub`, and `Mul` separate
   arms. The top-level `RWD00` table already has separate entries, so the
   dispatch target set and BTB shape should remain unchanged; only the arm
   bodies specialize. Expected effect: remove 1-3 inner compares and a second
   indirect jump on the mixed path, and potentially collapse F5's cloned tail.
2. **P2 / A15 - snapshot operands:** read `acc` and `slotRef` into locals once
   before multi-testing their tags. A 16-byte `JsValue` local whose `Obj` half
   is unused on the numeric path may promote to one GPR and remove F4's
   aliasing barrier.
3. **P3 / A16 - integer numeric canonicalization:** test
   `(bits & BoxMask) == BoxHdr` on the `vmovq` integer bits instead of the
   floating self-compare. This follows the existing box-header mask pattern,
   removes the xmm-to-flags dependency, preserves the exact `JsValue`
   invariant, and may be centralized in one internal
   `FromNumericResult(double)` helper.
4. **P4 / A17 - 32-bit overflow:** compare `int r = a + b` with
   `((a ^ r) & (b ^ r)) < 0` (or the smaller `(int)res == res` form) against
   current semantics. This is a tiny innermost-loop experiment and needs exact
   Smi-to-Float64 promotion tests.
5. **P5 / A18 - entry clear ceiling:** test `[SkipLocalsInit]` on `Run` (or,
   only after an assembly-wide audit, at assembly scope) after auditing all
   managed-reference initialization. Verify that the prologue loop disappears
   in tiered-off asm and use re-entrant accessor/generator workloads, not just
   `smi-sum-loop`.
6. **P6 / A19 - three-operand superinstructions:** after T2 pair frequencies
   and P7 headroom, fuse patterns such as `Ldar rA; Add rB; Star rC` into
   `AddRR rA,rB -> rC`, bypassing `this.acc` and two dispatches. This follows
   the register-machine shape used by LuaJIT/JSC; V8 Ignition avoids the same
   cost with a physical accumulator that the current C# loop cannot provide per
   dynamic opcode. Adding bytecode entries changes the BTB target set, so
   re-check the dispatch evidence.
7. **P7 / A20 - accumulator-local ceiling:** make a probe-only hacked build
   with a local `ulong accBits` mirror used only by numeric
   arithmetic/compare/`Ldar`/`Star` arms. It may be semantically wrong outside
   numerics and is valid only for cases such as `smi-sum-loop` and
   `for-loop-sum`. A small ceiling kills the invasive path; a large one
   justifies the synchronization audit for calls, suspends, and exceptions.

Execution order is the prepared T1 listing analyzer, T2 opcode/pair
profiling, the P7 ceiling probe, then isolated P1-P5 attempts. P6 remains
deferred until both the pair profile and ceiling are positive. T3-T6 are
follow-on attribution tools when the isolated results remain unclear. When an
F1-F6 hypothesis is confirmed, copy the measured result into
`OKOJO_VM_OPTIMIZATION_INSIGHTS.md` with its snapshot and tier; until then,
keep it labeled as a hypothesis here.

## Candidate Attempts

Order = proposed execution order (cheap/measurable first). Each attempt:
one hypothesis, `capture-jit.ps1` snapshot with default pgo-off, dasm diff via
`compare-jit.ps1`, BDN confirmation only if probe+dasm look good.

| ID | Idea | Hypothesis / Notes |
| -- | ---- | ------------------ |
| A1 | IL-locals diet in `Run` loop head | 137 locals (56 Int32) inflate GC-report slots and reg pressure; hoist/reuse temps, move per-arm temporaries into handler methods. Measure: frame-setup instructions in dasm, code size, probe timing |
| A2 | Hot/cold split: cold opcode handlers -> NoInlining methods | Cold rare ops leave the loop body; hot core shrinks from ~22KB toward I-cache-friendly size; cold paths pay call overhead only when executed |
| A3 | Remove redundant checks via Unsafe where provably safe | e.g. `GetPcOffset` uses `checked` byte-offset math on every slow-path hop; validated bytecode makes many bounds/overflow checks dead. Remove only where correctness is provable; add regression tests for touched paths |
| A4 | Manual inline / AggressiveInlining audit | Ensure hot-op helper bodies actually inline (call-site scan in dasm); mark tiny hot helpers AggressiveInlining, keep big ones NoInlining to protect budgets |
| A5 | Execution-check countdown placement/width | `--nextCheck == 0` sits on every dispatch edge; test fusing with pc advance or widening countdown |
| A6 | Narrow try/catch scope around dispatch | Whole-loop EH region may constrain codegen; exceptions are the JS throw mechanism so semantics must be preserved exactly |
| A7 | Dispatch structure: switch -> opcode-indexed function-pointer table | Static `delegate*<ref VmState, ...>[]` indexed by opcode passes state explicitly. Expected gain is NOT dispatch speed (jump table already confirmed) but code-size/reg-pressure relief per handler; risks indirect-call overhead + state-struct refactor. Compare stable pgo-off diffs before/after |
| A8 | Per-operation implementation changes (smi fast paths etc.) | Per-op work with V8 reference observations; use OkojoBytecodeTool cases + Node/V8 repros per AGENTS tooling rules |
| A9 | Opcode set streamlining | Compiler-contract change (frame layout/operand rules): only after A1/A2 measurements justify it; needs OkojoBytecodeTool evidence first |
| A10 | IC-helper devirtualization friendliness | Help PGO guard/guarded-devirtualize named-property IC calls (sealed/final shapes, explicit type tests) |
| A11 | Tree-walk interpreter alternative | Largest change; diverges from the V8/Ignition reference model. Only if the bytecode path plateaus after A1-A10; requires its own feature note before starting |
| A12 | `Run` C# local sharing | Use the PDB-backed local report to preserve frame/stack-machine state, but share short-lived same-type temporaries. Measure benchmark first, then Tier1 frame/calls, then IL locals |
| A13 | Scaled operand reader fast/cold split | Keep the common single-byte operand read inline and share wide/extra-wide/invalid decoding in one cold NoInlining helper |
| A14 | Arithmetic arm de-fusion | Give `Add`, `Sub`, and `Mul` separate arms so mixed numeric paths do not re-dispatch on `op`; verify the top-level jump-table target set is unchanged and measure mixed arithmetic |
| A15 | Operand snapshots before tag tests | Copy `acc` and `slotRef` to locals before repeated numeric/type tests; inspect whether byref reloads and duplicate mask loads disappear without enlarging the frame |
| A16 | Numeric result canonicalization | Replace the floating self-compare used for box-header avoidance with the integer mask invariant; add NaN/number regression coverage before accepting a helper |
| A17 | 32-bit Smi overflow check | Test a 32-bit add/overflow test against the current 64-bit range checks; preserve exact JS integer/float promotion semantics |
| A18 | `SkipLocalsInit` entry probe | Test removal of the `Run` prologue clear only after auditing managed-reference initialization and re-entry paths; use accessor/generator cases, not just a single loop |
| A19 | Three-operand arithmetic superinstructions | Fuse measured register-op patterns such as `Ldar` + arithmetic + `Star`; require T2 pair frequencies, P7 headroom, compiler/bytecode evidence, and an explicit opcode-contract owner |
| A20 | Accumulator-local ceiling probe | Probe-only local `ulong` accumulator mirror for numeric `Ldar`/`Star`/arithmetic/compare paths; semantically incomplete by design and never a shipping change |

Deferred/rejected ideas stay recorded here with reasons instead of being
retried silently (AGENTS.md: no old fast-path experiments without profiling
evidence).

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
| A8 per-op implementation | open (per-op) |
| A9 opcode set | open (needs bytecode evidence) |
| A10 IC devirt friendliness | ACCEPTED (merged) |
| A11 tree walk | open (last resort) |
| A12 Run local sharing | ACCEPTED (short A/B; BDN deferred) |
| A13 scaled operand reader | ACCEPTED (short A/B; BDN deferred) |
| T1 listing analyzer | PREPARED (`analyze-jit.ps1`; validate against current `FullOpts`/`Tier1` dumps) |
| T2 opcode/pair profiler | PLANNED (gates superinstruction selection) |
| A14 arithmetic arm de-fusion | PLANNED (after T1/T2/P7 gates) |
| A15 operand snapshots | PLANNED |
| A16 numeric canonicalization | PLANNED |
| A17 32-bit overflow check | PLANNED |
| A18 `SkipLocalsInit` entry probe | PLANNED |
| A19 arithmetic superinstructions | DEFERRED (requires T2 and P7 evidence) |
| A20 accumulator-local ceiling | PLANNED (probe-only gate) |

Cumulative historical baseline remains Tier1 22373 -> 20562 (-8.1%). The
fresh 20260828 comparison for the current local-sharing + cold-split attempt
is 22503 -> 21925 Tier1 bytes (-2.6%), 98 -> 44 IL locals, with the full
suite green. Local count is recorded as a means to improve generated code,
not as an acceptance criterion by itself.

## Optimization Work Rules (binding for this effort)

1. Never skip or defer a bug discovered mid-attempt; fix it before measuring.
2. Every attempt must also improve reusable tooling when a gap appears
   (bench-ab.ps1 and compare-jit.ps1 were born this way).
3. Record all findings/failures in the snapshot notice.md AND this document;
   rejected attempts stay on their branches as recoverable knowledge.
4. Measure hot cases AND degradable cases (workloads that execute the changed
   cold paths) before accept/reject.
5. Accept/reject only on bench-ab medians plus dasm evidence; single probe
   runs are not decisions.
6. To compare two revisions fairly use `tools/VmLoopProbe/bench-ab.ps1`
   (git-worktree isolation, alternating rounds).

Rules for every attempt:

1. One hypothesis per attempt; fill `notice.md` from the template.
2. Compare dasm of the SAME case/config against the newest accepted baseline
   snapshot with `compare-jit.ps1`; use `tiered-off` with one representative
   case for deterministic `FullOpts` assembly, and `pgo-off` for tiered timing
   and Tier1/OSR behavior.
3. Confirm with BenchmarkDotNet before changing engine code defaults.
4. Language/compiler/VM decisions reference V8 (`tools/V8BytecodeTool`);
   built-in/runtime API decisions reference Node.
5. Keep frame layout and opcode operand conventions stable unless the attempt
   explicitly owns that contract change (see AGENTS.md Core Rules).
