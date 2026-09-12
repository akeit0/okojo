# Bytecode (current)

Status: architecture synthesis (base `c651320`). Corpus research:
`../performance/reports/OKOJO_A8_A9_RESEARCH.md` (static, compilation-only
counts — a size signal, not an execution profile).

## Execution model

Register machine with an accumulator. `BytecodeBuilder` emits bytes eagerly;
peephole scope is constrained by already-assigned PCs and debug anchors, and
`IsPositionAnchored` scans bound label positions. There is a private
`OptimizeBytecode()` with no call sites whose decoder copies per instruction —
dormant complexity, not an active bottleneck; the proposal is to delete or
replace it, never to just enable it.

## Known encoding facts

- Some arithmetic/bitwise forms carry a feedback operand that the handler
  reads and discards (emitted as zero by `EmitRegisterWithSlotOp`). Removing
  one audited operand saves a byte per narrow instruction (two in wide form).
  Size win is definite; speed win needs measurement.
- Accumulator/register traffic dominates static counts (`Star` ~22.6%,
  `Ldar` ~7.2%); context-slot traffic (~10.6%) concentrates in `for(let)`
  unconditional alias-slot setup even when uncaptured.
- 78 of 153 opcodes are never emitted in the sampled corpus (pruning needs
  Test262 validation, not just corpus absence).
- Six opcode clusters form six jump tables, which helps cyclic streams
  (see `../performance/OKOJO_VM_OPTIMIZATION_INSIGHTS.md` §§1.1–1.2).

## Accidental format limits (encoding decisions, not engine limits)

| Restriction | Current site | Proposal |
|---|---|---|
| Branch displacement must fit signed 16 bits | builder finalization | short/long selection at layout |
| Switch-table start fits one byte | builder finalization | scalable table index |
| Generator register range / suspension IDs byte-sized | generator emission | resume-table entries, scalable indices |
| Module-cell index signed-byte-sized; context depth byte-sized | access emission | explicit scalable operand types |

Common operands stay compact; uncommon large programs must stay
representable. ISA renumbering is acceptable.

## Tooling

- `tools/OkojoBytecodeTool` — disassembly; cases in
  `artifacts/okojobytecodetool/cases/*.js`, snapshots only under timestamped
  `artifacts/okojobytecodetool/snapshots/<yyyyMMdd-HHmmss>/`.
- `tools/V8BytecodeTool` and `node --print-bytecode` — V8 Ignition reference
  for language/compiler/VM questions.

The structural proposal (logical instructions, final layout stage, declarative
opcode schema, liveness-driven allocation, fused forms from fresh dynamic
profiles) is [../proposals/bytecode-isa-layout.md](../proposals/bytecode-isa-layout.md).
The old "no opcode-set expansion" policy that closed the fusion work is not
carried into the redesign.
