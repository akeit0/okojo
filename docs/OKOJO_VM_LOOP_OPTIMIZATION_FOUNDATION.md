# Okojo VM Loop Optimization Foundation

Related documents:

- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative technical knowledge base
  (JIT/codegen, CPU, C# pitfalls, measurement methodology).
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
| `jit/<case>.<config>.jit.txt` | diffable JIT disassembly |
| `jit/<case>.<config>.stdout.txt` / `.result.txt` | probe program output |

Configs: `pgo-on` (`DOTNET_TieredPGO=1`), `pgo-off` (`DOTNET_TieredPGO=0`),
`tiered-off` (`DOTNET_TieredCompilation=0`). Diffable dumps are enabled via
`DOTNET_JitDisasmDiffable=1`; JIT output is separated from program output via
`DOTNET_JitStdOutFile`.

**Default config set is `pgo-off` only.** Without profile-guided
recompilation the optimized code shape is deterministic (single Tier1-OSR +
Tier1 listing, no PGO specialization churn), so attempt-vs-baseline A/B
comparisons run faster and stay stable. Add `-Configs pgo-off,pgo-on`
explicitly when studying specialization effects.

The default workflow is deliberately light (probe + JIT dumps only, about a
minute). BenchmarkDotNet never runs automatically; pass `-Benchmark` once an
attempt looks promising to add a confirmation run and copy its reports into
the snapshot's `bench/` directory.

### Comparing snapshots

```powershell
pwsh tools/VmLoopProbe/compare-jit.ps1 -Case smi-sum-loop
```

Diffs `jit/<case>.<config>.jit.txt` between two snapshots (defaults: newest
vs newest baseline), prints per-listing code-size deltas, saves a unified
diff into the newer snapshot as
`jit/<case>.<config>.vs-<fromSnapshot>.diff.txt`, and shows the first diff
hunk inline.

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

BDN numbers are the go/no-go signal; probe numbers are directional only.

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

Cumulative vs 0000-baseline: Tier1 code 22373 -> 20562 (-8.1%), IL locals
137 -> 93 (-32%), residual hot-accessor calls eliminated, timings neutral
to better (for-loop-sum -4.1%, closure-heavy -1.6%), full suite green at
every merge.

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
   snapshot with `compare-jit.ps1`; use `pgo-off` as the default comparison
   config so diffs reflect engine changes, not PGO specialization drift.
3. Confirm with BenchmarkDotNet before changing engine code defaults.
4. Language/compiler/VM decisions reference V8 (`tools/V8BytecodeTool`);
   built-in/runtime API decisions reference Node.
5. Keep frame layout and opcode operand conventions stable unless the attempt
   explicitly owns that contract change (see AGENTS.md Core Rules).
