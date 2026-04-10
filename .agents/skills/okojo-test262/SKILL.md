---
name: okojo-test262
description: Work on Okojo inside okojo. Use this for Okojo runtime/compiler/Test262 work, including deciding what to implement versus intentionally skip, running the local Okojo test loop, and continuing Test262-driven compliance fixes.
---

# Okojo Test262
Use this skill for work under:

- `src/Okojo`
- `tests/Okojo.Tests`
- `docs/`
- `tools/OkojoBytecodeTool`
- `tools/V8BytecodeTool`
- `tools/Test262Runner`

## Primary priorities

1. correctness
2. observability/tooling
3. measured optimization

Prefer:

- V8 as the reference for language/compiler/VM behavior
- Node as the reference for built-in/runtime API behavior

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

## Required workflow for non-trivial debugging

1. inspect emitted Okojo bytecode
2. inspect VM/runtime behavior
3. inspect V8 / Node behavior
4. record short findings: copy vs intentional difference

Tools:

- `tools/OkojoBytecodeTool`
- `tools/V8BytecodeTool`
- `node -e ...`
- `sandbox/OkojoRepl`
## Fast local loop

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

Skip list location:

- `tools/Test262Runner/SkipList.cs`

## Implementation guidance

- Keep hot paths simple.
- Push uncommon semantics into explicit slow paths.
- Keep frame layout and opcode operand conventions stable.
- Keep numeric index keys out of shape transitions.

## Editing guidance

- Prefer minimal, local fixes.
- Add a focused regression test for each fix.
- Do not revert unrelated user changes.
- Do not widen policy support accidentally just to satisfy a single Test262 file.

## Commit discipline

```powershell
git status --short
git add src/Okojo/... tests/Okojo.Tests/... tools/... docs/...
git commit -m "<type>(<scope>): <subject>"
```

Do not commit unrelated IDE files, experiments, or non-Okojo changes.

## Focused references

Use the matching doc note when a feature has an active work note in `docs/`.
