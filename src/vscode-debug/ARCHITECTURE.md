# Okojo VS Code Debug Architecture

## Current Shape

- `src/vscode-debug/extension`
  - VS Code extension scaffold
  - debugger contribution
  - adapter entrypoint

## Intended Split

- VS Code extension
  - UI, commands, debug configuration, DAP glue
- Okojo debug host (`Okojo.DebugServer`)
  - runtime-side execution control and paused-state materialization
- Okojo runtime
  - checkpoints, locals, source mapping, breakpoint patching

## Design Rules

- keep the extension thin
- keep runtime pause data structured
- keep breakpoint patching off the UI transport path
- keep debugger stop formatting separate from transport messages

## Reference

Lua-CSharp uses a similar split between a debug host and a VS Code adapter. Okojo should follow the same boundary, but stay synchronous and avoid async execution control in the VM path.
