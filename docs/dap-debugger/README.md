# DAP debugger implementation

## Scope

A reusable, editor-independent stdio DAP adapter and VS Code extension for the
existing `Okojo.DebugServer` host. Preserve the existing command-line debugger
and bytecode viewer. Keep VM checkpoints synchronous; queue inspection and
breakpoint changes onto the execution thread rather than reading mutable VM
state from the stdin thread.

Supported first slice: launch, configuration barrier, source breakpoints,
continue/pause/step in/over/out, instruction stepping, stack frames, per-frame
locals, lazy object/array expansion, read-only property-path evaluation, source
retrieval, all-exception checkpoints, output, termination and disconnect.
Unsupported capabilities are not advertised: attach, arbitrary expression
execution, mutation, conditional/hit-count breakpoints, logpoints, data/function
breakpoints, reverse execution and worker debugging.

## Minimal repros

```js
function inner(value) {
  const object = { value, nested: { answer: 42 } };
  debugger;
  return object.nested.answer;
}
const result = inner(7);
console.log(result);
```

Also test an infinite loop (pause/terminate), a file path containing spaces,
recursive frames with different local values, a throwing getter and a cyclic
object (inspection must not execute code or recursively stringify objects).

## References and intentional differences

- DAP overview and specification: https://microsoft.github.io/debug-adapter-protocol/overview
- VS Code debugger extension guide: https://code.visualstudio.com/api/extension-guides/debugger-extension
- ChibiRuby runtime/protocol separation and explicit launch handshake:
  https://github.com/hadashiA/ChibiRuby/tree/main/src/ChibiRuby.Debugger
  https://github.com/hadashiA/ChibiRuby/tree/main/src/ChibiRuby.Debugger.Dap
  https://github.com/hadashiA/ChibiRuby/tree/main/editor-extensions
- Lua-CSharp host/adapter/UI split:
  https://github.com/akeit0/Lua-CSharp/tree/debugger/src/Lua.DebugServer
  https://github.com/akeit0/Lua-CSharp/tree/debugger/src/vscode-debug

Follow the layering and DAP lifecycle, not another VM's frame layout. No reference
implementation code is copied. Okojo retains its synchronous VM. Node/V8 is a
behavioral reference for frame-local lookup (a caller is not a lexical parent),
non-invoking inspection, and source-level stepping. No ECMAScript language
semantics are intentionally changed.

## Tests and performance

- `src/vscode-debug/extension/test`: real DAP framing, adapter lifecycle and host
  protocol contract tests; optional real-engine DAP end-to-end tests.
- `tests/Okojo.DebugServer.Tests`: VM-thread command dispatch, structured
  inspection, source paths, stale handles and execution controls.
- Existing engine and debugger suites remain regression gates.

Continue uses the configured checkpoint interval. Single-stepping temporarily
uses interval 1; restore the normal interval on continue. Object inspection is
lazy, paged and shallow. No new per-instruction checks are added to the VM hot path. The existing
periodic slow path reloads the check interval after returning from a callback. Handles are invalidated at resume and never silently
rebound to a later pause.


## Additional runtime corrections

A debugger request no longer reads live VM state on the stdin thread. Resume
invalidates inspection handles, and caller-frame locals are not used as an
invented lexical scope chain. Getter/property inspection uses descriptors and
proxies remain opaque. All source breakpoint replacements are serialized on the
VM thread and validate the entire request before removing old registrations.

Step commands temporarily use interval 1. A periodic callback no longer restores
an obsolete pre-stop interval. Repeated visits to the same program counter are
recognized using the instruction counter, and frame transitions can stop even
when line numbers coincide. Exact source positions are mapped before comparing
step targets. Intentional debugger termination uses the runtime's fatal exception
path so catch-protected loops cannot swallow shutdown as an ordinary JS error.

## Validation evidence

Node/V8 executed `samples/okojo-debugger-workspace/dap-inspection.js` and printed
`result: 107` and `getterCalls: 0`; the inner function's V8 bytecode was also dumped.
A separate Node probe confirmed that a caller-local identifier is not lexically
visible in an independently declared callee, and descriptor lookup does not invoke
a getter. These are reference observations, not tests of Okojo.

## Validation

Windows follow-up (`.NET SDK 11.0.100-preview.5` targeting `net10.0`,
Node.js v25.5.0, locked `npm ci` dependencies), all passing:

- `npm run compile` (`tsc -p .`, full extension typecheck)
- `npm test`: 25 adapter/framing tests, 0 failures
- `npm run test:engine` against the Release host: 4 tests, 0 failures
- `dotnet test tests/Okojo.DebugServer.Tests -c Debug`: 35 tests, 0 failures
- `dotnet test tests/Okojo.Tests -c Debug`: 2244 passed, 0 failed
- `dotnet csharpier format` on all changed/new C# files; Debug and Release
  builds warning-free; `git diff --check` clean; VSIX packaging works

Fixes applied on top of the delivered implementation (all covered by the
suites above): `evaluate` resolves top-level `let`/`const`/`function` via the
realm's global lexical bindings; the debug host forces UTF-8 stdio encoding so
non-ASCII breakpoint paths survive on non-UTF-8 code pages; a `pause` landing
while a `resume` is still queued stays sticky instead of being lost.

Still manual: VSIX install with interactive F5, source-map/generator stepping
acceptance, Linux CI observation, and bytecode/performance comparison. No
performance claim is made.
