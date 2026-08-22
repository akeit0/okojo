# Okojo VM Loop Optimization Foundation

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

## Candidate Attempts

| ID | Idea | Hypothesis |
| -- | ---- | ---------- |
| A1 | Hot-opcode fast path before the switch | Only pays if JIT stops using a single jump table; verify against baseline dasm first |
| A2 | Move cold opcode handlers out of `Run` into NoInlining methods | Shrink Tier1 code size (~22KB), improve I-cache hit rate in dispatch loop |
| A3 | Execution-check countdown placement/width | `--nextCheck == 0` sits on every dispatch edge; test fusing with pc advance or widening countdown |
| A4 | Narrow try/catch scope around dispatch | Whole-loop EH region may constrain codegen; exceptions are the JS throw mechanism so semantics must be preserved |
| A5 | IC-helper devirtualization friendliness | Help PGO guard/guarded-devirtualize named-property IC calls (sealed/final shapes, explicit type tests) |

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
