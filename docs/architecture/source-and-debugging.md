# Source and debugging (current)

Status: architecture synthesis (base `c651320`). Source detail:
`debugging/OKOJO_DEBUGGER_ROADMAP_NOTE.md` (active roadmap),
`debugging/OKOJO_DEBUG_SERVER_NOTE.md` (historical precursor),
`debugging/OKOJO_EXECUTION_CHECKPOINT_NOTE.md`,
`debugging/OKOJO_CALL_SITE_DIAGNOSTICS_NOTE.md`.

## Source ownership

Shared `SourceCode` ownership already exists. Function `toString` uses
retained source text — that is a specification behavior, not optional debug
data, and must survive debug-table stripping.

## Current debugger shape

- `debugger;` stops; hook flags for breakpoint/call/return/pump/generator;
  `src/Okojo.DebugServer` + Core with a VS Code scaffold, `stopOnEntry`,
  module breakpoints, line/instruction stepping.
- Missing: adapter hardening, variables/evaluate/hover, exception
  breakpoints, formatting layer, public API cleanup.
- Checkpoints cost one flag plus a countdown on the hot path (slow path
  no-inline); call/return/pump/generator/debugger independently togglable;
  timeout rides the slow path via the host clock. `debugger` without a
  debugger is a no-op (V8 parity).
- Call-site messages (`x is not a function`) render at compile time into
  `JsScript.DebugNames` (V8 `RenderCallSite` copied, no reparse since the
  arena AST is released); the VM binary-searches only on the exceptional path.
- Local metadata today is one flat record per variable
  (name, register/slot, single PC interval, flags).

## Open defects (concrete, accepted)

1. `SourceCode.GetOrCreateLineStarts` recognizes only `\n`; it must handle
   CR, CRLF-as-one, U+2028, U+2029 (spec lexical grammar).
2. Local-debug emission drops every name starting with `$`, hiding legitimate
   user variables like `$value`; replace spelling filtering with an explicit
   synthetic flag (`JsCompilerBase.cs`).

## Tooling ownership

Runtime checkpoints and breakpoint patching (which today mutates
`Script.Bytecode`) are debugger architecture; host/tool usage belongs in
guides. Source-map `TryMapToOriginal` scans linearly — matters for large
single-line generated files.

Structural direction (scopes with location ranges, sequence points attached
pre-layout, copy-on-write execution views for breakpoints, lazy-compilation
identity): [../proposals/debug-info-redesign.md](../proposals/debug-info-redesign.md).
