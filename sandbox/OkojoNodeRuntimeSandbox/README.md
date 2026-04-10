# Okojo Node Runtime Sandbox

This sandbox is a small file-backed Node-style runtime probe for `Okojo.Node`.

It intentionally uses real fixture files instead of in-memory loaders:

- CommonJS app with `node_modules` lookup
- ECMAScript module app with package `"exports"` import resolution
- Node built-ins currently implemented in `Okojo.Node`

Current scenarios:

- `fixtures/cjs-app/main.js`
  - `require("pkg")`
  - `require("node:path")`
  - `Buffer.from(...)`
- `fixtures/esm-app/main.mjs`
  - `import value from "pkg"`
  - `import path from "node:path"`
  - `import process from "node:process"`
- `fixtures/tick-events-app/main.js`
  - `process.nextTick(...)`
  - `require("node:events")`
  - host pump after entry execution
- `fixtures/stream-app/main.js`
  - `require("node:stream")`
  - `new stream.PassThrough()`
  - `stream.pipeline(...)`
  - basic `data` flow and writable state
- `fixtures/tty-app/main.js`
  - `process.stdout.write(...)`
  - `process.stderr.write(...)`
  - `require("node:tty")`
  - `require("node:util")`
  - `process.stdout.cursorTo(...)`
  - `process.stdout.moveCursor(...)`
  - `process.stdout.clearLine(...)`
  - `process.stdout.clearScreenDown(...)`
  - configured `isTTY` / `columns` / `rows`
  - captured terminal output shown with escaped control sequences

Run:

```powershell
dotnet run --project sandbox\OkojoNodeRuntimeSandbox\OkojoNodeRuntimeSandbox.csproj
```

What this sandbox is for:

- pressure the real file/module resolver path
- expose Node runtime API gaps outside unit tests
- provide a concrete checkpoint before trying larger package targets
