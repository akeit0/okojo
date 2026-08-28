# Okojo VM Deep Inspection Method

Related documents:

- `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` - workflow, tooling commands,
  and baseline constraints.
- `OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md` - the single active plan
  (proposals, backlog, suggested order).
- `OKOJO_VM_ATTEMPT_LOG.md` - completed attempt history.
- `OKOJO_VM_OPTIMIZATION_INSIGHTS.md` - cumulative findings (JIT/codegen,
  CPU, C# pitfalls, measurement rules).

Scope: how to *investigate* interpreter performance problems where source
logic, RyuJIT codegen, and CPU microarchitecture interact - the thinking
process and artifact-reading recipes, plus the design for the missing
IL-to-native mapping layer. This document changes rarely; per-attempt
results go to the insights doc and snapshot notices.

## 1. The layered model

Every performance question about `JsRealm.Run` lives on one of four layers:

| layer | artifact | tool |
| ----- | -------- | ---- |
| C# source | the switch arm / helper | editor, `rg` |
| IL | IL bytes, locals, EH regions | `VmLoopProbe --inspect-run` |
| native code | tier-specific asm listing | `capture-jit.ps1`, `analyze-jit.ps1`, `compare-jit.ps1` |
| VM execution stream | fetched opcode and frame-local pair counts | `VmLoopProbe --profile-opcodes` (profile build) |
| CPU behavior | wall time (today); PMU/samples (planned) | `bench-ab.ps1`, probe medians |

The core discipline: **a change is only understood when you can name the
layer it acts on and hold evidence from that layer AND from wall time.**
A source-level "improvement" (fewer branches, fewer locals, shared code)
routinely gets defeated one layer down - register allocation, code layout,
inline budgets, BTB target sets. In `Run` this is the norm, not the
exception (insights 1.11: independently good edits are not additive;
1.2: fewer dispatch sites can be slower).

Corollary: never argue from one layer alone.

- Source intuition alone: rejected repeatedly (insights 3.8).
- IL/local counts alone: explicitly not an acceptance criterion (1.7).
- Asm shape alone: explains, but medians decide (§4 of insights).

## 2. The investigation loop

For each hypothesis, run this loop; steps are cheap-first.

1. **State the hypothesis at a specific layer**, falsifiably.
   "The mixed int/float Add path re-dispatches on `op` inside the arm"
   is checkable in asm; "the loop is slow" is not a hypothesis.
2. **Ceiling-measure before designing.** If the feature/cost can be
   disabled or hacked out (even semantically incorrectly for one probe
   case), measure the maximum possible win first. A noise-level ceiling
   kills the idea for the cost of one build (killed A5; standard since).
3. **Pick the artifact that can falsify it** and read it (recipes in §3).
   Do not skip to timing: timing tells you *whether*, artifacts tell you
   *why*, and without the why, layout noise will mislead you.
4. **Change one thing.** One hypothesis per attempt, `bench-ab` alternating
   medians, same-config asm diff against the newest accepted snapshot.
5. **Explain the delta mechanistically.** If the timing moved but the asm
   diff shows only layout churn (block reordering, different spill slots,
   alignment), suspect layout luck: re-run, and re-verify the stacked
   result after combining with other accepted edits.
6. **Record regardless of outcome** - snapshot `notice.md` plus an insights
   entry. Rejected attempts stay on branches; the reason is the value.

When behavior contradicts the source at any point: verify the boring
explanations first - stale binary (insights 3.5), raw bytecode bytes vs
the disasm listing (foundation A2 log), wrong tier listing (1.12/1.13).

## 3. Reading the artifacts

### 3.1 `run-locals.txt` (IL layer)

- `il_bytes`, `locals`, `init_locals` header line: trend these across
  snapshots; `init_locals=True` means the prologue zeroes the frame.
- The `[run-source-local]` scope list separates persistent machine state
  (`pc`, `acc`, `fp`, `registerRef`, IC arrays - must keep dedicated
  locals) from per-arm temporaries (sharing candidates, see A12).

### 3.2 The JIT listing (`jit/<case>.<config>.jit.txt`)

Read in this order:

1. **Header**: tier (`FullOpts` vs Tier1/OSR - compare like with like),
   `Total bytes of code`, PGO line.
2. **Prologue (IG01)**: `sub rsp, N` = frame size; a `vmovdqa`+`jne` loop
   means init-locals zeroing is paid on every `Run` entry (matters for
   re-entrant workloads: accessors, `InvokeFunction`, generator drives).
3. **Dispatch block**: locate the opcode fetch (`movzx` from the pc
   register). Count what each dispatch pays: stack spills (`mov [rbp-...]`),
   the countdown load/dec/store, range check, table load, `jmp reg`.
   Count ALL `jmp r*` sites in the method - the switch lowers to multiple
   clustered tables (insights 1.1), and the cluster structure interacts
   with the BTB (1.2).
4. **Opcode-to-arm map**: the `RWD00` data block at the end of the listing
   is the jump table - one `dd G_M000_IGxxx - G_M000_IGyy` entry per
   opcode value, in opcode order. This gives a machine-readable
   opcode -> arm-label mapping; use it to find any opcode's arm without
   guessing from instruction patterns.
5. **Per-arm audit** (for arms the case actually executes):
   - pointer reloads: repeated `mov rax, bword ptr [rbp-...]` before
     accesses = a byref local homed on the stack (e.g. the `acc` ref);
   - duplicate loads the JIT would not CSE: two `and r, [rax]` mask tests
     off the same address = aliasing blocked CSE; fix at source by
     copying to a local first;
   - `call` sites: `rg "call.*\[Okojo" <listing>` grouped by target -
     tiny hot accessors appearing here are inline failures (A4 recipe);
     big helpers appearing here confirm NoInlining is working;
   - cloned tails: the same call sequence with different `[rbp-...]` temp
     slots repeated 2-3x = the JIT duplicated a shared source tail along
     multiple flows; restructure to a single exit;
   - inner re-dispatch: `cmp edx, <opcode>` chains or secondary `RWD`
     tables inside an arm = a fused multi-opcode arm re-testing `op` at
     runtime; candidate for de-fusing (dispatch table shape is unchanged
     since the opcodes already own separate entries).
   - numeric result writes: `vmovq`/`vucomisd` followed by a store of the
     numeric bits and a second store clearing `Obj` = the box-header/NaN
     canonicalization cost; test an integer mask invariant without changing
     `JsValue` representation.
   - integer overflow: `movsxd` plus a 64-bit add and two range checks in an
     int-plus-int arm = a candidate for a 32-bit overflow test, but only with
     exact JS Smi promotion coverage.
6. **EH-forced spills**: any local read inside the `catch` (e.g. the
   `opcodePc` cursor) is live across the whole try region and will have a
   stack home no matter what. Recognize these as immovable before
   spending an attempt on them.

### 3.3 Arm-level report (`analyze-jit.ps1`)

The T1 analyzer turns the listing's `RWD00` table into a compact arm report:

```powershell
pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path <listing>.jit.txt -Tier FullOpts

pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path <attempt>.jit.txt -ComparePath <baseline>.jit.txt `
  -Tier Tier1 -ChangedOnly
```

The report is keyed by opcode and groups entries that target the same IG
label. It counts instructions, approximate loads/stores, calls, indirect
jumps, and private `[rbp-...]` slots. A non-diffable same-tier listing can be
passed as `-AddressPath` to add per-arm byte spans from its `;; offset=...`
annotations. This is structural attribution only: it does not identify hot
arms or prove a speedup.

Use `-ChangedOnly` for a compact A/B view. Since the comparison key is the
opcode, a changed target label is reported as a target change rather than
silently losing the arm in an IG-label diff.

### 3.4 Dynamic opcode/pair profile (`VmLoopProbe --profile-opcodes`)

Build the probe with the opt-in profile constant, then run a workload through
the resulting binary:

```powershell
dotnet build tools/VmLoopProbe/VmLoopProbe.csproj -c Release `
  -p:OkojoVmProfile=true
dotnet tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll `
  smi-sum-loop 10 400 --profile-opcodes
```

The output is sorted by count and has one summary line followed by
`[profile-op]` opcode rows and `[profile-pair]` rows. It counts every fetched
byte-valued dispatch opcode, including `Wide`/`ExtraWide` prefixes. Pair state
resets at each VM frame reload, so a caller's `Call` is not falsely adjacent to
the callee's first opcode; this makes the pair rows suitable for compiler
fusion screening. The profile build is single-threaded probe instrumentation,
not a benchmark build. A normal build contains no counters, profile locals, or
per-dispatch branches; using `--profile-opcodes` there reports the rebuild
requirement.

Use the profile to choose candidates, not to accept an optimization. The
candidate still needs the normal pgo-off timing comparison, same-tier assembly,
and IL evidence described above.

### 3.5 Diffs (`compare-jit.ps1`)

Code-size delta first, then WHERE: a delta concentrated in the changed
arm is explainable; a diff smeared across unrelated arms is layout churn
- do not attribute timing shifts to your semantic change until re-run.

### 3.6 From an asm signature to an isolated plan

Use the signature, not the source idea, to choose the next experiment:

| listing signature | first experiment |
| ----------------- | ---------------- |
| `vmovdqa`/`jne` frame-clear loop in `IG01` | `SkipLocalsInit` ceiling on re-entrant workloads; audit managed-reference initialization first |
| repeated reload of the spilled `&acc` | A21 value-local accumulator; publish only at re-entry, observable, exception, and exit boundaries; verify `finally` can see the local |
| `op` compares or a secondary `RWD` table inside arithmetic | separate `Add`/`Sub`/`Mul` arms; top-level dispatch is the control |
| repeated tag masks from the same byref | copy the operand to one local, then re-read the arm assembly |
| repeated slow-call tail with distinct temp slots | make one shared exit or de-fuse the flows, then inspect code-size and call-count deltas |
| sign-extended int arithmetic plus range checks | compare a 32-bit overflow form against semantic edge cases and the inner-loop median |

These signatures are candidate explanations, not proof of a bottleneck. The
acceptance order remains benchmark median, relevant optimized assembly, then
IL/local evidence.

Do not spend an attempt removing the `opcodePc`/`op` stack homes merely because
they are visible in the listing: the catch reads `opcodePc` and fused arms read
`op`, so EH liveness forces those homes. De-fusing can reduce `op` readers, but
the spill itself is not a target until the lifetime contract changes.

## 4. Known blind spots (and what fills them)

The current toolchain now answers structural opcode-to-arm costs, but not:

| blind spot | consequence | fill |
| ---------- | ----------- | ---- |
| where cycles go inside `Run` | arm costs inferred from shape only | sampled heatmap (§5.3) |
| asm <-> source correlation | CLRMD map gives ranges/instructions, not arm rollups | IL/native map integration and T1 join (§5) |
| branch-miss / I-cache data | BTB reasoning rests on one microbench | ETW PMC / VTune wrapper |

## 5. IL-to-native mapping tool

### 5.1 Why a tool is required

`DOTNET_JitDisasmWithDebugInfo` would annotate listings with IL offsets,
but it is honored only by Debug/Checked runtime builds - the product
runtime silently ignores it (documented in `capture-jit.ps1`). On the
release runtime the mapping must come from the runtime's debug info via
one of:

| route | mechanism | cost | notes |
| ----- | --------- | ---- | ----- |
| **CLRMD (recommended)** | `Microsoft.Diagnostics.Runtime`: `ClrMethod.ILOffsetMap` + `NativeCode` - the DAC-side equivalent of `ICorProfilerInfo::GetILToNativeMapping` | managed-only NuGet, no native build | attach to a paused live process or open a `dotnet-dump`; reads the CURRENT jitted version, so pin the tier |
| native `ICorProfiler` | C++ profiler DLL, `JITCompilationFinished` + `GetILToNativeMapping2` | new native project, COM registration env vars | only needed if per-tier history or exact rejit tracking becomes necessary |
| ETW `MethodILToNativeMap` | `Microsoft-Windows-DotNETRuntime` JitTracing keyword, parsed with TraceEvent | capture-time only | the natural route when combining with CPU sample events (§5.3) |

### 5.2 Current first slice and remaining shape

The first slice is implemented as `tools/VmLoopIlMap` (managed console app)
plus one probe flag:

1. `VmLoopProbe <case> --hold`: run warmup so `Run` reaches the intended
   tier (use `tiered-off` for a single deterministic body), print
   `[hold] pid=<pid>`, block on stdin.
2. `VmLoopIlMap <pid>` (or `<dump-path>`):
   - attach via CLRMD, locate `JsRealm.Run`, read `NativeCode`,
     `HotColdInfo`, and `ILOffsetMap`;
    - disassemble the hot/cold native bytes with Iced on x86/x64;
    - open the portable PDB (`System.Reflection.Metadata`) and resolve IL
      offsets to sequence points (`JsRealm.VmLoop.cs` lines) - the same
      PDB path `--inspect-run` already uses;
    - emit `[map]` ranges and `[asm] native | asm | IL-offset | file:line`
      lines.
3. Remaining integration: `capture-jit.ps1 -IlMap` should run the pair and
   store `run.ilmap.txt` beside the diffable listing; the T1 `RWD00` map can
   then add per-source-line and per-arm rollups.

Caveats to encode in the tool's output header:

- Optimized-code mappings are range-approximations with `NO_MAPPING` /
  `PROLOG` / `EPILOG` sentinel regions; per-line rollups are attribution
  hints, not exact accounting.
- Addresses differ from the diffable listing (ASLR, non-diffable mode);
  treat `run.ilmap.txt` as an independent artifact and correlate with the
  diffable listing by instruction sequence, not by offset.
- The map reflects the currently active code version; capture under
  `tiered-off` unless studying tier differences deliberately.

### 5.3 Follow-on: cycles per source line

Once the map exists, the heatmap chain is: ETW CPU samples (PerfView or
`dotnet-trace`, max sample rate) -> sample RVA inside `Run` ->
`ILOffsetMap` -> sequence points -> cycles per C# line / per opcode arm.
That converts §3.2's shape-based arm auditing into direct attribution and
is the intended endpoint of this tooling track.

## 6. Reasoning patterns that repeatedly worked

The recurring "how to think" checklist when a result surprises:

1. **What can the JIT not know here?** Aliasing through refs blocks CSE
   (copy to a local before multi-testing); EH liveness forces stack
   homes; 16-byte struct accessors exceed inline heuristics under
   pressure (force them); `checked` emits real branches even when
   structurally dead; `init_locals` zeroes whole frames.
2. **What does the CPU see, not the source?** Count indirect-jump sites
   and their per-site target sets before predicting BTB behavior; a
   "removed branch" that merges two predictable branches into one
   unpredictable one is a regression.
3. **Is this delta mine?** Diffable dumps are byte-identical for
   identical code - any nonzero diff is caused by the change; but timing
   deltas without asm deltas in the touched arm are layout/measurement
   artifacts until reproduced.
4. **Measure the ceiling before the mechanism.** Disable first, design
   second.
5. **Prefer evidence that survives rebuilds**: raw bytes (bytecode dumps,
   `RWD` tables), medians over rounds, and mechanism explanations -
   single-run numbers and address-specific observations do not.

## 7. Standing tool backlog for this track

In dependency order:

1. **T1 - `analyze-jit` (prepared):** the PowerShell analyzer parses `RWD00`'s
   `dd` entries and IG blocks into a per-opcode arm table containing code
   bytes, instruction count, loads/stores, calls, private `[rbp-...]` slots,
   and indirect jumps. Its opcode-keyed comparison makes target-label churn
   visible. Remaining work is validation against more cases and, if needed,
   richer source/IL correlation; it is not a hotness profiler.
2. **T2 - dynamic opcode/pair profile (prepared):** build with
   `-p:OkojoVmProfile=true` and run `VmLoopProbe --profile-opcodes` for
   per-opcode execution counts and the frame-local `(previous -> next)` pair
   matrix. The normal build pays zero dispatch cost. This supplies the actual
   fusion candidates for P6/A19; it is not a timing configuration.
3. **T5 - ceiling-build harness:** formalize probe-only named constants such
   as `CEILING_ACC_LOCAL`, `CEILING_NO_NAN_CANON`, and
   `CEILING_NO_OBJ_CLEAR`; let `bench-ab.ps1` select those builds. P7/A20 and
   the numeric-result ceiling are the first customers.
4. **T3 - sampled cycle heatmap:** use ETW CPU sampling (`PerfView` or
   `dotnet-trace` at the maximum useful rate), a non-diffable listing with
   addresses, and T1's opcode-to-IG map to attribute sample IPs to arms. The
   output should answer whether dispatch or mixed arithmetic consumes the
   `smi-sum-loop` time.
5. **T4 - PMU wrapper:** add `capture-pmc.ps1` using VTune when available or
   `xperf -pmcprofile BranchMispredictions,CacheMisses`; report branch misses
   and L1I misses per dispatched op using T2's counts. This is the evidence
   needed for BTB/cluster-spread claims on real workloads.
6. **IL/native map integration (partial):** `VmLoopIlMap` + `--hold` are
   prepared and validated on `JsRealm.Run`; add `capture-jit.ps1 -IlMap`,
   per-source-line/per-arm rollups, and explicit tier capture metadata (§5.2).
7. **Static sequence analysis (T6):** run uiCA/llvm-mca on the roughly
   20-instruction dispatch and hot-arithmetic sequences extracted by T1. Use
   it for port-pressure comparisons, especially P3 variants, without making
   it a substitute for wall-clock evidence.

Do not begin the three-operand superinstruction track until T2 supplies real
opcode-pair frequencies and the accumulator ceiling probe shows enough headroom
to justify a compiler/bytecode change. T3/T4 remain attribution tools, not
reasons to bypass the benchmark gate.
