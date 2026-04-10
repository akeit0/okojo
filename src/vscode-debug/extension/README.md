# Okojo VS Code Debug Extension

This project is the VS Code-side debugger scaffold for Okojo.

Current state:

- debugger contribution and configuration provider
- inline adapter that launches `src/Okojo.DebugServer`
- paused stack/local/source inspection from debug-server checkpoints
- source breakpoints by `sourcePath:line`
- launch-time `stopOnEntry` support

Try it:

1. open `src/vscode-debug/extension` in VS Code
2. run the `Okojo: Launch` configuration
3. optionally set `stopOnEntry` in the launch config to pause before execution
4. open a `.js` file in the workspace and set breakpoints on source lines

The launch config points at `src/Okojo.DebugServer/Okojo.DebugServer.csproj` by default and uses the current workspace file as `program` when one is open.

For a ready-made example, open `samples/okojo-debugger-workspace` and run `Okojo: Sample Launch`.
That sample includes separate launch configs for generated `dist/*.mjs` debugging and authored `src/*.mts` debugging through source maps.

Build:

```powershell
cd src/vscode-debug/extension
npm install
npm run compile
```

Local run:

1. open `src/vscode-debug/extension` in VS Code
2. press `F5`
3. VS Code opens an Extension Development Host with `samples/okojo-debugger-workspace`
4. run `Okojo: Sample Launch (generated JS)` to debug emitted JavaScript, or open `src/entry.mts`
5. set a breakpoint in `src/lib/math.mts` or `src/entry.mts`
6. run `Okojo: Sample Launch (TS source maps)`
