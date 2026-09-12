# C3: Block-Lexical TDZ Hole-Init Elision

## Scope for this iteration

Extend lexical hole-initialization elision (currently: function-top-level
statement prefixes + for-head declarations with literal initializers) to
**block scopes**, so block-scoped lexicals with computed initializers inside
loop bodies skip the per-iteration `LdaTheHole`/`Star` pair when no read can
observe the hole. Primary target: `stopwatch-modern`'s inner loop (three
`const`s per iteration, ~6-7 dispatches/iteration of dead hole-init stores,
5.9M `LdaTheHole` per probe).

Per-binding independent analysis for each statement of a block:

1. Single-declarator `let`/`const` declaration with an initializer.
2. The initializer does not reference the binding itself (`let x = x + 1`
   keeps the hole-init).
3. The initializer contains no function/class node (IIFE bodies execute
   during initialization and can read the binding; conservative).
4. No preceding statement in the block references the binding by name
   (read-before-declaration keeps the hole-init).
5. If the binding is captured by a child closure (`Planned.IsCaptured`), the
   block must contain no function nodes at all: hoisted block-level
   function declarations create closures at block entry, before the
   initialization, so capture + any function node keeps the hole-init.
6. Storage may be `LexicalRegister` or `ContextSlot` (the gate proves the
   slot's prior content is never read, which is storage-independent).

Reuses the existing `skippedLexicalHoleInitializations` set consumed by
`EmitScopeLexicalHoleInitialization`; new prepare runs in
`EmitBlockStatementCore` before `EnterScope`.

## Minimal JS repros

```js
// elided: initializer is user code but never reads z, no closures
for (let y = 0; y < 3; y++) {
    const z = x ^ y;
    if (z % 2 == 0) sw.Start();
    const ms = sw.ElapsedMilliseconds;
    const rn = sw.IsRunning;
}

// kept: read before declaration (must throw on first iteration)
for (let y = 0; y < 3; y++) {
    if (y > 0) read(t);
    let t = y + 1;
}

// kept: initializer references the binding itself
for (let y = 0; y < 3; y++) {
    let t = t + 1; // TDZ throw
}

// kept: closure created before the declaration captures the binding
for (let y = 0; y < 3; y++) {
    if (y > 0) f();
    let t = y + 1;
    var f = () => t; // note: f is var (outer), created per iteration
}

// kept: captured binding + hoisted block function declaration
for (let y = 0; y < 3; y++) {
    g();
    let t = y + 1;
    function g() { return t; } // closure created at block entry
}
```

## Planned tests

- New focused tests in `tests/Okojo.Tests` (TDZ elision cases):
  elision-does-not-change-values (multi-iteration loops reading after init),
  each "kept" repro above must still throw `ReferenceError`.
- Existing TDZ coverage: `tests/Okojo.Tests` let/const TDZ tests must stay
  green; Test262 `language/statements/let`, `language/statements/const`,
  block-scope sweeps.

## Reference observations (V8, compiler/VM policy)

`node --print-bytecode` on the elision repro: V8 emits `LdaTheHole`/`Star`
only for bindings whose hole is observable; computed initializers in loops
get no hole-init when the declaration dominates all uses. (To be captured
with `tools/V8BytecodeTool` during implementation.)

## Copy vs intentional difference

- Copy: elision gate semantics (no observable hole => no hole-init).
- Intentional difference: Okojo's per-binding textual gate is more
  conservative than V8's flow-based elision scope; direct-eval capture is
  not considered (direct eval is treated as global eval per AGENTS).

## Perf plan

- Hot path: removes 2 dispatches per elided binding per iteration in loop
  bodies (`stopwatch-modern`: 3 bindings -> ~6 dispatches/iteration).
- No VM/`Run` changes: bytecode emission only; `Run` IL/asm unaffected
  (C1/C2 pattern).
- Risks: none to hot paths; compile-time cost of the AST walks is
  linear in block statement count and only walks block statement lists
  once each.
