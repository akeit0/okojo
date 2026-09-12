# Split immutable code from runtime state

Status: `proposed` — highest architectural priority.
Replaces: `JsScript` responsibility bundle
(`src/Okojo.JavaScript/Bytecode/JsScript.cs`),
`JsFunctionCompiler` returning realm-bound `JsBytecodeFunction`
(`Compiler/JsFunctionCompiler.Compile.cs`), closure creation by
`MemberwiseClone` (`Objects/JsBytecodeFunction.cs`), realm-coupled constant
pools and script-level declaration validation
(`Compiler/JsScriptCompiler.Compile.cs`, `Execution/JsRealm.VmLoop.cs`).

## Problem

`JsScript` is neither a clean immutable compilation product nor a clean
runtime instance: bytecode, constant pools, frame size, mutable
property/global caches, agent registration, source ownership, debugger
tables, generator targets, and top-level declaration metadata share one
large mutable constructor. Compilation output therefore cannot be shared
across executions or realms, and every execution pays for state it never
mutates.

## Ownership boundaries

| Component | Owns | Does not own |
|---|---|---|
| `CompilationUnit` | shared source, function descriptors, symbolic names, declaration/module plans | mutable runtime caches |
| `FunctionCode` | bytecode, constant descriptors, frame layout, handler/resume tables, feedback layout | realm, agent, JS object identity |
| `FunctionInstance` | realm-linked constants, atom handles, live binding references, feedback, execution-code view | original source storage |
| `JsClosure` | JS function identity, captured environment, lexical `this`, home object, private-environment state | a duplicated compilation product |
| `FunctionDebugInfo` | sequence points, lexical scopes, variable locations, diagnostic-name mappings | mutable execution state |

These are ownership boundaries, not an allocation mandate: descriptors can
live in compilation-unit arrays, and debug/feedback storage stays absent
until needed.

## Decisions

- Stop compiling into a JS function object. `FunctionTemplate` exists but the
  compiler still returns a realm-bound function; replace it with an immutable
  descriptor plus explicit runtime closure construction
  (`descriptor → optional code → realm-linked instance → many closures`).
  No compatibility adapter around the old constructor — it would preserve the
  old ownership inside the new one.
- Make realm linking explicit. Atomized names, prepared literal layouts, and
  realm-associated constant objects cross the boundary today; compiled code
  must carry symbolic descriptors linked at instantiation to atom handles,
  layouts, and binding cells, with declaration checks at the execution
  boundary.
- Start with in-memory reuse across executions and realms. Persistent
  bytecode caching follows; it must not complicate the first cut.

## Acceptance gate

Same passing JS behavior with shared immutable code across two realms in one
process; no mutable caches reachable from the compilation product;
`toString`/identity/hoisting semantics unchanged (see
[conformance-gate.md](conformance-gate.md)).
