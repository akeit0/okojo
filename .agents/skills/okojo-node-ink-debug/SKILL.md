---
name: okojo-node-ink-debug
description: Debug Okojo.Node large-app failures through sandbox/OkojoInkProbe and sandbox/OkojoNodeDebugSandbox. Use this for Ink bring-up, reduced Okojo.Node repros, CommonJS/ESM/runtime compatibility debugging, debugger/disassembly investigation, and focused regression-driven fixes.
---

# Okojo.Node Ink Debug

Use this skill for work under:

- `sandbox\OkojoInkProbe`
- `sandbox\OkojoNodeDebugSandbox`
- `src\Okojo.Node`
- `src\Okojo`
- `tests\Okojo.Node.Tests`
- `docs\PALE_NODE_INK_DEBUG_WORKFLOW.md`

## Primary goal

Move `sandbox\OkojoInkProbe` forward by fixing real engine/runtime/compiler bugs and missing compatibility surfaces in a disciplined order.

Prefer:

1. correctness
2. observability/tooling
3. focused compatibility increments
4. measured optimization

## Core policy

Do not treat large-app failures as app-specific until reduction proves they are.

Prefer:

- Node as the reference for Node-facing behavior
- V8 / Node bytecode behavior when the failure looks compiler/VM related

Avoid:

- broad speculative fixes
- Ink-specific hacks for general runtime/compiler bugs
- skipping the reduction step when a smaller repro is possible

## Required workflow

1. reproduce in `sandbox\OkojoInkProbe`
2. reduce to the smallest JS shape possible
3. move the reduced shape into `tests\Okojo.Node.Tests` when stable
4. inspect disassembly / debugger state in `sandbox\OkojoNodeDebugSandbox`
5. compare with Node behavior
6. implement the narrow fix
7. rerun focused regressions
8. rerun Ink to reveal the next blocker

Read:

- `docs\PALE_NODE_INK_DEBUG_WORKFLOW.md`

before doing substantial work in this area.

## Required tools

### Real integration checkpoint

```powershell
dotnet run --project sandbox\OkojoInkProbe\OkojoInkProbe.csproj -c Release -- --debugger
```

### Focused debugger sandbox

```powershell
dotnet run --project sandbox\OkojoNodeDebugSandbox\OkojoNodeDebugSandbox.csproj -c Release -- --app-root <appRoot> --entry <entry> --stop caught --log <logPath>
```

Use the focused debugger sandbox for:

- `debugger;` stops
- caught exceptions
- disassembly around the current PC
- locals/register inspection
- reduced app repros

## Testing loop

Focused loop:

```powershell
dotnet test tests\Okojo.Node.Tests\Okojo.Node.Tests.csproj -c Release --filter <Name>
```

After a fix, rerun nearby focused regressions before going back to Ink.

If the change becomes broad or touches shared compiler/runtime paths, expand validation appropriately.

## Current known lessons

- the old Ink `require(undefined)` failure was actually a wide-register compiler bug
- dedicated debugger/disassembly tooling was necessary to prove the root cause
- `AbortController` support may be needed tactically for bring-up, but `Abort*` / `Event*` likely belong long-term in `Okojo.WebPlatform` / shared web globals

## Deliverables expected from this skill

When using this skill, aim to leave behind:

- a reduced repro
- a focused regression test
- a narrow code fix
- a rerun of Ink showing either success or the next blocker

If the blocker changes, update the workflow note or the active session plan so other agents can resume cleanly.
