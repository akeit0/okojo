# Okojo VS Code Debug Roadmap

## Implemented

- extension scaffold
- debugger contribution type `
- process-backed launch path to `Okojo.DebugServer`
- sample workspace for local debugger runs
- module entry support for multi-file `.mjs` samples
- loader-side path fallback for extensionless and index-style imports
- stack / scope / paused-source plumbing from the debug host into the adapter
- debug options command
- bytecode viewer command hookup

## In Progress

- adapter control flow and pause/resume behavior
- breakpoint UX and relocation/verification feedback
- bytecode viewer UI polish
- step UX polish

## Next

1. tighten adapter stop/resume behavior
2. improve locals / scopes / value rendering
3. improve bytecode viewer UI to be closer to the Lua reference
4. add evaluate / hover / watch-expression support

## Non-Goals For The First Pass

- production-grade DAP polish
- async VM execution control
- transport-specific runtime state
- full globals snapshot on every stop
