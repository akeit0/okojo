# A8 / A9 Research - Full Findings

Status: RESEARCH phase complete; implementation gated per proposal list at
the end. No engine behavior changes have landed from this document except
one bytecode-metadata fix (section 1.1).

Research corpus: benchmarks/Okojo.Benchmarks/scripts/*.js (32 files),
disassembled with OkojoBytecodeTool into
artifacts/okojobytecodetool/snapshots/20260823-111535-a8a9-research/
after the metadata fix in section 1.1.

Reference engine for comparison: V8 Ignition via `node --print-bytecode`
(tools/V8BytecodeTool) on identical sources.

## 0. Executive summary

1. Register-copy traffic dominates the instruction stream: `Star` alone is
   22.6% of all dispatched opcodes, `Ldar` another 7.2%. The accumulator
   model inherently produces these copies; superinstruction fusion is the
   lever.
2. Context-slot traffic totals ~10.6% and is concentrated in one avoidable
   pattern: `for (let ...)` loops lower their loop variable through context
   cells even when nothing captures it (A8-L1).
3. The dispatch switch lowers to six jump-table clusters; measurement shows
   this multi-site layout HELPS cyclic streams (per-site BTB target sets).
   Opcode renumbering/shrinking for dispatch reasons is dead.
4. 78 of 153 opcodes were never emitted by the corpus; pruning candidates
   exist but require test262-wide validation.
5. Two disassembler/metadata defects found and one fixed; a third (context
   operand formatting) blocks precise per-slot analysis and must be fixed
   before A8-L1 can be verified properly.

## 1. Tooling findings (rule: fix tools when research hits gaps)

### 1.1 FIXED: GetOperandCount missing narrow CreateClosure

BytecodeInfo.GetOperandCount had an entry only for CreateClosureWide (=3).
Narrow CreateClosure ([idx][flags], 2 operands) fell through to the default,
so every metadata consumer treated it as zero-operand:

- OkojoBytecodeTool decoded its operands as instructions: after
  `0000 CreateClosure` came phantom `0001 LdaUndefined / 0002 LdaUndefined`
  (the two zero bytes), which also shifted all subsequent offsets.
- Frequency statistics computed from such listings would be wrong.
- Potential debugger/source-map consumers of BytecodeInfo were silently
  mis-decoding as well.

The ENGINE was never affected: opcode handlers hand-code operand consumption
(HandleCreateClosure advances pcOffset by 3 total). This split between
hand-coded lengths (engine) and table-driven lengths (metadata) is the root
cause class; section 4 proposes a single-source-of-truth audit to close it.

Fix landed with this research: `or JsOpCode.CreateClosure` added to the
two-operand arm. Verified: `function f(){}; f;` now prints
CreateClosure idx:0 flags:0 at 0000 and StaGlobalFuncDecl at 0003, matching
the raw byte dump [123,0,0,29,...] captured during A2 debugging.

### 1.2 RESOLVED (R1): context-slot mystery - formatter innocent, allocation unconditional

Instrumentation outcome (temporary [diag] probes in
MarkCapturedByChildBinding / EnsureCurrentContextSlotForLocal / both capture
fallbacks, all removed after):

1. Disassembler operand formatting for the CurrentContextSlot family is
   CORRECT - it prints exactly the byte the engine decodes. The confusing
   smi-sum-loop listing is faithful bytecode:
   - slot0 = the loop variable i (TDZ hole write, then init 0, read x2 and
     writeback x1 per iteration),
   - while s lives entirely in register r0,
   - plus a dead `LdaTheHole / Star r1` prologue pair (r1 never read).
   The earlier reading ("two variables sharing slot 0") was wrong.
2. The `context-slots: 0` header was the tool passing a hardcoded default:
   DisassemblerOptions.ContextSlots was never populated and JsScript carries
   no such field. FIXED in Disassembler.Dump via ResolveContextSlots: when
   the caller does not supply a value, derive it from
   CreateFunctionContextWithCells / CreateFunctionContext operands by
   pre-scanning the bytecode. Script units without contexts still print 0;
   functions now print their true cell count.
3. THE REAL FINDING (refines A8-L1): `EnsureLoopAliasContextSlots`
   (JsCompiler.cs) unconditionally allocates context slots for EVERY
   for-head lexical binding and every for-in/of head lexical via
   `EnsureAliasBindingContextSlots` - with NO capture check. The per-iteration
   ROTATION stays gated on captures (ShouldUsePerIterationContextForForLoop
   requires IsCapturedByChildBinding, verified no marker fires for
   smiSumLoop), but slot ALLOCATION does not. Net effect: any function
   containing `for (let ...)` gets a function context with cells, and the
   head variable round-trips the cell even when rotation never activates and
   nothing captures it.

A8-L1 refined implementation shape: gate alias-slot allocation on
IsCapturedByChildBinding(symbolId) (matching the rotation gate), keeping the
context path whenever captures exist. Verification unchanged: fixed-format
disasm must show zero context ops in non-capturing loops; bench-ab after.

### 1.3 Reinforced: stale binaries mask edits

The 1.1 fix appeared ineffective twice until rebuilt --no-incremental
(known AGENTS file-lock/copy race). Standing rule: when behavior does not
change after an edit, suspect the binary before the edit.

## 2. Corpus opcode profile

2457 instructions across 32 scripts. Full frequency table (top 30):

| rank | op                        | count | share |
| ---- | ------------------------- | ----- | ----- |
| 1    | Star                      | 556   | 22.6% |
| 2    | Ldar                      | 178   | 7.2%  |
| 3    | StaCurrentContextSlot     | 150   | 6.1%  |
| 4    | Return                    | 125   | 5.1%  |
| 5    | LdaUndefined              | 107   | 4.4%  |
| 6    | LdaTheHole                | 106   | 4.3%  |
| 7    | CreateClosure             | 87    | 3.5%  |
| 8    | LdaCurrentContextSlot     | 86    | 3.5%  |
| 9    | LdaGlobal                 | 74    | 3.0%  |
| 10   | LdaZero                   | 72    | 2.9%  |
| 11   | LdaNamedProperty          | 63    | 2.6%  |
| 12   | Add                       | 62    | 2.5%  |
| 13   | Jump                      | 59    | 2.4%  |
| 14   | CreateFunctionContextWithCells | 50 | 2.0% |
| 15   | LdaContextSlot            | 47    | 1.9%  |
| 16   | CallUndefinedReceiver     | 43    | 1.8%  |
| 17   | CallRuntime               | 43    | 1.8%  |
| 18   | LdaSmi                    | 42    | 1.7%  |
| 19   | CallProperty              | 41    | 1.7%  |
| 20   | StaGlobalFuncDecl         | 34    | 1.4%  |

Observations beyond the ranking:

- Copy traffic (Star + Ldar + Mov) ~= 30.3%. Every accumulator-model
  operation pays a copy when its result targets a register or slot.
- Initialization traffic is huge: LdaUndefined/LdaTheHole/LdaZero plus their
  trailing Stars cover ~14% of instructions (TDZ inits, defaults, hoisting).
- Context family (StaCCS + LdaCCS + variants + CFCWC) ~= 10.6%, dominated by
  the let-loop pattern of section 5.
- Calls (CallUndefinedReceiver + CallProperty + CallRuntime + Construct)
  ~= 5.6%, always preceded by register-setup Stars (Star -> CallX bigrams:
  42 + 41 alone).

## 3. Bigram analysis - fusion candidates (A8 superinstructions)

Top adjacent pairs within each instruction unit (2311 bigrams total):

| bigram                        | count | candidate form |
| ----------------------------- | ----- | -------------- |
| Star -> LdaNamedProperty      | 53    | GetNamedPropertyTo rX |
| Ldar -> StaCurrentContextSlot | 53    | StaCurrentContextSlotFromReg rX |
| Star -> LdaZero               | 52    | LdaZeroStar rX |
| LdaGlobal -> Star             | 48    | LdaGlobalToReg rX |
| Add -> Star                   | 47    | AddToReg rX |
| LdaUndefined -> Star          | 46    | LdaUndefinedStar rX |
| LdaNamedProperty -> Star      | 45    | (same site as row 1) |
| Ldar -> Star? (via ToNumeric) | ~40   | Inc-family fusion below |
| Star -> CallUndefinedReceiver | 42    | call arg setup - see note |
| Star -> CallProperty          | 41    | see note |
| LdaZero -> Star               | 37    | LdaZeroStar |
| LdaTheHole -> Star            | 35    | LdaTheHoleStar |

Note on Star -> CallX: calls take callee from a register
(`CallAny func:r0, args:r0..`), so the Star materializes the callee.
A CallTo variant would fuse, but call arms are the heaviest handlers;
fusion there needs care to stay on the fast path.

Priority order by (count x implementation simplicity):

1. LdaZeroStar / LdaTheHoleStar / LdaUndefinedStar - trivial compiler +
   trivial VM arm; kills ~170 instructions per 2457 (~7%).
2. StaCurrentContextSlotFromReg - kills the biggest context pattern.
3. LdaGlobalToReg / GetNamedPropertyTo - hot IC paths; must preserve IC
   update semantics exactly (slot feedback unchanged).
4. AddToReg - arithmetic family; keep smi/double fast paths inline.

Expected combined effect if 1-4 land: -10..15% dispatched opcodes in typical
code, directly multiplying everything else we optimized (dispatch edges are
what the whole campaign's fixed costs amortize over).

## 4. Dispatch structure research (A9) - hypothesis overturned by data

Questions asked: are opcode numbers badly laid out? Does the 153-case switch
split hurt? Should we renumber or shrink the set?

Findings:

1. Numbering is already perfectly dense: values 0..152, no gaps (parsed from
   JsOpCode.cs). Renumbering has nothing to fix on that axis.
2. RyuJIT partitions the switch into SIX jump-table clusters (see Insights
   1.1). My initial hypothesis blamed a numbering gap; wrong - the split is
   a lowering heuristic for large case counts.
3. Measured effect of clustering (A9probe, tools/VmDispatchMicrobench):
   SAME 153-case switch method, cycle streams differing only in value
   spread. SPREAD across clusters: 0.32 ns/op. COMPACT into one cluster:
   0.49 ns/op. Spread wins by ~53% (stable across runs).
   Interpretation: each indirect-jmp site owns predictor resources; spread
   values give sites fewer distinct targets, so cyclic streams predict
   near-perfectly. The multi-table split is likely BENEFICIAL for real JS
   loops.
4. Therefore: shrinking/reordering the opcode set FOR DISPATCH is dead.
   Remaining A9 value:
   - metadata correctness (1.1 done; add an automated engine-vs-table
     operand-length audit so drift cannot recur),
   - dead-opcode pruning purely for code size (below), 
   - fusion forms of section 3 (compiler+VM contract work).

Dead-opcode candidates (never emitted in this corpus): BitwiseAnd/Or/Xor/Not,
CallAny, CreateBlockContext, CreateClosureWide, CreateEmptyArrayLiteral,
CreateFunctionContext(+Wide), CreateMappedArguments, CreateObjectLiteralWide,
CreateRestParameter, Debugger, Dec, DefineOwnKeyedProperty, Exp,
ForInEnumerate/Next/Step, GetNamedPropertyFromSuper(+Wide),
InitializeArrayElement, InitPrivateMethod, InvokeIntrinsic, JumpIfNull,
JumpLoop(!), all *Wide context/global/local variants, LdaLexicalLocal(+Wide),
LdaModuleVariable/StaModuleVariable, LdaNamedPropertyWide, LdaNewTarget,
LdaNumericConstantWide, LdaTypedConst(+Wide), LogicalNot, Negate, PushContext,
ShiftLeft/RightLogical, StaGlobalInit(+Wide), StaGlobalWide, SwitchOnSmi,
TestEqual/NotEqual/GreaterThan/GreaterThanOrEqual(Smi), TestIn,
TestInstanceOf, ToName/Number/String/TypeOf, TypeOfGlobal(+Wide), Wide.

Caveats before pruning ANY of them: the corpus is micro-benchmarks; test262
exercises far more surface (generators use JumpLoop/Suspend/Resume, regexes
use InvokeIntrinsic...). JumpLoop appearing unused while plain Jump carries
all back-edges is itself notable - the compiler currently emits plain Jump
for loops, losing V8's dedicated back-edge interrupt point; that interacts
with A5's finding and deserves its own note if loop-interrupt semantics ever
change. Pruning should wait for a test262-wide histogram tool run.

## 5. Compiler lowering research (A8) - the let-loop context pattern

Capture analysis is correct for plain locals:

- `function nocap(){ let x=1; return x+2; }`
  -> registers only: LdaSmi/Star r0/AddSmi. No context created.
- `function cap(){ let f=()=>x; let x=1; return f()+x; }`
  -> CreateFunctionContextWithCells + context slot for x, because f captures
  it. Correct per spec.

But the for-loop HEAD variable goes through context regardless:

```
// function smiSumLoop(){ let s=0; for(let i=0;i<100000;i++){ s+=i; } return s; }
CreateFunctionContextWithCells slots:1
LdaTheHole / StaCurrentContextSlot slot:?      ; TDZ
LdaZero / Star r0                              ; s lives in r0!
LdaTheHole / Star r1                           ; i TDZ in r1?
LdaZero / StaCurrentContextSlot slot:?         ; but i ALSO written to ctx?
loop body per iteration:
  LdaCurrentContextSlot -> Star r2             ; read i from ctx
  TestLessThan r2 / JumpIfFalse
  LdaCurrentContextSlot -> Add r0 -> Star r0   ; s += i (s in reg!)
  LdaCurrentContextSlot -> ToNumeric -> Inc -> Star r2
  LdaCurrentContextSlot -> Ldar r2 -> StaCurrentContextSlot   ; writeback i
  Jump
```

Note the mixed regime: s appears register-resident while i round-trips the
context every iteration (read x2, write x1 per loop). Per spec, `let` loop
variables need a fresh binding per iteration ONLY for closure capture
correctness; when nothing captures i, register copies satisfy semantics -
exactly what V8 does for the same source:

V8 Ignition output for the identical loop keeps BOTH variables in registers:

```
LdaZero / Star0            ; s
LdaZero / Star1            ; i
LdaSmi [10] / TestLessThan r1 / JumpIfFalse
Ldar r1 / Add r0, [1] / Mov r0, r2 / Star0
Ldar r1 / Inc [2] / Star1
JumpLoop [21], [0], [3]
```

No context, no slot traffic, and note V8 uses JumpLoop (dedicated back-edge
with interrupt slot + loop depth) where Okojo emits plain Jump.

Proposal A8-L1: capture-aware per-iteration copies. When the loop-head
variable is not captured by any closure inside the loop, implement fresh-
binding semantics with register Mov copies instead of context cells. Keep
the context path whenever any capture exists (per-iteration closures must
each see their own binding).

Impact estimate: removes ~3 context ops + 2 Stars per loop iteration in the
single most common JS shape; corpus context traffic (~10.6%) mostly
evaporates for non-capturing loops; smi-sum-loop-style dispatch counts drop
by roughly a third.

Verification gate: fix 1.2 first (trustworthy slot printing), then rerun
disasm to confirm zero context ops in the nocapture loop, then bench-ab
(smi-sum-loop expected well below current ~3ms per 100k-iteration execute;
for-loop-sum and lexical-block should follow).

## 6. Proposal backlog produced by this research

| id | proposal | owner | gate |
| -- | -------- | ----- | ---- |
| R1 | Disassembler context-op formatting + header derivation + root-cause of let-loop context allocation | tooling/compiler | DONE (this branch) |
| R2 | A8-L1 register per-iteration let bindings: gate EnsureLoopAliasContextSlots on IsCapturedByChildBinding | compiler | DONE - **-29..-44% across all bench cases** (see a8l1 notice) |
| R3 | Fusion: LdaZeroStar / LdaTheHoleStar / LdaUndefinedStar | compiler+VM contract | bench-ab + test262 |
| R4 | Fusion: StaCurrentContextSlotFromReg | compiler+VM contract | R1, bench-ab |
| R5 | Fusion: LdaGlobalToReg, GetNamedPropertyTo, AddToReg | compiler+VM contract | after R3/R4 experience |
| R6 | Engine-vs-metadata operand-length audit (single source of truth) | tooling | none |
| R7 | test262-wide opcode histogram before any pruning | tooling | none |
| R8 | Investigate plain-Jump vs JumpLoop back-edge semantics vs V8 | design note | after R7 |
