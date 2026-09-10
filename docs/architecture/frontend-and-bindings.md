# Frontend and bindings (current)

Status: architecture synthesis (base `c651320`). Source detail lives in
`frontend/OKOJO_DIRECT_PARSER.md`, `frontend/OKOJO_COMPILER_THROUGHPUT_DESIGN.md`,
and `frontend/OKOJO_MULTI_PASS_COMPILER_DESIGN.md`.

## Parser

`JsLexer → JavaScriptParser → JsAst` is the only production path. There is no
fallback second parser; unsupported grammar is rejected directly. The AST is
flat: one compile owner, dense node IDs (`-1` = absent), post-order layout
with pooled side tables, syntax-only (no scope facts attached by parsing).

Reference policy for language work is V8 first (semantics/bytecode shape),
Oxc second (cheap frontend ideas).

## Compiler passes

Long-term shape is multi-pass on dense IDs and pools, never emit-while-parsing:

```text
scope/binding discovery → resolution/capture → storage classification
  → storage planning → register allocation → emission
```

The parser stays syntax-only. Emission consumes a plan; it must not
rediscover scope relationships. Recorded pre-cutover wins for the flat path:
parse ~0.5–0.7× time, compile 1.4–2.1× faster at 1.7–3.2× less allocation,
plus `BytecodeBuilder` disposal, pooled lists, lazy parameter maps, and shared
source ownership (see `../performance/reports/OKOJO_FRONTEND_ALLOCATION_PROFILE_20260830.md`).

The frontend keeps value/effect/test expression contexts. Indiscriminate
collection pooling and scanner experiments were tried and rejected — see the
allocation report before retrying them.

## Bindings today

Binding collection walks nested function bodies; nested compilation
re-collects; emission re-searches scopes and binding lists; child-capture
preparation re-sweeps active bindings. These overlapping views are correct
but repeated, and each nested unit pays binding work plus emission, array
materialization, and registration (462 units on the linq-js corpus).

The synthesis proposal replaces them with one indexed analysis product
(`ReferenceId → BindingId → scope/flags/capture`, `ScopeId → parent/owner`,
`FunctionId → parameters/captures/nested/source range`) plus per-child
capture descriptions and lazy nested compilation. That is a proposal, not
current behavior: [../proposals/compiler-lazy-bindings.md](../proposals/compiler-lazy-bindings.md).

## Open compiler work (accepted, in flight)

- C1/C2-style elisions landed per the active plan in
  `../performance/OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md`.
- `../proposals/OKOJO_C3_TDZ_ELISION_NOTE.md` (block-scope hole-init elision)
  and `../proposals/OKOJO_C4_COMPLETION_ELISION_NOTE.md` (root completion-sink
  elision) are planned iterations with test targets, not current behavior.
