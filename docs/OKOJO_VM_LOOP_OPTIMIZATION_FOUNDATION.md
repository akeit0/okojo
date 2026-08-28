# Okojo VM Loop Optimization Foundation

Related documents:

- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative technical knowledge base
  (JIT/codegen, CPU, C# pitfalls, measurement methodology).
- `OKOJO_VM_ATTEMPT_LOG.md` - historical record of completed attempts
  (accepted/rejected/prepared/deferred) and the final verdict table.
- `OKOJO_VM_DEEP_INSPECTION_METHOD.md` - investigation method: layered
  model, artifact-reading recipes, IL-to-native mapping tool design.
- `OKOJO_A7_DISPATCH_DESIGN.md` - dispatch-structure analysis and E1
  microbench evidence.
- `OKOJO_A8_A9_RESEARCH.md` - bytecode/compiler research: corpus profile,
  fusion candidates, let-loop context lowering.
- `OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md` - active 2026-08-28 proposals:
  compiler emission elisions (C3-C4), arm-level VM work (V2-V8), fusion
  revisit-trigger evidence.

Scope: establish a repeatable methodology for optimizing the interpreter
dispatch loop (`JsRealm.Run`, `src/Okojo.JavaScript/Execution/JsRealm.VmLoop.cs`)
beyond wall-clock benchmarking: IL/JIT-assembly comparison, Dynamic PGO A/B,
and per-attempt evidence capture. This document holds the workflow,
tooling, and baseline constraints only; the active plan lives in
`OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md` and completed attempt history in
`OKOJO_VM_ATTEMPT_LOG.md`.

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
- Build with `-p:OkojoVmProfile=true` and pass `--profile-opcodes` to collect
  profile-only opcode and frame-local adjacent-pair counts. The profile build
  is intentionally separate from timing/assembly acceptance; the normal build
  contains no per-dispatch profile code.

```powershell
dotnet build tools/VmLoopProbe/VmLoopProbe.csproj -c Release `
  -p:OkojoVmProfile=true
dotnet tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll `
  smi-sum-loop 10 400 --profile-opcodes
```

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
locations. `--hold` blocks on stdin after warmup; press Enter after
inspection. This is a mapping/disassembly artifact, not a timing result.

Capture once, view offline. One capture writes the full report: `[map]`
IL-to-native ranges, per-instruction `[asm]` lines (native address, IL
offset, PDB source line), `[line-map]` (source line -> contiguous native
ranges with exact instr/calls/loads counts), and `[summary-arm]` (native
bytes attributed to `case JsOpCode.X:` arm groups, sorted by size). Views
over a saved report need no process attach and no repeated execution:

```powershell
dotnet tools/VmLoopIlMap/bin/Release/net10.0/VmLoopIlMap.dll `
  --from run.ilmap.txt --summary          # arm-size rollup
dotnet tools/VmLoopIlMap/bin/Release/net10.0/VmLoopIlMap.dll `
  --from run.ilmap.txt --source-map       # line -> native ranges table
dotnet tools/VmLoopIlMap/bin/Release/net10.0/VmLoopIlMap.dll `
  --from run.ilmap.txt --line 2102,2103   # disassembly of those source lines
```

Multi-case capture driver: `tools/VmLoopIlMap/capture-ilmap.ps1` starts the
probe with a held-open stdin pipe (so `--hold` blocks), attaches, saves one
report per case/env, and prints the tier (`compilation=`) and hot/cold
region sizes:

```powershell
pwsh tools/VmLoopIlMap/capture-ilmap.ps1 `
  -Cases smi-sum-loop,dromaeo-3d-cube-modern -Env tiered-off
```

Notes: attribution is line-based through the PDB - the JIT does not emit
arms in source order, and shared locals that are passed byref anywhere in
`Run` are memory-homed, so per-line native sizes reflect where the code
landed, not source complexity. tiered-off produces one identical FullOpts
body across cases (insights 1.12); tiered/pgo-off captures show which tier
each case actually reached.

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

## Active plan pointer

The dump-driven findings (F1-F6), all open proposals and experiments
(A8/A9/A11, A14-A19, A22-A26, C3-C4, V2-V8), the consolidated backlog, and
the suggested execution order live in the single active plan document:

- `OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md`

Completed attempt history lives in `OKOJO_VM_ATTEMPT_LOG.md`. Do not record
plans or completed attempts in this document.

## Optimization Work Rules (binding for this effort)

1. Never skip or defer a bug discovered mid-attempt; fix it before measuring.
2. Every attempt must also improve reusable tooling when a gap appears
   (bench-ab.ps1 and compare-jit.ps1 were born this way).
3. Record all findings/failures in the snapshot notice.md AND
   `OKOJO_VM_ATTEMPT_LOG.md` (accepted/rejected/prepared entries there;
   durable conclusions in `OKOJO_VM_OPTIMIZATION_INSIGHTS.md`);
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
