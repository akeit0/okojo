# Okojo docs

This directory is organized by purpose. Each page states what it is
(architecture, proposal, decision, report, guide) and what revision it was
verified against. Proposal status uses one of
`proposed | accepted | implemented | rejected | superseded`.

Base revision for this reorganization: `c651320` (2026-08-30).
The reorganization itself is content-preserving: existing notes were moved,
not rewritten. New synthesis pages distill the structural review
(`artifacts/review/REVIEW_2026_09_10.md`) without copying it verbatim.

## Map

- `architecture/` — verified current design. Start at
  [architecture/overview.md](architecture/overview.md).
  - `frontend/` — parser and compiler design notes (moved, preserved).
  - `debugging/` — debugger, checkpoint, and diagnostic notes (moved, preserved).
  - `OKOJO_LIBRARY_SPLIT_PLAN.md` — accepted package-boundary plan (active anchor).
  - `OKOJO_MODULE_EMBEDDING_API.md` — module embedding guidance.
  - [OKOJO_HOST_GLOBAL_THIS.md](architecture/OKOJO_HOST_GLOBAL_THIS.md) — host global-this and navigation-stable same-agent window references.
  - [OKOJO_REALM_OWNERSHIP.md](architecture/OKOJO_REALM_OWNERSHIP.md) — releasing browser realm registry ownership while preserving live JavaScript references.
  - `OKOJO_EXPLICIT_RESOURCE_MANAGEMENT.md` — staging `using`/`await using` note.
- `proposals/` — redesign proposals distilled from the 2026-09-10 structural
  review. Index and statuses: [proposals/README.md](proposals/README.md).
- `decisions/` — decided matters with evidence. Index:
  [decisions/README.md](decisions/README.md).
- `performance/` — methodology, active plan, attempt history, and dated reports.
  Start at [performance/methodology.md](performance/methodology.md).
  - `reports/` — dated, reproducible snapshots (allocation profile, A8/A9
    corpus research, regexp split). Historical unless stated otherwise.
  - `experiments/` — index into the attempt log, active plan, and
    per-attempt snapshots.
- `conformance/` — Test262 skip taxonomy and progress method.
- `guides/` — contributor workflows (packaging, feature-note template,
  implementation workflow).
- `integrations/` — host profiles: `node/` (Okojo.Node), `dotnet/` (.NET hosting).
- `assets/` — images used by docs.
- `archive/` — completed migration history (policy note; nothing archived
  yet — `performance/reports/` stays addressable, it is not archive).

## Conventions

- Architecture pages describe behavior verified against the base revision.
- Proposals describe intended changes, with acceptance gates and pointers to
  the current sources they would replace. They are not implementation claims.
- Benchmark reports record build/runtime configuration, hardware, corpus,
  commands, and raw results (or link to the snapshot that does).
- Prefer forward-slash relative links. Bare `OKOJO_*.md` links mean
  same-directory unless a relative path says otherwise.
- Link check: `python3 eng/check-doc-links.py` from the repo root (exit
  non-zero on broken links). Run it after moving or retitling any doc.

## Known documentation gaps (accepted, not yet fixed)

- `architecture/frontend/OKOJO_DIRECT_PARSER.md` lists current semantic gaps
  and pending default adoption, then marks production replacement complete.
  Read the F0–F5 gates as truth and "complete" as migration-complete until
  the note is reconciled (review-observed, §7).

- `AGENTS.md` feature-kickoff references now resolve to
  `guides/OKOJO_FEATURE_NOTE_TEMPLATE.md` and
  `guides/FEATURE_IMPLEMENTATION_WORKFLOW.md` (created in this reorganization
  from the requirements already stated in `AGENTS.md`).
- `integrations/node/OKOJO_NODE_RUNTIME_PLAN.md` lines 562–565 referenced
  `docs\...` paths valid only from the old flat layout, plus a
  `OKOJO_NODE_COMMONJS_RESOLUTION_NOTE.md` that was never created. Fixed to
  same-directory links; the CommonJS-resolution note is still missing and
  tracked in [proposals/README.md](proposals/README.md).
- `OKOJO_ECMA262_COMPLIANCE_PLAYBOOK.md` feature-doc convention now points at
  `docs/proposals/OKOJO_FEATURE_*.md` (where C3/C4-style iteration notes
  live); the `OKOJO_FEATURE_PROXY_PHASE1.md` example remains a placeholder
  until that note lands.
- `conformance/TEST262_SKIP_TAXONOMY.md` can drift from
  `tools/Test262Runner/SkipList.cs` (e.g. explicit resource management is
  taxonomy-excluded but the runner exclusion is commented out). The taxonomy
  is policy; the runner is truth. See [conformance/README.md](conformance/README.md).
- `performance/reports/OKOJO_A8_A9_RESEARCH.md` mixes static emission
  frequency with execution language in places ("dispatches" for what its own
  section 1 calls compilation-only counts). Read it as a bytecode-size
  signal, not an execution profile.
- `proposals/OKOJO_EH_SPLIT_DESIGN.md` still motivates part of its case with
  a per-dispatch null-init cost that current `JsRealm.VmLoop.cs` hoists to
  once per `Run`. See [proposals/exception-handling.md](proposals/exception-handling.md).
