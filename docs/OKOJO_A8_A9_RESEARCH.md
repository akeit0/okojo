# A8 / A9 Research Findings

Status: RESEARCH ONLY - no engine changes beyond one tooling fix.
Corpus: benchmarks/Okojo.Benchmarks/scripts/*.js (32 files) disassembled with
OkojoBytecodeTool after the metadata fix below. Snapshot:
artifacts/okojobytecodetool/snapshots/20260823-111535-a8a9-research

## Tooling fixes & new bugs (rule: fix tools when research hits gaps)

1. FIXED: BytecodeInfo.GetOperandCount had no entry for narrow CreateClosure;
   tool decoded its operands as instructions (phantom LdaUndefined).
   Engine hand-decodes correctly, so only metadata consumers were affected.
   Lesson reinforced: stale-binary races can mask edits; --no-incremental
   rebuild when behavior does not change after an edit.
2. NEW BUG (open): context-slot operand formatting looks wrong - smi-sum-loop
   shows two writes both printed as `slot:0` (TDZ init of s, then i=0) which
   cannot be semantically right, yet execution is correct. Either
   Disassembler.FormatOperands misprints (slot,depth) pairs or headers lie
   (`context-slots: 0` while CreateFunctionContextWithCells emitted).
   Blocks precise per-slot analysis; must be fixed before deeper A9 work.

## Corpus opcode profile (2457 instructions)

Top frequencies:

| rank | opcode        | share | note |
| ---- | ------------- | ----- | ---- |
| 1    | Star          | 22.6% | acc->reg copy dominates everything |
| 2    | Ldar          | 7.2%  |
| 3    | StaCurrentContextSlot | 6.1% |
| 4    | Return        | 5.1%  |
| 5-6  | LdaUndefined/LdaTheHole | ~8.7% combined init traffic |
| 7    | CreateClosure | 3.5%  |
| 8    | LdaCurrentContextSlot | 3.5% |
| 9    | LdaGlobal     | 3.0%  |
| 11   | LdaNamedProperty | 2.6% |
| 12   | Add           | 2.5%  |

Context-slot family total ~= 10.6% of all instructions.

### Bigram fusion candidates (A8 superinstructions)

| bigram                        | count | candidate form |
| ----------------------------- | ----- | -------------- |
| Star -> LdaNamedProperty      | 53    | GetNamedPropertyTo reg |
| Ldar -> StaCurrentContextSlot | 53    | StaContextSlotFromReg |
| Star -> LdaZero               | 52    | LdaZeroStar reg |
| LdaGlobal -> Star             | 48    | LdaGlobalToReg |
| Add -> Star                   | 47    | AddToReg |
| LdaUndefined -> Star          | 46    | LdaUndefinedStar reg |
| LdaNamedProperty -> Star      | 45    | (same as row 1 pair) |
| LdaZero -> Star               | 37    | LdaZeroStar |
| LdaTheHole -> Star            | 35    | LdaTheHoleStar |

Together these pairs cover ~25% of all instructions. Fusing the top five
forms could remove roughly 10-15% of dispatched opcodes in typical code.

## Dispatch structure (A9) - measured, hypothesis overturned

- Numbering is already dense 0..152 (no gaps).
- RyuJIT lowers the 153-case switch into SIX jump-table clusters
  (reloc RWD00/612/644/660/720/760 = six indirect jmp sites).
- Microbench (tools/VmDispatchMicrobench A9probe): SAME 153-case switch,
  cycle stream with values SPREAD across sub-tables runs 0.32 ns/op vs
  0.49 for values COMPACTED into one cluster (-53% for spread!).
  Interpretation: more indirect-jmp sites => fewer distinct targets per site
  => better per-site BTB learning for cyclic streams. The multi-table split
  is likely HELPING, not hurting.

Conclusion: do NOT renumber/shrink opcodes for dispatch reasons. Case-count
reduction has no measured dispatch benefit; remaining A9 value is metadata
correctness (done above), dead-opcode pruning for pure code size (78 of 153
opcodes never emitted in corpus - verify against test262 corpus before any
removal), and fusion forms listed above (which are compiler-contract changes
owned jointly by A8/A9).

## A8 compiler finding: let-loop context lowering

Repro pair (OkojoBytecodeTool):

- `function nocap(){ let x=1; return x+2; }` -> pure registers, no context.
  Capture analysis works for plain locals.
- `function cap(){ let f=()=>x; ... }` -> CreateFunctionContextWithCells +
  context slots. Correct.
- BUT: `for (let i...)` loops lower the loop variable through context cells
  even with no captures (smi-sum-loop: full
  LdaCurrentContextSlot/Star/StaCurrentContextSlot dance per iteration),
  while the equivalent `var` loop is pure registers
  (Ldar/TestLessThanSmi/Add/Star).

V8 comparison (node --print-bytecode, same loop): V8 keeps `s`,`i` in
registers r0/r1 with per-iteration `Mov` copies - no context involved.

Proposal A8-L1: implement per-iteration copy semantics via register Mov when
the loop variable is not captured; keep context path for captured case.
Expected effect: removes the entire context round-trip per iteration in the
single most common JS loop shape; corpus-wide context-op traffic (~10.6%)
mostly evaporates for non-capturing loops. This aligns with the reference
behavior (V8) and AGENTS priority order.

Follow-ups queued (not started):
- Fix context-op disasm format (blocks verification of A8-L1 results).
- Quantify A8-L1 on bench-ab once implemented (smi-sum-loop expected to drop
  far below current 3ms/100k-iter).
- Fusion forms need compiler+VM contract work (A9-owned) - start with
  LdaZeroStar/LdaTheHoleStar (trivial) then GetNamedPropertyTo (hot IC path).
