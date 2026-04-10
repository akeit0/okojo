# Test262 skip taxonomy

Okojo's Test262 skips are now treated as **classified inventory**, not just ad-hoc exclusions. The runner carries structured metadata so skips can be grouped by **spec status**, **Okojo disposition**, and **priority**.

The goals are:

1. separate **policy decisions** from **missing implementation**
2. separate **active TC39 proposal coverage** from **finished-but-not-baseline-yet** coverage
3. make it easy to track what should never be prioritized, what should be deferred, and what should be fixed next

## Where the classification lives

- `tools\Test262Runner\SkipList.cs`
  - `SkipSpecStatus`
  - `SkipDisposition`
  - `SkipPriority`
  - `ExcludedFeatureEntries`
  - `Entries`
  - `ClassifyPathEntry(...)`
- `tools\Test262Runner\Test262Runner.Infrastructure.cs`
  - `ShouldSkip(...)` emits formatted classified reasons
  - default `excludedFeatures` now come from `SkipList.DefaultExcludedFeatures`

Skipped cases now carry reasons like:

- path skip: `[Standard/UnsupportedByPolicy/None] ...`
- excluded metadata feature: `[feature:Proposal/DeferredImplementation/Low] ...`

## Taxonomy

### Spec status

| Status | Meaning |
| --- | --- |
| `Legacy` | Deprecated or legacy web-compat surface that Okojo does not currently prioritize. |
| `Proposal` | Active TC39 proposal coverage, not yet part of the current baseline target. |
| `FinishedProposalNotInBaseline` | Proposal is effectively done, but not yet part of Okojo's current ES2025 baseline target. |
| `Standard` | ECMAScript-standard surface already within the main compatibility target. |
| `Other` | Non-standard or mixed cases where spec-state is not the important dimension. |

### Okojo disposition

| Disposition | Meaning |
| --- | --- |
| `UnsupportedByPolicy` | Intentionally unsupported; not a current implementation target. |
| `DeferredImplementation` | Valid target, but still unimplemented or postponed. |
| `IntentionalDeviation` | Known tradeoff or deliberate temporary divergence from preferred behavior. |
| `EnvironmentOrHarness` | Skip is caused by checkout, fixture, or harness limitations rather than engine semantics. |
| `Other` | Residual bucket when the skip is known but not yet worth finer specialization. |

### Priority

| Priority | Meaning |
| --- | --- |
| `High` | Near-term fix target inside baseline semantics. |
| `Medium` | Valid follow-up work, but not the current top slice. |
| `Low` | Deferred until more foundational work or baseline changes happen. |
| `None` | Intentionally not prioritized under current policy. |

## Excluded metadata features

These come from `ExcludedFeatureEntries` and seed the default `excludedFeatures` set in the runner.

| Feature | Classification | Why it is excluded now |
| --- | --- | --- |
| `__proto__` | `Legacy/UnsupportedByPolicy/None` | Legacy `__proto__` web-compat surface is intentionally unsupported. |
| `__getter__` | `Legacy/UnsupportedByPolicy/None` | Deprecated legacy accessor API. |
| `__setter__` | `Legacy/UnsupportedByPolicy/None` | Deprecated legacy accessor API. |
| `__lookupGetter__` | `Legacy/UnsupportedByPolicy/None` | Deprecated legacy accessor API. |
| `__lookupSetter__` | `Legacy/UnsupportedByPolicy/None` | Deprecated legacy accessor API. |
| `explicit-resource-management` | `Proposal/DeferredImplementation/Low` | Stage-3 proposal, outside the current baseline. |
| `ShadowRealm` | `Proposal/DeferredImplementation/Low` | Active proposal surface, not yet finished. |
| `immutable-arraybuffer` | `Proposal/DeferredImplementation/Low` | Active proposal surface, not yet finished. |
| `joint-iteration` | `Proposal/DeferredImplementation/Low` | Active proposal behind `Iterator.zip` and related helpers. |
| `legacy-regexp` | `Proposal/DeferredImplementation/Low` | Still tracked as proposal coverage, not baseline. |
| `await-dictionary` | `Proposal/DeferredImplementation/Low` | Active proposal surface, not baseline. |
| `import-defer` | `Proposal/DeferredImplementation/Medium` | Proposal for deferred module evaluation. |
| `source-phase-imports` | `Proposal/DeferredImplementation/Medium` | Proposal for source-phase import semantics. |
| `source-phase-imports-module-source` | `Proposal/DeferredImplementation/Medium` | Proposal for `import source` / module-source semantics. |
| `Temporal` | `FinishedProposalNotInBaseline/DeferredImplementation/High` | Finished proposal and future target, but not inside the current baseline slice. |
| `Error.isError` | `FinishedProposalNotInBaseline/DeferredImplementation/Medium` | Finished proposal tracked after the current baseline. |
| `upsert` | `FinishedProposalNotInBaseline/DeferredImplementation/Medium` | Finished proposal tracked after the current baseline. |

## Current path-skip inventory

`SkipList.Entries` currently contains **298** explicit path rules.

| Count | Classification | Interpretation |
| --- | --- | --- |
| 181 | `Standard/UnsupportedByPolicy/None` | Mostly direct-`eval`, `with`, and species-policy exclusions: standard surface, but intentionally not supported under current engine policy. |
| 72 | `Legacy/UnsupportedByPolicy/None` | Legacy/deprecated semantics such as `__proto__`, old octal, `function.caller`, `function.arguments`, `arguments.callee`. |
| 26 | `Proposal/DeferredImplementation/Low` | Proposal-only coverage such as `Iterator.zip`, `import.defer`, `import source`, and decorators syntax. |
| 8 | `Standard/DeferredImplementation/High` | Standard semantics intentionally skipped because the implementation slice is still open; currently dynamic import attributes syntax. |
| 5 | `Standard/DeferredImplementation/Medium` | Standard semantics not fully modeled yet, such as unresolvable assignment timing, regexp case-mapping differences, and some bounds/SharedArrayBuffer coverage. |
| 2 | `Other/IntentionalDeviation/Low` | Edge cases that recurse into engine stack overflow and are not useful conformance targets right now. |
| 2 | `Standard/IntentionalDeviation/Medium` | Temporary semantic tradeoffs, currently module lexical TDZ `typeof` and a `matchAll` observability tail. |
| 1 | `Other/EnvironmentOrHarness/Low` | Fixture checkout issue: LF source-text preservation cannot be validated when the file is checked out as CRLF. |
| 1 | `Standard/DeferredImplementation/Low` | Standard-but-staging path tied to resizable/growable buffer integrity work. |

### Representative examples

| Classification | Examples |
| --- | --- |
| `Standard/UnsupportedByPolicy/None` | `language/eval-code/direct/*`, `language/expressions/function/unscopables-with.js`, `built-ins/Array/Symbol.species/*` |
| `Legacy/UnsupportedByPolicy/None` | `language/literals/numeric/legacy-octal-integer.js`, `language/expressions/object/__proto__*`, `language/statements/function/13.2-*.js` |
| `Proposal/DeferredImplementation/Low` | `built-ins/Iterator/zip/*`, `language/expressions/dynamic-import/syntax/valid/*import-source*`, `language/statements/class/decorator/*` |
| `Standard/DeferredImplementation/High` | `language/expressions/dynamic-import/syntax/valid/*import-attributes*` |
| `Standard/DeferredImplementation/Medium` | `language/identifier-resolution/assign-to-global-undefined.js`, `language/literals/regexp/u-case-mapping.js`, `language/expressions/class/subclass-builtins/subclass-SharedArrayBuffer.js` |
| `Standard/IntentionalDeviation/Medium` | `language/module-code/instn-local-bndng-let.js`, `built-ins/String/prototype/matchAll/flags-undefined-throws.js` |
| `Other/IntentionalDeviation/Low` | `built-ins/Array/prototype/sort/precise-comparefn-throws.js`, `built-ins/Array/prototype/sort/precise-prototype-accessors.js` |
| `Other/EnvironmentOrHarness/Low` | `built-ins/Function/prototype/toString/line-terminator-normalisation-LF.js` |

## How to use the taxonomy

### Highest-value near-term buckets

1. `Standard/DeferredImplementation/High`
2. `Standard/DeferredImplementation/Medium`
3. `Standard/IntentionalDeviation/Medium`

These are the main compatibility backlog for the current baseline.

### Usually do not prioritize

1. `Legacy/UnsupportedByPolicy/None`
2. `Standard/UnsupportedByPolicy/None`
3. `Proposal/DeferredImplementation/Low`

These are intentionally unsupported policy surfaces or proposal-stage coverage that should not crowd out baseline work.

### Special handling

- `FinishedProposalNotInBaseline/*` means the feature is likely real future work, but should be tracked as **baseline expansion**, not as a current regression.
- `EnvironmentOrHarness` should be fixed in repo/harness plumbing, not in the runtime.
- `Other/IntentionalDeviation` should stay explicit, because these are conscious conformance tradeoffs rather than missing implementation.

## Guidance for future updates

When adding a new skip:

1. prefer a feature entry if the exclusion is metadata-driven
2. otherwise add a path entry with a reason that classifies cleanly
3. choose the status/disposition on purpose; do not collapse everything into "unsupported"
4. raise priority only for baseline-standard behavior that should realistically be fixed soon

When removing a skip:

1. remove the path or feature entry
2. keep the classification counts in mind when reviewing progress
3. if the work changes baseline scope, update the top-level planning docs as well
