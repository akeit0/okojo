# Okojo Debugger

A VS Code debugger for the Okojo JavaScript engine, plus a reusable, editor-independent
Debug Adapter Protocol (DAP) adapter over stdio. The same `OkojoDebugSession` runs
inside VS Code and behind `node dist/main.js`.

## Build and install

Use Node.js 22 for development/testing, npm, and the .NET 10 SDK for building the
Okojo debug host. A published DLL needs the .NET 10 runtime; a self-contained host
can instead be selected with `debugServerPath`.

From the repository root:

```sh
dotnet build src/Okojo.DebugServer/Okojo.DebugServer.csproj -c Release
cd src/vscode-debug/extension
npm ci
npm run compile
npm test
npm run package
```

`npm run package` invokes the VS Code packaging CLI via `npx` and therefore needs
network access when that CLI is not cached. Install the resulting
`okojo-vscode-debug-0.1.0.vsix` through **Extensions: Install from VSIX**.
The extension does not bundle .NET or an engine binary, and has no runtime npm
package dependencies. Do not select `Okojo.DebugServer` itself as a DAP executable:
its newline-JSON protocol is private to the adapter.

For extension development, open this directory in VS Code and press F5. The
Extension Development Host opens `samples/okojo-debugger-workspace`. Choose
**Okojo: DAP inspection** for the plain JavaScript example; no transpiler is needed.
The original generated-module and TypeScript/source-map examples are retained.

## Launch configuration

With the repository root as the workspace:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "okojo",
      "request": "launch",
      "name": "Okojo: inspect",
      "program": "${workspaceFolder}/samples/okojo-debugger-workspace/dap-inspection.js",
      "cwd": "${workspaceFolder}",
      "debugServerProject": "${workspaceFolder}/src/Okojo.DebugServer/Okojo.DebugServer.csproj",
      "stopOnEntry": true,
      "checkInterval": 1024
    }
  ]
}
```

`debugServerProject` uses `dotnet run` in Release configuration. Set `noBuild: true`
after building Release to avoid rebuilding on each launch. The adapter searches
workspace ancestors and the adjacent source checkout when a project is omitted.
An installed VSIX used outside the engine checkout needs an explicit project or
published host path, either in launch.json or `okojo.debugger.debugServerPath`.

For a published host, replace `debugServerProject` with:

```json
"debugServerPath": "/absolute/path/to/Okojo.DebugServer.dll"
```

This also accepts a native executable. Other launch options include `dotnetPath`,
`env` (string values; null removes a variable), `debugServerArgs`, `startupTimeout`,
`requestTimeout`, `stepGranularity: "line" | "instruction"`, and
`enableSourceMaps`. An omitted `moduleEntry` preserves `.mjs` autodetection;
set it explicitly to force module or script entry. TypeScript must be compiled
first: `program` names the emitted JavaScript, not a `.ts`/`.mts` file.

Debugging launches local executable code and is disabled in untrusted/virtual
workspaces. No remote port is opened. Pause and graceful termination are
cooperative at VM checkpoints; the adapter kills its owned process tree if the
host cannot stop promptly, including when blocked inside native host code.

## Available debugging features

- Launch/configuration barrier, optional pre-execution entry stop, no-debug runs,
  console output, graceful disconnect and process-tree cleanup.
- Source line breakpoints, complete replacement per file, stable IDs and late
  verification/relocation, including the existing source-map integration.
- Continue, pause, step in/over/out; line and instruction granularity; one VM thread.
- Stack frames and selected-frame Locals/Global scopes, variable paging, lazy
  object/array expansion and identity-preserving cyclic references.
- Read-only hover, watch and Debug Console property-path inspection. For example:
  `object`, `object.nested.answer`, `object.items[1]`, `object["spaced key"]`.
- All-exception stopping (handled and unhandled VM-captured exceptions), basic
  exception stop information, loaded sources and source content retrieval.
- **Okojo: Bytecode Viewer** and **Okojo: Debugger Options**. A per-request DAP
  stepping granularity overrides the session default.

The entry stop is before compilation/execution, so it has an empty Locals scope.
Set a breakpoint or continue to `debugger;` to inspect live bindings. Variable and
frame handles expire on resume and never silently refer to a later stop.
Object previews are shallow; accessors appear as `<accessor>`, and proxies are
opaque. Reading a getter or proxy through an evaluate request is rejected instead
of executing it. This is a debugger inspector, not a JavaScript REPL.

## Deliberate limits

There is no attach/TCP server, full JavaScript expression execution, assignments,
setVariable, conditional/hit-count/column breakpoints, logpoints, function/data
breakpoints, reverse execution, restart, worker/multi-agent debugging, or Node.js
runtime emulation. Unsupported requests fail and unsupported capabilities are not
advertised. Statement granularity uses the same source-line stepping policy as line
mode. JavaScript symbols as property keys and a prototype tree are not enumerated.

Locals contain bindings described by the selected runtime frame. The current
runtime snapshot does not provide a complete named outer lexical-scope chain;
the adapter does **not** mislabel caller frames as captured scopes. Optimized-away
or unreported outer bindings cannot be reconstructed by watch expressions.
`this` is available only when represented in the runtime's local metadata.
The exception filter is `all`, not `uncaught`; `exceptionInfo` provides a stop
summary, not a full thrown-value/object inspection model. Primitive strings use
bounded previews. Full source-map and generator/async stepping behavior still
requires real-engine and editor acceptance testing; see the validation report.

## Use with another DAP client

Build `npm run compile:adapter`, then configure the client to spawn:

```sh
node /absolute/path/to/src/vscode-debug/extension/dist/main.js
```

stdin/stdout use UTF-8 `Content-Length` DAP framing. Diagnostics never go to raw
stdout. Send `initialize`, then `launch`. After `initialized`, configure
breakpoints/exceptions and send `configurationDone`. The `launch` response is
intentionally held until configuration completes. No VS Code-specific UI request
is needed before continue. The protocol handles native/file-URI source paths and
zero-/one-based client positions. `okojo/bytecode` is an optional custom request
which emits an `okojo/bytecode` event when bytecode is available at the stop.

## Tests

`npm test` builds the strict, editor-independent adapter and runs actual stdio DAP
framing/lifecycle tests against an explicitly simulated engine host. These are
contract tests, not proof of VM correctness.

To run the separate suite against the actual engine (from the repository root):

```sh
dotnet test tests/Okojo.DebugServer.Tests/Okojo.DebugServer.Tests.csproj -c Debug
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj -c Debug
dotnet build src/Okojo.DebugServer/Okojo.DebugServer.csproj -c Release
export OKOJO_DEBUG_SERVER="$PWD/src/Okojo.DebugServer/bin/Release/net10.0/Okojo.DebugServer.dll"
cd src/vscode-debug/extension
npm run test:engine
```

In PowerShell, use `$env:OKOJO_DEBUG_SERVER = (Resolve-Path '...dll').Path` instead
of `export`. The real-engine suite fails clearly when its built host is absent;
it never silently falls back to the fake host. The checked-in
`.github/workflows/dap-debugger.yml` supplies Linux/Windows validation jobs.

See `docs/dap-debugger/` for the implementation record and exactly which
checks were performed and which remain unverified.
