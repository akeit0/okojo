# Liveness-driven registers and suspension

Status: `proposed` — unlocks with [bytecode layout](bytecode-isa-layout.md).
Replaces: high-water-mark register accounting in generator emission
(`Compiler/JsCompilerBase.Generators.cs`), inferred resume state.

## Problem

Generator emission saves registers `[0, RegisterCount)` — the allocated
high-water mark, not the set live across a suspension. Dead temporaries are
retained, suspension payloads are oversized, and resume logic infers its
state model from neighboring operands.

## Direction

One shared per-function liveness analysis drives three consumers:

- **Register reuse.** Dead temporaries recycle across blocks; contiguous call
  arguments place better.
- **Suspension snapshots.** Each yield/await saves only the registers needed
  after it, via a compact range list or bitmap chosen by density.
- **Reference lifetime.** Dead managed references clear at frame/suspension
  boundaries instead of surviving because a register once held an object.

The resume table states target, required register values, environment state,
and pending completion explicitly. Exception edges, `finally`, delegated
generators, iterator closing, and resource disposal participate in the
analysis — values needed during cleanup are live even when the normal
continuation never reads them.

## Consequences

Debugger variable locations must become richer (a variable no longer owns one
register for its scope) — designed jointly in
[debug-info-redesign.md](debug-info-redesign.md). Register allocation and the
shared argument window ([runtime-caches-arrays-calls.md](runtime-caches-arrays-calls.md))
are the same coordinated change as call-ABI work; that is why the current ABI
is explicitly renegotiable.

## Acceptance gate

Liveness-verified snapshots on generator/async tests including `finally`,
delegation, and disposal paths; smaller suspension payloads on a recorded
corpus; no behavior change on abrupt-completion Test262 variants.
