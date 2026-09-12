---
name: okojo-test262
description: Run and continue Okojo Test262 compliance work. Use only when the request involves Test262, Test262Runner, compliance sweeps, failing Test262 cases, the passed cache, or SkipList. Do not use for ordinary Okojo implementation, refactoring, review, or debugging.
---

# Okojo Test262

Use this skill only for Test262-driven work in `tools/Test262Runner` and the engine/test files needed to resolve a selected Test262 failure. Ordinary engine work belongs to the general Okojo development workflow.

## Core policy

Fix standards-aligned behavior.

Do not prioritize deprecated/legacy behavior unless explicitly re-approved. Treat these as intentionally unsupported by default:

- direct-eval-specific semantics
- `with`
- legacy `__proto__`
- deprecated legacy accessor APIs
- `Function.prototype.arguments` / `Function.prototype.caller`
- `arguments.callee`

For these, prefer narrow intended Test262 skips with a clear reason over regressing core paths.

Do not add a skip merely to make a run green. A skip requires an intentionally unsupported feature or an explicit user decision.

## Workflow

1. reproduce the exact Test262 case
2. classify it as engine/compiler/runtime behavior or intentionally unsupported coverage
3. compare V8 for language/compiler/VM behavior or Node for built-in/runtime behavior
4. implement the narrow root-cause fix
5. add a focused regression under `tests/Okojo.Tests`
6. rebuild the runner before trusting an exact rerun
7. rerun the exact case, nearby coverage, and the requested continuation batch

## Focused engine loop

```powershell
dotnet build tests/Okojo.Tests/Okojo.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj -c Release --no-build --filter <Name>
```

When the runner is involved:

```powershell
dotnet build tools/Test262Runner/Test262Runner.csproj -c Release /p:UseSharedCompilation=false
```

## Test262 runner

Show help:

```powershell
dotnet run --project tools/Test262Runner/Test262Runner.csproj -c Release -- --help
```

Common continuation pattern:

```powershell
dotnet run --project tools/Test262Runner/Test262Runner.csproj -c Release -- --max-tests 30 --full-path --skip-passed
```

Array.prototype sweep example:

```powershell
dotnet run --project tools/Test262Runner/Test262Runner.csproj -c Release -- --max-tests 30 --category built-ins --filter Array/prototype --full-path --skip-passed
```

Useful runner notes:

- `--skip-passed` uses the local passed cache
- rebuild the runner after runtime/compiler changes before trusting exact reruns
- `SkipList.cs` contains intentional exclusions
- do not run a broad sweep when an exact or category-filtered rerun answers the current question

## Editing guidance

- Prefer minimal, local fixes.
- Add a focused regression test for each fix.
- Do not revert unrelated user changes.
- Do not widen policy support accidentally just to satisfy a single Test262 file.
