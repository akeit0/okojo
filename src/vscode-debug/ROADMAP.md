# Okojo debugger roadmap

## Implemented in source

- Shared editor-independent DAP session, bounded UTF-8 framing, standalone stdio entry.
- Explicit launch/configuration barrier and pause/resume lifecycle; correlated host requests.
- Line breakpoint replacement, stable IDs, late verification and existing source-map routing.
- Continue/pause/step in/over/out, line/instruction stepping, stack/selected-frame scopes.
- Lazy object/array expansion, paging, cyclic identities and safe property-path inspection.
- All-exception filter, output/source events, cancellation/termination and child cleanup.
- VM-thread command dispatch, step countdown and fatal termination fixes.
- VS Code configuration/trust handling, independent simultaneous sessions and bytecode viewer.
- Stdio contract tests, real-engine tests, NUnit runtime regressions and Linux/Windows CI workflow.

Implementation status is not a claim that every runtime/editor path was tested.
See `docs/dap-debugger/` for the implementation and validation record.

## Required acceptance gates

- Run .NET builds and focused/full runtime suites, including formatting checks.
- Run real-engine DAP end-to-end tests on Linux and Windows.
- Build/package the extension with its locked typings; test installation and F5 in VS Code.
- Exercise source maps, nested/repeated calls, generator/async frames and multiple sessions.
- Profile paused inspection on large/sparse arrays and very deep stacks.

## Deferred (not advertised)

- Complete named outer lexical-scope metadata and reliable receiver (`this`) exposure.
- Arbitrary JS evaluate/REPL and variable mutation with explicit execution policies.
- Conditional/hit-count/column breakpoints, logpoints, function/data breakpoints.
- Attach/TCP and embedded-server authentication/lifecycle policy.
- Uncaught-only exception filtering and thrown-value inspection.
- Symbol/prototype properties, setExpression, completions and disassemble requests.
- Restart, reverse execution, worker/multi-agent and asynchronous stepping targets.

Keep VM execution synchronous. Do not infer lexical visibility from call-stack
order, execute getters during implicit inspection, or require VS Code-specific
handshakes for standard DAP controls.
