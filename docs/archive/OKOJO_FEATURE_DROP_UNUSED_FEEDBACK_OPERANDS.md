# Drop unused feedback operands from arithmetic/bitwise/test opcodes

Status: implemented.
Scope for this iteration: remove the dead trailing operand emitted by
`EmitRegisterWithSlotOp` / `EmitImmediateWithSlotOp` (and two direct-emission
sites) for 30 opcodes; update the 15 VM discard sites, `BytecodeInfo` counts,
the disassembler, and the operand contract test. No semantic change, no new
opcodes, no fused forms (those belong to `bytecode-isa-layout.md` §E).

## Minimal repros

```js
function f(a, b) { return a + b - a * b; } f(1, 2);
```

Before (`OkojoBytecodeTool`):

```text
0000  Ldar r1
0002  Add r0, slot:0
0005  Star r2
```

After (expected):

```text
0000  Ldar r1
0002  Add r0
0004  Star r2
```

`flat_ast_arithmetic.js` (`x += 2`): `AddSmi imm:2, slot:0` (3 bytes) becomes
`AddSmi imm:2` (2 bytes).

## Affected opcodes (audited)

Register family, via `EmitRegisterWithSlotOp`
(`JsCompilerBase.Emit.cs:357`) — VM discards operand 2 in every handler:

Add, Sub, Mul, Div, Mod, Exp, BitwiseAnd, BitwiseOr, BitwiseXor, ShiftLeft,
ShiftRight, ShiftRightLogical, TestEqual, TestNotEqual, TestEqualStrict,
TestLessThan, TestGreaterThan, TestLessThanOrEqual, TestGreaterThanOrEqual,
TestIn, TestInstanceOf (21).

Smi family, via `EmitImmediateWithSlotOp` (`JsCompilerBase.Emit.cs:368`) —
VM skips byte 2 in every handler:

AddSmi, SubSmi, MulSmi, ModSmi, ExpSmi, TestLessThanSmi, TestGreaterThanSmi,
TestLessThanOrEqualSmi, TestGreaterThanOrEqualSmi (9).

Two direct-emission sites carry the same dead zero and change with it:
`JsCompilerBase.Statements.cs:454` (`TestLessThanSmi, 0, 0`) and `:518`
(`TestLessThan, indexRegister, 0`).

## Required companion fix (decoder consistency)

`BytecodeInfo.GetOperandByteCount(op, Wide)` returns the *unscaled* count for
ops outside `SupportsOperandScalePrefix`, while the VM loop scales every
operand read under a `Wide` prefix. The static decoder therefore already
mis-decodes wide arithmetic forms (decoder: 2 bytes, VM: 4 bytes). After this
change the register operand still scales in the VM, so the 20
register-family ops (TestEqualStrict is already listed) must join
`SupportsOperandScalePrefix`: then wide decodes as 1 operand × 2 bytes,
matching the VM. Smi ops stay out — the emitter never prefixes them and
their handlers read raw bytes. This fixes the latent wide-decode mismatch as
a side effect; behavior for narrow forms is unchanged.

## Planned tests

- `BytecodeOperandContractTests.cs` — update `ExpectedByteLength` (30 ops
  2→1) and `ScalableOps` (+20 register-family ops) in the same change.
- New focused tests in `ArithmeticTests.cs`: compile `a + b` / `x += 2` and
  assert the single-operand disassembly (`Add r0`, `AddSmi imm:2`), plus a
  300-local wide-register case proving the scaled `Add` executes and the
  decoder stays aligned (no `<truncated>`). Deliberately no negative
  "slot must be absent" assertions — absence checks on formatting text are
  brittle; the positive encoding plus the contract table pin the shape.
- Full suite (`Okojo.Tests`, no-build after focused pass) plus the Test262
  conformance slices as the behavior-preservation gate; re-run
  `OkojoBytecodeTool` on the two repros and the `cases/` corpus for
  size deltas.

## Reference observations

- V8/Node: not applicable as behavior references — zero observable JS
  semantics change by construction. The before/after disassembly identity
  (modulo the removed operand) plus green Test262 is the evidence.
- Design analogy only: V8 keeps type feedback in a side-table feedback
  vector, not inline in the bytecode stream — consistent with not reserving
  speculative inline bytes here.

## Copy vs intentional difference

Encoding differs from the old Okojo stream by design (1 byte narrow / 2 bytes
wide saved per affected instruction). Execution semantics identical: every
removed operand read was a proven discard (emitter always wrote zero).

## Perf plan

Hot path: one fewer operand read and one fewer `pc` advance per affected
dispatch; smaller win than the guaranteed size reduction. Slow path: none
(handlers keep their shape, minus the discard line). Allocation: none — this
removes bytes, it does not add objects. Measure size delta on the `cases/`
corpus; do not claim dispatch-speed wins without `bench-ab` medians.

## Verification (2026-09-10)

- `Okojo.Tests`: 2207 passed, 0 failed, 4 skipped (2211 total, incl. 3 new
  encoding tests). Focused-first, then full `--no-build` pass.
- `Okojo.Compiler.Tests`: 362/362. `Okojo.DebugServer.Tests`: 24/24.
- `Okojo.Node.Tests`: 113 passed, 1 pre-existing environment failure
  (`OkojoNode_EnableSourceMaps_Remap_StackTrace_To_Original_Source` — fails
  identically on the pristine tree in this environment; CLI stack frames
  render without file locations with and without this change), 1 skipped.
  Zero new failures vs pristine.
- Test262 full run: 42617 passed / 437 failed / 10378 skipped with this
  change vs 42618 / 436 / 10378 pristine. Every failing path in both runs is
  under `test262/test/staging/sm/` (policy-excluded staging); the staging
  failure set itself is flaky across runs (parallel harness-include timing,
  e.g. `typedArrayConstructors is not defined` comes and goes). Zero
  non-staging failures in either run — the passing baseline is preserved.
- `OkojoBytecodeTool` before/after on the two repros matches this note;
  `eng/check-doc-links.py` clean.

## Deferred

- Fused destination-aware forms (`bytecode-isa-layout.md` §E) — needs a
  fresh dynamic profile first.
- Deleting dormant `OptimizeBytecode()` — adjacent but independent; separate
  tiny change.
- Debug defects (line terminals, `$` filtering) and the fresh conformance
  baseline — same "first" batch, separate iterations.
