# Archive

Completed work belongs here — implemented feature notes and fully superseded
migration history, kept so past decisions stay traceable.

- `OKOJO_FEATURE_DROP_UNUSED_FEEDBACK_OPERANDS.md` — implemented 2026-09-10
  (commit `4ade560`); the behavior now lives in the emitter, VM handlers,
  `BytecodeInfo`, and the contract/encoding tests.
- `OKOJO_FEATURE_DEBUG_TABLE_DEFECTS.md` — implemented 2026-09-10
  (commit `3eb7447`); line-start index, synthetic-local flag, dormant
  optimizer deletion.
- `OKOJO_FEATURE_CJS_WRAPPER_SOURCE.md` — implemented 2026-09-10; CommonJS
  wrapper attaches source so stack frames carry locations (also repairs the
  Node source-maps CLI test).

`performance/reports/` holds dated evidence that stays addressable (not
archive).

When archiving, move the file with `git mv`, leave no stub, and record the
superseding document and revision in the archive note's header.
