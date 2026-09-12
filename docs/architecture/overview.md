# Architecture overview

Status: architecture (verified against `c651320`, 2026-08-30, via existing
docs plus spot checks — not a full re-verification).

Okojo is a register-based JavaScript engine in C# targeting browser-compatible
behavior: a flat parsing/compilation frontend, a register bytecode with an
accumulator, and a `JsRealm.Run` dispatch loop over transition-shape objects
with dictionary fallback.

## Pipeline

```text
source text (SourceCode)
  → JsLexer → JavaScriptParser → flat JsAst (single owner, dense node IDs)
  → multi-pass compiler (discover → resolve/capture → allocate → emit)
  → BytecodeBuilder → JsScript (+ JsBytecodeFunction closures)
  → JsRealm.Run dispatch loop → JsObject model (shape / dictionary)
```

Details per layer:

- [frontend-and-bindings.md](frontend-and-bindings.md) — parser ownership,
  binding collection, capture planning, storage allocation, emission.
- [bytecode.md](bytecode.md) — instruction set, operand encoding, layout
  limits, disassembly tooling.
- [execution-and-objects.md](execution-and-objects.md) — frame model,
  dispatch loop, property caches, arrays, call arguments, exceptions.
- [source-and-debugging.md](source-and-debugging.md) — source ownership,
  debug metadata, checkpoints, breakpoints, source maps.

## Cross-cutting contracts

- Frame layout and opcode operand conventions are ABI contracts (see `AGENTS.md`
  core rules). The redesign proposals in `../proposals/` explicitly ask to
  renegotiate them — that is intentional and flagged per proposal.
- Numeric index keys stay out of shape transitions.
- Atom/string conversion stays out of hot paths unless semantically required.
- Slow semantics live in explicit slow paths.
- Public composition prefers `JsRuntimeBuilder`; raw `JsRuntime` constructor
  growth is discouraged.

## Source detail (moved, preserved)

- `frontend/OKOJO_DIRECT_PARSER.md` — flat parser ownership and F0–F5 gates.
- `frontend/OKOJO_COMPILER_THROUGHPUT_DESIGN.md` — throughput anchor and P0–P5 plan.
- `frontend/OKOJO_MULTI_PASS_COMPILER_DESIGN.md` — six-pass direction
  (its "current implemented slice" predates the flat compiler; read the
  principle, not the slice).
- `OKOJO_LIBRARY_SPLIT_PLAN.md` — package/namespace boundaries (active anchor).
- `OKOJO_MODULE_EMBEDDING_API.md` — `LoadModule` sync-first embedding guidance.
- `OKOJO_EXPLICIT_RESOURCE_MANAGEMENT.md` — staging `using` support state.
- `debugging/` — debugger roadmap, checkpoint, call-site diagnostics notes.

## Where the flaws the redesign attacks live

- `JsScript` bundles immutable compilation product with mutable runtime state
  → [../proposals/code-instance-split.md](../proposals/code-instance-split.md).
- Nested functions compile eagerly (462 units on the linq-js corpus) and
  bindings are re-resolved per view → [../proposals/compiler-lazy-bindings.md](../proposals/compiler-lazy-bindings.md).
- Bytes are emitted before optimization; unused operands and byte-sized limits
  are baked in → [../proposals/bytecode-isa-layout.md](../proposals/bytecode-isa-layout.md).
- Generator snapshots save the register high-water mark, not live state →
  [../proposals/liveness-suspension.md](../proposals/liveness-suspension.md).
- Monomorphic property cache, hole scanning, argument copying →
  [../proposals/runtime-caches-arrays-calls.md](../proposals/runtime-caches-arrays-calls.md).
- Debug tables ride with compilation output; two concrete defects are open →
  [../proposals/debug-info-redesign.md](../proposals/debug-info-redesign.md).
