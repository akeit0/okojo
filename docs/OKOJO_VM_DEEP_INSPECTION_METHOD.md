# Okojo VM Deep Inspection Method

Related documents:

- `OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` - workflow, tooling commands,
  attempt backlog and log.
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
| native code | tier-specific asm listing | `capture-jit.ps1`, `compare-jit.ps1` |
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

### 3.3 Diffs (`compare-jit.ps1`)

Code-size delta first, then WHERE: a delta concentrated in the changed
arm is explainable; a diff smeared across unrelated arms is layout churn
- do not attribute timing shifts to your semantic change until re-run.

### 3.4 From an asm signature to an isolated plan

Use the signature, not the source idea, to choose the next experiment:

| listing signature | first experiment |
| ----------------- | ---------------- |
| `vmovdqa`/`jne` frame-clear loop in `IG01` | `SkipLocalsInit` ceiling on re-entrant workloads; audit managed-reference initialization first |
| repeated reload of the spilled `&acc` | numeric accumulator-local ceiling; preserve machine-stack and call/suspend synchronization in any real attempt |
| `op` compares or a secondary `RWD` table inside arithmetic | separate `Add`/`Sub`/`Mul` arms; top-level dispatch is the control |
| repeated tag masks from the same byref | copy the operand to one local, then re-read the arm assembly |
| repeated slow-call tail with distinct temp slots | make one shared exit or de-fuse the flows, then inspect code-size and call-count deltas |
| sign-extended int arithmetic plus range checks | compare a 32-bit overflow form against semantic edge cases and the inner-loop median |

These signatures are candidate explanations, not proof of a bottleneck. The
acceptance order remains benchmark median, relevant optimized assembly, then
IL/local evidence.

## 4. Known blind spots (and what fills them)

The current toolchain answers "what does the code look like" but not:

| blind spot | consequence | fill |
| ---------- | ----------- | ---- |
| where cycles go inside `Run` | arm costs inferred from shape only | sampled heatmap (§5.3) |
| asm <-> source correlation | manual pattern-matching per arm | IL/native map tool (§5) |
| dynamic opcode/pair frequencies | fusion candidates chosen by intuition | profile build counters |
| branch-miss / I-cache data | BTB reasoning rests on one microbench | ETW PMC / VTune wrapper |

## 5. IL-to-native mapping tool (design)

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

### 5.2 Tool shape

New tool `tools/VmLoopIlMap` (managed console app) plus one probe flag:

1. `VmLoopProbe <case> --hold`: run warmup so `Run` reaches the intended
   tier (use `tiered-off` for a single deterministic body), print
   `[hold] pid=<pid>`, block on stdin.
2. `VmLoopIlMap <pid>` (or `<dump-path>`):
   - attach via CLRMD, locate `JsRealm.Run`, read `NativeCode`,
     `HotColdInfo`, and `ILOffsetMap`;
   - disassemble the native bytes with Iced;
   - open the portable PDB (`System.Reflection.Metadata`) and resolve IL
     offsets to sequence points (`JsRealm.VmLoop.cs` lines) - the same
     PDB path `--inspect-run` already uses;
   - emit `run.ilmap.txt`: `native-offset | asm | IL-offset | file:line`,
     plus a per-source-line rollup (native bytes and instruction count
     per C# line) and a per-arm rollup using the §3.2 `RWD00` opcode map.
3. `capture-jit.ps1 -IlMap`: run the pair automatically and store
   `run.ilmap.txt` in the snapshot beside the diffable listing.

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

1. Listing analyzer (T1): parse `RWD00` + IG blocks into a per-arm table
   (bytes, loads/stores, calls, private `[rbp-...]` slots, indirect
   jumps) and diff snapshots arm-by-arm - automates §3.2 step 5.
2. `OKOJO_VM_PROFILE` build constant (T2): per-opcode execution counts and
   (prev -> next) pair matrix, `--profile-opcodes` probe flag - the
   data source for any fusion/superinstruction decision.
3. Ceiling-build harness (T5): named probe-only constants for accumulator
   locality and numeric-result canonicalization before invasive designs.
4. `VmLoopIlMap` + `--hold` + `-IlMap` snapshot integration (§5.2).
5. Sampled heatmap script over the IL/native map (§5.3).
6. ETW PMC / VTune wrapper for branch-miss and L1I per dispatched op
   (denominator from item 3).
7. uiCA/llvm-mca reports for short dispatch and arithmetic sequences (T6),
   only after T1 has extracted stable regions.

Do not begin the three-operand superinstruction track until T2 supplies real
opcode-pair frequencies and the accumulator ceiling probe shows enough headroom
to justify a compiler/bytecode change. T3/T4 remain attribution tools, not
reasons to bypass the benchmark gate.
