---
name: okojo-engine-development
description: Develop and debug Okojo ECMAScript semantics and engine internals, including the parser, compiler, bytecode, VM, object model, and built-ins. Use when work requires engine-specific semantic references or execution investigation. Do not use for assembly/package/namespace/API-boundary migration, documentation-only or mechanical refactoring, Test262 campaigns, or Okojo.Node Ink integration.
---

# Okojo Engine Semantics Development

Use this skill for behavior changes and debugging inside the ECMAScript implementation. Repository organization, library splitting, embedding API migration, and general workflow are governed by `AGENTS.md` and the relevant planning documents without this skill.

## Priorities

1. correctness
2. observability/tooling
3. measured optimization

Use V8 as the primary reference for language/compiler/VM behavior and Node for built-in behavior.

## Non-trivial debugging

Trace the failing path before editing. When the issue involves generated code or VM execution:

1. inspect emitted Okojo bytecode with `tools/OkojoBytecodeTool`
2. inspect the VM/runtime state at the mismatch
3. compare V8 or Node behavior when an external semantic reference exists
4. record whether Okojo copies the reference or intentionally differs

## Implementation constraints

- Keep frame layout and opcode operands stable unless the task explicitly changes that contract.
- Keep numeric index keys out of shape transitions.
- Keep hot paths simple and move uncommon semantics into explicit slow paths.
- Fix a shared root cause once instead of patching individual callers.

## Verification

Follow the test order and commands in `AGENTS.md`. This skill does not define a separate build or test sequence.
