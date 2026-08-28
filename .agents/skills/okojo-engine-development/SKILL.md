---
name: okojo-engine-development
description: Develop or debug Okojo ECMAScript engine internals. Use for parser, compiler, bytecode, VM, object-model, or built-in implementation work; use the linked specialist skill only when its task matches.
---

# Okojo Engine Development

Use this skill only for Okojo engine implementation or debugging. Read
`../../../AGENTS.md` for repository rules; this skill does not replace them.

## Route only when needed

- Language/compiler/VM investigation: use the V8 reference and read
  `../../../docs/OKOJO_VM_DEEP_INSPECTION_METHOD.md` when the task is
  non-trivial.
- VM-loop optimization: also read
  `../../../docs/OKOJO_VM_LOOP_OPTIMIZATION_FOUNDATION.md` only for that
  optimization work.
- Test262 campaigns: use [`okojo-test262`](../okojo-test262/SKILL.md), not this
  workflow.
- Okojo.Node/Ink debugging: use
  [`okojo-node-ink-debug`](../okojo-node-ink-debug/SKILL.md), not this workflow.

API/package/namespace migration, documentation-only edits, and mechanical
refactoring do not require this skill.

## Minimal default

Inspect the relevant code and tests, make the smallest correct change, and
follow the focused-test/full-suite workflow in `AGENTS.md`. Use V8 for
language/compiler/VM behavior and Node for built-in behavior when a semantic
reference is needed.

Do not load the deep references or specialist skills unless the current task
actually needs them.
