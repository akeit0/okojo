# Debug information redesign

Status: `proposed`; the two defect fixes are concrete and unblocked.
Replaces: flat `JsLocalDebugInfo` records
(`Execution/JsLocalDebugInfo.cs`), `$`-prefix filtering
(`Compiler/JsCompilerBase.cs`), `\n`-only line starts
(`Parsing/SourceCode.cs`), linear `TryMapToOriginal`
(`SourceMaps/SourceMapDocument.cs`), breakpoints mutating `Script.Bytecode`
(`Execution/JsBreakpointHandle.cs`).

## A. Source text is not debug info

A debug-disabled build must be able to drop debugger tables without changing
`Function.prototype.toString` (spec source-returning case uses retained
source text, independent of debugger support). Keep source ownership plus
function source ranges separate from optional scope/sequence-point metadata.
Shared `SourceCode` ownership already exists — the new work is the
separation, not another wrapper optimization. The allocation report's debug
payload (~69 of ~129 KB durable payload) motivates *optional* tables; it is
not a claim about total compilation allocation.

## B. Scopes and location ranges

Replace flat records with:

```text
LexicalScope: parent, source range, instruction range
Variable: name, declaration kind, owning scope, synthetic flag
LocationRange: variable, instruction range, location
Location: register | context-depth/slot | constant | unavailable
```

This supports shadowing, register reuse, optimized-out values, and storage
that changes mid-scope. Attach sequence points and scopes to logical
instructions and resolve PCs at final layout — metadata must describe
optimization, never block it because offsets were assigned too early.

## C. Two defect fixes (do first)

1. `GetOrCreateLineStarts`: handle CR, CRLF-as-one, U+2028, U+2029 per the
   spec lexical grammar (lexer terminators are separate; this is the
   source-location defect).
2. `$`-prefix filtering hides real user variables (`$value`). Replace with an
   explicit compiler-generated/synthetic flag.

## D. Source maps and breakpoint ownership

`TryMapToOriginal`: upper-bound binary search over sorted columns (matters
for large single-line generated files); build reverse indexes lazily for
breakpoint mapping. Breakpoints keep cheap patching through a copy-on-write
execution-code view owned by the runtime instance — never a dictionary
lookup on the normal path. Breakpoint identity is source/function first,
executable PC after compilation, so identities survive lazy compilation.

## Acceptance gate

Debug-disabled builds byte-identical in behavior with `toString` intact;
shadowing/reuse/optimized-out cases covered in debugger tests; breakpoint
round-trip across lazy compile; no hot-path cost.
