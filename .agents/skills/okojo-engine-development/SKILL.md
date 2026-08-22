---
name: okojo-engine-development
description: Develop and debug the Okojo JavaScript engine, runtime, compiler, and embedding APIs. Use for ordinary Okojo implementation, refactoring, architecture-boundary work, bytecode/VM investigation, and focused regressions. Do not use for Test262 runner or skip-list work, or Okojo.Node Ink integration, which have dedicated skills.
---

# Okojo Engine Development

Use this skill for engine work under `src/Okojo`, its host-facing projects, related tooling, tests, and architecture documents.

## Priorities

1. correctness
2. observability/tooling
3. measured optimization

Use V8 as the primary reference for language/compiler/VM behavior and Node for built-in/runtime API behavior.

## Non-trivial debugging

Trace the failing path before editing. When the issue involves generated code or VM execution:

1. inspect emitted Okojo bytecode with `tools/OkojoBytecodeTool`
2. inspect the VM/runtime state at the mismatch
3. compare V8 or Node behavior when an external semantic reference exists
4. record whether Okojo copies the reference or intentionally differs

Do not force bytecode or external-reference work onto documentation-only, mechanical, or unrelated host-infrastructure changes.

## Implementation constraints

- Keep frame layout and opcode operands stable unless the task explicitly changes that contract.
- Keep numeric index keys out of shape transitions.
- Keep hot paths simple and move uncommon semantics into explicit slow paths.
- Prefer the existing builder-based embedding surface over adding policy to raw runtime constructors.
- Fix a shared root cause once instead of patching individual callers.

## Verification

Use the smallest focused regression that proves the change, then expand validation according to impact:

```powershell
dotnet build tests/Okojo.Tests/Okojo.Tests.csproj -c Release /p:UseSharedCompilation=false
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj -c Release --no-build --filter <Name>
```

Treat build warnings and test failures as blockers unless the user explicitly approves an exception. Preserve unrelated user changes and commit only files in scope.
