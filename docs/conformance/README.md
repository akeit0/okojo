# Conformance

- [TEST262_SKIP_TAXONOMY.md](TEST262_SKIP_TAXONOMY.md) — classified skip
  inventory (`SkipSpecStatus` / disposition / priority). Policy document.
- Factual progress tables live at repo root (`TEST262_PROGRESS*.md`) and are
  generated from runner results — regenerate them from the same
  manifest/results the runner uses; do not hand-edit counts.

## Truth order

`tools/Test262Runner/SkipList.cs` is truth; the taxonomy is policy intent.
Known drift: the taxonomy lists explicit resource management as excluded
while that runner exclusion is commented out. Reconcile by changing one side
deliberately, never by editing both silently.

## Redesign gate

The gate proposal (fresh baseline, pinned suite revision, phase-sensitive
comparison, focused optimization-sensitive regressions) is
[../proposals/conformance-gate.md](../proposals/conformance-gate.md).
