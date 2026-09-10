# Redesign proposals

Status: `proposed` unless stated otherwise. Distilled from the 2026-09-10
structural review (`artifacts/review/REVIEW_2026_09_10.md`); each page names
the current sources it would replace and the gate that accepts it. These are
work orders with reasoning, not implementation claims — verify against source
before building.

| Proposal | Priority | Status |
|---|---|---|
| [code-instance-split.md](code-instance-split.md) — immutable code vs realm state vs closures vs debug info | highest architectural | proposed |
| [compiler-lazy-bindings.md](compiler-lazy-bindings.md) — one binding analysis + lazy nested compilation | highest compiler | proposed |
| [bytecode-isa-layout.md](bytecode-isa-layout.md) — dead operands now; logical instructions + final layout + opcode schema next | immediate + next | proposed (operand removal is concrete and unblocked) |
| [liveness-suspension.md](liveness-suspension.md) — register liveness driving reuse, snapshots, lifetimes | next | proposed |
| [runtime-caches-arrays-calls.md](runtime-caches-arrays-calls.md) — polymorphic caches, packed/holey arrays, argument window | next runtime | proposed |
| [exception-handling.md](exception-handling.md) — cold wrapper + static handler tables as measured experiments | experiment | proposed |
| [debug-info-redesign.md](debug-info-redesign.md) — source/debug separation, scopes, two defect fixes, breakpoint ownership | parallel + immediate fixes | proposed (defect fixes unblocked) |
| [conformance-gate.md](conformance-gate.md) — fresh-baseline gate, not frozen representations | parallel | proposed |
| `OKOJO_EH_SPLIT_DESIGN.md` (this dir) — EH-split input sketch | input, partly stale | see exception-handling.md |
| `OKOJO_C3_TDZ_ELISION_NOTE.md` (this dir) — block-scope hole-init elision | accepted iteration | active plan |
| `OKOJO_C4_COMPLETION_ELISION_NOTE.md` (this dir) — root completion-sink elision | accepted iteration | active plan |

## Review priority order (preserved)

1. Land the small concrete fixes first (unused operands, line-terminals,
   synthetic-local flag, delete dormant optimizer) and establish a fresh
   conformance baseline — the trustworthy gate for everything after.
2. Build the code/instance/source split together with persistent function
   analysis and lazy compilation.
3. Then logical instruction layout plus liveness — unlocking bytecode,
   generator state, and call ABI together.

Public APIs, `JsScript` construction, the function-object hierarchy, opcode
numbering, and frame layout are changeable. Currently passing JavaScript
behavior is not.

## Still-missing input notes (accepted gaps)

- `OKOJO_NODE_COMMONJS_RESOLUTION_NOTE.md` (referenced by the Node runtime
  plan, never written). The plan's link list is fixed to resolve; the note
  itself is still owed.
