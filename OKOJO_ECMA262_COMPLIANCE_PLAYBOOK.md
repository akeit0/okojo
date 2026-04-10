# ECMA-262 Compliance Playbook (Okojo)

This playbook tracks ECMAScript coverage in the Okojo path (`src/Okojo`, `tests/Okojo.Tests`, `tools/Test262Runner`) and separates features by:

- `Supported`
- `In progress`
- `Not yet`
- `Won't be supported by design`

Refresh date: `2026-04-09`.

## 0) Quick status snapshot (latest compliance intent)

Use this as a gate check for PRs and planning.

### Language & Grammar

- `Supported`
  - Non-legacy, non-staging `test262` language coverage is currently passing.
  - Core modern ECMAScript syntax required by the current engine/runtime profile is in the supported baseline.
- `In progress`
  - Stability work to keep the passing baseline intact during API cleanup and compiler/runtime refactors.
  - Experimental compiler completion and adoption.
- `Not yet`
  - Staging proposal coverage that is outside the current non-staging target.
  - Proposal families intentionally deferred until they justify implementation cost.
- `Won't be supported by design`
  - `with` statement semantics.
  - direct `eval`-special behavior (`eval` treated as normal/global `eval`).
  - `legacy` `__proto__` behavior.
  - deprecated accessor APIs:
    - `Object.prototype.__defineGetter__`
    - `Object.prototype.__defineSetter__`
    - `Object.prototype.__lookupGetter__`
    - `Object.prototype.__lookupSetter__`
  - `Function.prototype.arguments`, `Function.prototype.caller`, `arguments.callee`.

### Built-ins / Runtime API

- `Supported`
  - The non-legacy, non-staging `test262` target is currently passing for the supported baseline.
  - Core built-ins and runtime semantics are in maintenance mode rather than catch-up mode.
- `In progress`
  - Performance and allocation improvements in hot runtime paths.
  - `Okojo.Node` compatibility improvements.
  - Browser-adjacent host/runtime validation through more realistic integration targets.
- `Not yet`
  - Selected staging proposals, including likely candidates such as `Temporal`.
  - Broader host/platform behavior outside the current core-ECMAScript pass target.
- `Won't be supported by design`
  - Select legacy compatibility behavior that adds semantic complexity without engine-value.

## 1) How to prepare the checklist (recommended process)

1. Create/maintain one living matrix row per ECMA feature family you touch.
2. For each row, pin to one section in ECMA-262 and one or more local evidence points:
   - feature doc (`docs/OKOJO_FEATURE_*.md`)
   - focused `tests/Okojo.Tests`
   - `tools/Test262Runner` exact file/filter result
3. Before merging, fill one status cell + one `next action` row for every changed family.
4. If coverage changes, update both this checklist and `TODO.md` with deferred items.

Use this template in PR descriptions:

```text
Feature family:
ECMA section:
Status:
Evidence:
Remaining gap:
Action next:
```

## 2) CLI reading workflow (ECMA-262 study skill)

The goal is fast and repeatable section reading before implementation.

### 2.1 Resolve latest spec source with a script

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Catalog
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Section sec-ecmascript-language-expressions
```

### 2.2 Read one section as Markdown

The command above produces:

- `artifacts/ecma262/cache/ecmascript-language-expressions/ecmascript-language-expressions.html`
- `artifacts/ecma262/markdown/ecmascript-language-expressions/sec-ecmascript-language-expressions.md`
- `artifacts/ecma262/markdown/ecmascript-language-expressions/sections.md`
- `artifacts/ecma262/index/ecma262-section-links.txt`
- `artifacts/ecma262/index/ecma262-section-ids.txt`

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Section proxy-object-internal-methods-and-internal-slots
```

### 2.3 Read a section from direct URL

```powershell
$section = "sec-ecmascript-language-expressions"
$sectionUrl = "https://tc39.es/ecma262/multipage/ecmascript-language-functions.html#$section"
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -SectionUrl $sectionUrl -Section $section
```

Open the generated Markdown for stable reading and use `rg` over the saved Markdown to locate:

```powershell
rg -n "step|Runtime Semantics|Abstract Operation|Perform" artifacts\ecma262\markdown\ecmascript-language-expressions\$section.md
```

### 2.4 Link back to local evidence

```powershell
# find matching Test262 files that mention this family
if (Test-Path test262) {
  rg -n "Array.prototype|ArrayIterator|Proxy|Map|TypedArray|for-of|for-in|arguments|with" test262\ -g "*.js" | Select-Object -First 200
} else {
  Write-Output "test262 submodule not present locally. Run `git submodule update --init` (or repository-specific init) first."
}

# quick local status baseline from tests
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj --filter FullyQualifiedName~Feature -c Release
```

## 3) Suggested checklist file shape (copy for each feature family)

Add rows as needed and keep section IDs exact for traceability.

```text
## Feature Family: Proxy
- ECMA Section: https://tc39.es/ecma262/multipage/proxy-object.html#sec-proxy-object-internal-methods-and-internal-slots
- Status: In progress
- Evidence:
  - tests/Okojo.Tests/ProxyTests.cs
  - docs/OKOJO_FEATURE_PROXY_PHASE1.md
- Test262 slice: language / built-ins / Proxy
- Last reviewed: YYYY-MM-DD
- Next: close trap-order mismatch for delete/defineProperty
```

## 4) Update cadence (small, weekly is enough)

- During feature work: update `in progress` / `supported`.
- During planning: mark staging or host-platform gaps as `not yet`.
- During architecture review: add new `won't be supported by design` items only when there is a concrete reason.
- End of milestone: snapshot the checklist and record it in the PR description.

## 5) Output format for governance

At the end of every feature slice, update:

- `OKOJO_ECMA262_COMPLIANCE_PLAYBOOK.md` (status row changes only)
- `TODO.md` (deferred behavior)
- issue/PR progress notes
- compliance notes if staging or host-platform scope changes
