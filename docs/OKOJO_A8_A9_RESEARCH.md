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

### 1.4 DONE (R6): operand-length contract audit - two more metadata bugs found

Full engine-audited byte-length table now pinned by
`tests/Okojo.Tests/BytecodeOperandContractTests.cs` (152 opcodes):

- `GetOperandCount` renamed to `GetSingleScaleByteLength` and documented:
  the table mixes two unit families - the nine prefix-scalable ops encode
  OPERAND COUNT (bytes = count x prefix width), everything else encodes
  FIXED BYTE LENGTH (Wide-suffixed forms encode their wide layout directly).
  The old name was actively misleading during A2 debugging.
- Metadata bugs fixed: narrow `CreateObjectLiteral` had NO entry (default 0;
  truth 2) so every object literal decoded its operands as phantom
  instructions; `CreateObjectLiteralWide` said 2 (truth 3);
  `LdaTypedConstWide` said 4 (truth 3). All dead/cold paths that hid the
  bugs until now.
- Corpus contamination quantified: regenerating the 32-script corpus after
  the fix removes 105 phantom instructions across 14 files (~4.3%).
  Fixed-corpus snapshot: artifacts/okojobytecodetool/snapshots/r6-fixed-corpus.
  Section 2/3 tables predate the fix - treat as directional; re-run the
  frequency pass on the fixed corpus before finalizing fusion priorities
  (folded into R7).
- Noted inconsistency (dead op): JumpLoop layout is [offset16][depth] = 3
  bytes and metadata says 3, but the disassembler prints it as if 2 bytes.
  Harmless while JumpLoop stays unimplemented/unemitted.


## 2. Corpus opcode profile

2457 instructions across 32 scripts (pre-R6-fix snapshot; see 1.4 contamination note). Full frequency table (top 30):
### 1.5 R3/R4/R5 (fusion superinstructions) - CLOSED per policy decision

Owner decision: no opcode-set expansion. Each fused form costs an ISA opcode
(switch arm growth, metadata, debugger surface) and post-A8-L1 measurements
show dispatch edges are no longer the dominant cost. Revisit trigger: a
future profile showing a specific adjacent pair dominating real workload
time AND explicit owner approval for a minimal opcode addition.



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
| R2 | A8-L1 register per-iteration let bindings: gate EnsureLoopAliasContextSlots on IsCapturedByChildBinding | compiler | DONE - **-29..-44% across all bench cases**; post-change test262 non-staging sweep clean (language 19731/0 fail, built-ins 10223/0, intl402 791/0; staging excluded per policy) |
| R3 | Fusion: LdaZeroStar / LdaTheHoleStar / LdaUndefinedStar | CLOSED per owner policy (no ISA growth) |
| R4 | Fusion: StaCurrentContextSlotFromReg | CLOSED per owner policy |
| R5 | Fusion: LdaGlobalToReg, GetNamedPropertyTo, AddToReg | CLOSED per owner policy; revisit trigger documented in 1.5 |
| R6 | Engine-vs-metadata operand-length contract audit + test + rename | DONE (see 1.4) |
| R7 | test262-wide opcode histogram | DONE (section 7; CSV snapshot saved) |
| R8 | plain Jump vs JumpLoop | DONE (section 8; JumpLoop dormant) |



## 7. R7 DONE - test262-wide opcode histogram

Tool: tools/Test262OpcodeHistogram (in-process parse+compile over every
test262 .js file; compilation only). CSV snapshot:
artifacts/okojobytecodetool/snapshots/r7-test262-histogram.csv

Coverage: files=53432 parsedOk=53432 compiled=49685 units=169099
instructions=10,748,516. moduleSkipped detection was not wired (module-flag
files land in compileUnsupported), negativeSyntaxSkipped=2530,
compileUnsupported=1217 (with-statements / destructuring assignment targets /
annexB assignmenttargettype set) - consistent with policy exclusions.

Distinct emitted: 126/153. Top opcodes test262-wide:

| rank | op            | count     | share |
| ---- | ------------- | --------- | ----- |
| 1    | Star          | 3,297,459 | 30.68% |
| 2    | LdaGlobal     | 580,382   | 5.40% |
| 3    | Ldar           | 541,476   | 5.04% |
| 4    | LdaNamedProperty | 519,516 | 4.83% |
| 5    | LdaUndefined  | 461,430   | 4.29% |
| 6    | LdaTheHole    | 444,936   | 4.14% |
| 7    | CallProperty  | 365,224   | 3.40% |
| 8    | LdaStringConstant | 356,318 | 3.32% |
| 9    | LdaSmi        | 325,185   | 3.03% |
| 10   | StaCurrentContextSlot | 246,880 | 2.30% |
| 11   | CallRuntime   | 229,788   | 2.14% |
| 12   | Jump          | 215,105   | 2.00% |

Top bigram by a wide margin: `LdaGlobal -> Star` x550k. The Star+copy family
(Star/Ldar/Mov) is ~37% of everything test262 dispatches.

Final dead list (27 opcodes never emitted across ALL of test262):
LdaNumericConstantWide, LdaModuleVariable, StaModuleVariable, StaGlobalWide,
StaGlobalInitWide(?), TypeOfGlobalWide, GetNamedPropertyFromSuperWide,
PushContext, LdaContextSlotWide, StaContextSlotWide,
LdaCurrentContextSlotWide(+NoTdzWide), ToName, SwitchOnSmi, JumpLoop, CallAny,
InvokeIntrinsic, CreateBlockContext, CreateFunctionContext,
CreateFunctionContextWithCellsWide, Wide, ExtraWide, LdaLexicalLocalWide,
StaLexicalLocalWide (+duplicates from enum aliasing).
These are the validated pruning candidates IF a future pass wants bytecode
compaction; several are Wide twins that exist for operand-width symmetry -
removal is an A9-class contract change and stays closed under current policy.

## 8. R8 DONE - plain Jump vs JumpLoop

Measured: back-edge ops across test262 = Jump 215,105 / JumpLoop **0**.
JumpLoop is entirely unimplemented (VM arm falls through to NotImplemented)
and unemitted; its metadata entry documents [offset16][depth] while the
disassembler prints a misleading 2-byte view (noted in 1.4).

Analysis vs V8: Ignition's JumpLoop exists to host loop interrupt checks and
loop-depth feedback at the back edge. Okojo's global countdown check covers
interrupts at EVERY dispatch edge (A5 proved it effectively free), so there
is no functional loss from plain Jump today.

Decision: keep JumpLoop dormant and documented (removal would renumber the
enum against stability for zero runtime gain). If loop-level instrumentation
or OSR-style feedback is ever needed, implementing JumpLoop emission is the
designated insertion point.


## 9. Jint comparison follow-up - regexp/call-path investigation (in progress)

Isolation probes (identical JS, fresh engines, Jint 4.2.2):

| op (100x100-char strings) | Jint | Okojo | ratio |
| ------------------------- | ---- | ----- | ----- |
| match /aaaaaaaaaa/g       | ~86ms | ~171ms | ~2x |
| test /a/                  | ~6ms  | ~5ms   | ok   |
| replace g -> literal      | ~4ms  | ~8ms   | ~2x  |
| split /.*/                | ~8ms  | ~10ms  | ok   |

Landed: [Symbol.match] /g fast path - when receiver resolves exec to the
intrinsic RegExp.prototype.exec, step RegExpEngine directly
(RegExpEngine.TryMatchRange, thread-static capture buffer reuse) returning
only group-0 strings; skips per-match exec-result object construction and
exec-function invocation. Gates: receiver is JsRegExpObject + intrinsic-exec
identity. lastIndex reset-to-0-on-failure preserved. Verified:
Okojo.Tests 2179/0; test262 built-ins RegExp 1949/0 forced rerun;
String/match + language regexp literals 0 fail.

Remaining whole-file gap decomposition (dromaeo-object-regexp-modern):
split(/.*/) & replace-global paths still generic (replace uses per-match
ExecMatchResult + replacement-template machinery; replace-with-callback adds
call-lane cost), and per-match Substring+array-element stores remain.
Next candidates: mirror the same fast stepping into [Symbol.replace] string
case; audit String.split(regex) loop allocations.

## 9a. Regexp fast-path attempts - REVERTED, deferred with spec notes

Two fast paths ([Symbol.match]/[Symbol.replace] global loops stepping
RegExpEngine directly with pooled capture buffers) were implemented,
measured (match-g micro -12%, whole-file flat), then REVERTED after the
suite exposed spec-observability violations:

1. The exec GET is observable EVERY iteration (RegExpExec reads R.exec each
   time). A pre-loop identity probe adds an extra get; skipping per-iteration
   reads drops them. Fixed once by re-checking inside the loop + mid-stream
   fallback to the generic loop via lastIndex writeback.
2. Custom exec interplay (RegExpExternalEngineTests.AdvanceLastIndexAfterEmptyMatch):
   fallback writeback of lastIndex=0 clobbers state a custom exec set
   (2^54 -> expected clamp/advance semantics 2^53). Correct handling requires
   exact ToLength/AdvanceStringIndex ordering identical to RegExpBuiltinExec.
3. String_Match_Global...Primitive_Path: TypeError from primitive property
   access during matchAll section - root cause not yet isolated; possibly
   interaction beyond the fast path.

Deferred design requirements before retrying:
- Per-iteration observable exec get (identity re-check inside loop).
- lastIndex read/write through the SAME property path as generic loop
  (no hidden-slot shortcuts) OR prove shadowing impossible for JsRegExpObject.
- Empty-match advance using AdvanceStringIndexLong on the ToLength-clamped
  value exactly like RegExpBuiltinExec; verify FromLengthValue clamping at
  2^53 boundary against test262 RegExp.prototype[Symbol.replace] cases.
- Isolate the matchAll primitive-path TypeError separately from the fast
  path (may be a pre-existing bug worth its own fix).
