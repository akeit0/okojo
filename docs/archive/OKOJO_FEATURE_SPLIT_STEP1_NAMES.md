# Split step 1: symbolic name table in the builder

Status: implemented (2026-09-10).
Scope for this iteration: `BytecodeBuilder` records pooled names
symbolically (`string`) and interns them once at finalization, in first-add
order. No change to emitted bytes, pool order, VM behavior, or public
compile results. Implements `OKOJO_CODE_INSTANCE_SPLIT_DESIGN.md` step 1
(first half: emission stops touching the realm atom table for pooling).

## Why this slice

Deferring pool interning to finalization removes the realm atom table from
the hot emit path and makes the builder's name product a plain string table
— the shape the future link step consumes. Realm *queries* (shape
transitions, global-declaration checks) keep interning live; those move at
link time (design steps 2/6), not here.

## What changes

- New builder state: `List<string>` symbolic names + rented
  `Dictionary<string, int>` dedup past the existing
  `ConstantDedupDictionaryThreshold`, mirroring the int-table pattern.
- `AddAtomizedStringConstant(string)`: canonical-index rejection, then
  record/dedup by string; returns the pool index; no interning.
- `AddAtomizedStringConstant(int)` (zero in-repo callers): deleted.
- Finalization: intern distinct strings in first-add order
  (`InternNoCheck` is idempotent, so pool contents equal today's);
  `AtomizedStringConstants` keeps its meaning (atom ids).
- Dedup-by-string ≡ dedup-by-atom (atom↔string is 1:1 per realm), so pool
  order and emitted operands are unchanged.

## Planned tests

- Existing suites unchanged (dedup test in `ToolingTests` exercises the
  same index-stability contract).
- `OkojoBytecodeTool` before/after on `cases/` + linq-js: identical unit
  counts, registers, constants, opcode/operand sequences.
- `CompilerAllocProbe` before/after (baselines below); allocation is the
  primary gate, timing must not regress beyond noise.

## Baselines (2026-09-10, pre-change)

- Default corpus: `compile(full)` 16.25 KB/op, 83.51 us/op.
- linq-js (462 units): `compile(full)` 1626.77 KB/op, 11050.67 us/op.

## Reference observations

- V8/Node: none — internal emission ordering, zero observable delta by
  construction (operands carry pool indices, never atom ids).

## Copy vs intentional difference

None.

## Perf plan

Hot path: per-add work swaps an atom-table hit + int-pool dedup for a
string-pool dedup; one intern per distinct name moves to finalization.
Risk: string hashing/dedup costs more per add than int dedup. Accept only
on measured parity: allocation primary, timing within noise on both
corpora. If it regresses, keep eager interning and carry a parallel
symbolic table instead (more memory, same boundary) — measure before
choosing.

## Verification (2026-09-10)

- `OkojoBytecodeTool` before/after on repros: byte-identical disassembly
  (pool order and operands unchanged, as designed).
- `CompilerAllocProbe` (fresh processes, medians):
  - default corpus: 16.25 → 16.35 KB/op (+0.6%, ~100 B/op — noise floor),
    83.51 → 82.01 us/op;
  - linq-js (462 units): 1626.77 → 1631.25 KB/op (+0.27%),
    11050.67 → 10981.51 us/op (−0.6%); units and array payload identical.
  - Gate holds: allocation within noise, timing not regressed.
- Suites: `Okojo.Tests` 2221/2225, Compiler 362/362, Node 115/116 — green
  (incl. the existing builder dedup-index contract test, unchanged).
- Deleted the zero-caller `AddAtomizedStringConstant(int)` overload.

## Deferred

- Realm-query interning (shapes, declaration checks): design steps 2/6.
- Builder pool rental (`RentCompile*`): still realm-sourced; inject later.
- Per-realm link interning: needs the instance concept (design step 3+).
