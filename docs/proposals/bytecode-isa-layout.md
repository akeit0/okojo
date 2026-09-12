# Bytecode: dead weight out, layout last, schema once

Status: `proposed`. Operand cleanup is concrete and unblocked; layout/schema
work follows the [code/instance split](code-instance-split.md).
Replaces: immediate byte emission in `Bytecode/BytecodeBuilder.cs`, discarded
feedback operands (`Compiler/JsCompilerBase.Emit.cs`, `Execution/JsRealm.VmLoop.cs`),
byte-sized format limits (builder finalization, generator/access emission),
and the dormant private `OptimizeBytecode()`.

## A. Remove unused arithmetic feedback operands (do first)

`EmitRegisterWithSlotOp` writes a zero feedback operand that `Add` and
similar handlers read and discard. For each audited instruction:

```text
narrow today: [Add] [register] [unused zero]  →  [Add] [register]
```

One byte saved per narrow use (two in wide form). Size reduction is certain;
speedup must be measured. Do not reserve the bytes for speculative future
specialization — future feedback gets a deliberate layout when it exists.

## B. Logical instructions with a final layout stage

Pipeline becomes:

```text
resolved function → flat instruction / basic-block form
  → local simplification + register allocation → instruction selection
  → branch/operand-width layout → bytes + finalized PC metadata
```

No SSA infrastructure required: stable instruction IDs, block labels,
virtual registers, and effect annotations suffice. One place then handles
redundant-transfer removal, fused selection, branch widening, and
source/debug/handler mapping after offsets are final. Delete the dormant
`OptimizeBytecode()` or replace it — never merely enable it (per-loop
`ToArray()`, per-instruction operand arrays).

## C. Remove accidental format limits

Branch displacement (s16), one-byte switch-table starts, byte-sized
generator ranges/suspension IDs, signed-byte module-cell indices and
byte-sized context depth all become scalable operand types with short/long
selection at layout. Common operands stay compact; large programs stay
representable. ISA renumbering is acceptable.

## D. Define the instruction set once

One declarative opcode schema (operand types, widths, signedness,
register/accumulator effects, control flow, may-call/may-throw/may-suspend)
generates decoder metadata, disassembler formatting, the verifier, debugger
operand inspection, and reference docs. Hand-written hot handlers remain
where they produce better machine code. This directly answers the repeated
operand-metadata fixes recorded in A8/A9.

## E. Reopen a small fused set

The "no opcode-set expansion" policy that closed the old fusion work is not
carried into the redesign. Trial destination-aware forms (global/property
loads to registers, arithmetic-plus-store, common initializers) chosen from a
**fresh dynamic profile** — fewer accumulator round trips, not a catalog.
Keep a fused form only while reduced dispatch and copying beat extra decode
and native-code footprint.

## Acceptance gate

Audited operand removal with byte-size deltas; layout stage producing
identical semantics with post-layout debug/handler tables; schema-generated
decoder/disassembler/verifier agreeing on operand counts (regression-test the
`CreateClosure`-style metadata gaps); fused forms each with before/after
dynamic-profile evidence.
