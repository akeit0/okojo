# TODO

## Okojo Priority Queue

### Architecture

- [ ] finalize stable embedding API boundaries for `src/Okojo.JavaScript` and `src/Okojo.JavaScript.Embedding`
- [ ] split host-facing APIs from core ECMA-262 engine APIs
- [ ] redesign task queue ownership so ECMAScript jobs stay in core engine and host tasks stay host-driven
- [ ] reduce direct scheduling policy living inside `JsAgent`
- [ ] tighten the remaining intended first-class `JsRuntime` / `JsRealm` API surface against the clean API plan
- [ ] clarify `Okojo.Hosting` presets and keep environment globals separate from embedder control APIs
- [ ] define and implement `JsRealm` structural split:
  `JsRealm` as coordinator/root, with internal `RealmIntrinsics` and `RealmShapes`
- [ ] continue module/runtime simplification without reintroducing wrapper-heavy paths

### Performance

- [ ] improve hot-path runtime allocation and branch behavior
- [ ] keep shape/dictionary rollout aligned with hot-path simplicity
- [ ] RegExp: lead-literal scan-ahead for sticky split steps, and interpreter hot-loop tuning vs Jint 4.16.1 (ShortRun 2026-09-11 has Okojo execute ahead on dromaeo-object-regexp, 0.83x, but parse+compile behind at 2.01x; see `benchmarks/README.md` and `docs/performance/reports/OKOJO_REGEXP_SPLIT_PERF_NOTE.md`)
- [ ] RegExp: general "matches empty at every position" pattern recognition beyond `(?:)` / empty source
- [ ] RegExp: reduce `RegExpEngine.Exec` per-call allocations (`CaptureRange[]` + capture substrings) for match-all loops

### Compatibility and integration

- [ ] scope module loading/caches per browser realm and detach document-owned host work on navigation; `JsRuntime.ReleaseRealm` removes registry ownership only (see `docs/architecture/OKOJO_REALM_OWNERSHIP.md`)

- [ ] keep the non-legacy, non-staging Test262 passing baseline stable during API/compiler/runtime work
- [ ] improve `Okojo.Node` compatibility against real Node-facing workloads
- [ ] attempt a real HTML/CSS renderer integration for DOM-manipulation browser compatibility testing
- [ ] add selected staging ECMA-262 support where justified, starting with candidates such as `Temporal`
- [ ] explicit-resource-management: give top-level-module `await using` async cleanup a dedicated lowering path instead of leaning on normal async-function suspension flow (current compiler/runtime seam is awkward)
- [ ] explicit-resource-management: give disposal promise completion a JS-value-preserving path so non-`Error` thrown values survive the host async bridge in the remaining staging `await using` rejection case

### Code/instance split follow-through

Core steps 1–6 are implemented and their conformance gates are green (see
`docs/implementation/CODE_INSTANCE_SPLIT_IMPLEMENTATION.md`).

- [ ] Measure compile/cold-link/cached-link/closure/steady-state and retained memory against the uploaded baseline; inspect first-copy debugger/reentry overhead separately.
- [ ] Implement true lazy nested bodies with owned binding summaries and a thread-safe compilation/error publication policy (proposal step 7).
- [ ] Make module import/export binding plans portable before advertising compilation units as complete reusable modules. Current portable module units contain executable bodies only.
- [ ] Define versioned bytecode serialization and private-name ID relocation before adding persistent caches.

### DAP debugger follow-up

- [ ] Run real-engine DAP tests on Linux and VS Code installation/F5/source-map acceptance checks.
- [ ] Add named outer lexical-scope/receiver metadata rather than exposing caller frames as captured scopes.
- [ ] Extend exception inspection with thrown values and uncaught-only classification.
- [ ] Consider conditional/log/hit breakpoints, controlled expression execution, mutation, and attach as separate capabilities with explicit safety/lifecycle policies.
- [ ] Profile large/sparse-object inspection and generator/async stepping; the delivered adapter preserves the existing VM/source-map integration without claiming those acceptance gates passed.
- [ ] Extend locals-by-name snapshots with outer-scope/context-chain value lookup helpers for paused debugger inspection.
- [ ] Add a compact local-name table to `JsScript` so paused frames can resolve visible locals without guessing from runtime slots.
