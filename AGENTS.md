# AGENTS

This file defines contributor and agent operating rules for **Okojo** work inside Okojo.

## Scope

This `AGENTS.md` applies to:

- `src/Okojo`
- `tests/Okojo.Tests`
- `docs/` (Okojo design/workflow docs)
- Okojo tooling (`tools/OkojoBytecodeTool`, `tools/V8BytecodeTool`, `tools/Test262Runner`)
- Okojo top-level planning and compliance documents (`OKOJO_*.md`, `TEST262_*.md`, root `README.md`, root `LICENSE`)

For non-Okojo work, follow the relevant project docs for that area.

## Current Direction (Okojo)

Primary goal: a **correct, observable, high-performance, web-browser-compatible JavaScript engine**.

Compatibility target:

- reach practical browser-level behavior for modern JavaScript
- pass all `test262` tests except intentionally unsupported legacy coverage and staging coverage
- move closer to ECMA-262 compatibility in both runtime semantics and embedding API shape

Implementation order:

1. correctness
2. observability/tooling
3. measured optimization

## Current Status Snapshot (Okojo)

Working baseline:

- register VM + compiler actively used
- all non-legacy, non-staging `test262` coverage is currently passing
- module/runtime/compiler path remains under simplification/refinement
- API split work is still active across `Okojo`, `Okojo.Hosting`, `Okojo.Diagnostics`, and `Okojo.WebPlatform`
- `Okojo.Node` compatibility remains an active integration target

Current architecture direction:

- transition-shape remains default object model
- dictionary fallback for dynamic churn, with JSON-first usage focus
- keep object model field count low and avoid unnecessary mode branches
- keep builder-first embedding as the preferred public composition path
- move from compliance catch-up toward API cleanup, performance, staging support, and host/browser validation

## API Direction

`src/Okojo` should move toward a clearer API split to support browser-grade compatibility work without leaking unstable internals as the default embedding surface.

Prefer these layers:

1. stable embedding API
2. host integration API
3. diagnostic/tooling API
4. internal runtime/compiler/object model implementation

For new embedding examples and integration work, prefer `JsRuntimeBuilder` composition over adding more policy to raw `JsRuntime` constructor overloads.

Avoid making runtime object internals, parser AST internals, or VM helper types part of the long-term default public surface unless there is a concrete embedding requirement.

## Philosophy

- use references to reduce wrong local decisions:
  - language/compiler/VM => V8
  - built-ins/runtime APIs => Node
- keep hot path simple; push uncommon semantics to explicit slow paths
- prefer incremental, measurable changes over large rewrites
- always add/keep regression tests for fixed failures
- prefer browser-compatible observable behavior over convenience shortcuts in public APIs

## Near-Term TODO Focus

1. refine the stable embedding and host API boundaries
2. reduce runtime allocation/branch overhead on hot property paths
3. keep module/runtime simplification moving without reintroducing wrapper-heavy paths
4. complete the experimental compiler and make it usable in real execution paths
5. add selected staging ECMA-262 support where it is worth carrying, starting with candidates such as `Temporal`
6. improve `Okojo.Node` compatibility against real Node-facing workloads
7. attempt real HTML/CSS renderer integration to exercise DOM-manipulation browser compatibility
8. keep shape/dictionary work aligned with hot-path simplicity

## Core Rules (Runtime/Compiler)

- Keep frame layout/opcode operand conventions as ABI contracts.
- Keep numeric index keys out of shape transitions.
- Keep atom/string conversion out of hot paths unless semantically required.
- Keep slow semantics in explicit slow paths.
- Prefer multi-pass compiler structure (discover -> resolve/capture -> allocate -> emit).
- Do not reintroduce old fast-path experiments without profiling evidence.

## Deprecated/Legacy Feature Policy

Okojo intentionally does **not** prioritize deprecated/legacy features unless explicitly re-approved for a concrete need.

Treat as unsupported by default:

- direct-eval-specific semantics (direct `eval` is treated as normal/global `eval`)
- `with` statement semantics
- legacy `__proto__`-specific behavior
- deprecated legacy accessor APIs:
  - `Object.prototype.__defineGetter__`
  - `Object.prototype.__defineSetter__`
  - `Object.prototype.__lookupGetter__`
  - `Object.prototype.__lookupSetter__`
- `Function.prototype.arguments` / `Function.prototype.caller`
- `arguments.callee`

If such behavior appears in Test262, prefer intended skip with a clear reason rather than regressing core paths.

## Tooling Rules (Required)

Before debugging/optimizing non-trivial behavior:

1. inspect emitted Okojo bytecode (`tools/OkojoBytecodeTool`)
2. inspect VM trace for execution mismatch
3. inspect V8 Ignition behavior for language/compiler/VM (`tools/V8BytecodeTool`, `node --print-bytecode`)
4. inspect Node behavior for built-in/runtime API (`node -e` repro)
5. optionally inspect QuickJS for design ideas
6. record short findings: copy vs intentional difference

### Reference Policy

- **Language/compiler/VM feature** -> V8 reference (primary)
- **Built-in/runtime API feature** -> Node behavior (primary)

## OkojoBytecodeTool Workflow (Required)

- Keep input cases in: `artifacts/okojobytecodetool/cases/*.js`
- Save snapshots only to timestamped dirs:
  - `artifacts/okojobytecodetool/snapshots/<yyyyMMdd-HHmmss>/*.disasm.txt`
- Do not use `cases/*.disasm.txt` as baseline storage.
- Keep fixed references (if needed) under:
  - `artifacts/okojobytecodetool/baseline/`

## Feature Kickoff Rule (Required)

For every new Okojo feature (syntax or runtime):

1. Create/update a short note in `docs/` containing:
   - scope for this iteration
   - minimal JS repros
   - planned tests (file targets)
   - reference observations:
     - V8 for language/compiler/VM
     - Node for built-ins/runtime APIs
   - optional QuickJS notes
   - copy vs intentional difference
   - perf plan (hot path/slow path/allocation risks)
2. Implement parser/compiler/runtime changes.
3. If deferring work, add deferred items to `TODO.md`.

Template:

- `docs/OKOJO_FEATURE_NOTE_TEMPLATE.md`

Also follow:

- `docs/FEATURE_IMPLEMENTATION_WORKFLOW.md`

## Planning Doc Rule

For any substantial API, compatibility, or compliance roadmap change, update the relevant top-level planning doc as part of the same work.

Current top-level anchor document:

- `OKOJO_BROWSER_COMPATIBILITY_PLAN.md`

Supporting architecture document for the current next slice:

- `OKOJO_API_ASSEMBLY_TASK_QUEUE_PLAN.md`

## Test Workflow (Required)

### Fast local loop

```powershell
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj
```

When doing repeated focused runs, prefer:

```powershell
dotnet build tests/Okojo.Tests/Okojo.Tests.csproj
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj --no-build --filter <Name>
```

This avoids transient `Okojo.dll` copy/file-lock races during tight local iteration.

Discipline:

- Do not run `dotnet build` and `dotnet test` in parallel.
- If a build is needed, wait for the build to finish before running tests.
- If only one verification command is needed, prefer a single `dotnet test ...` command instead of `build` plus `test --no-build`.
- Use `build` + `test --no-build` only for repeated focused loops after the sequential build has completed.

### Test262 workflow

`tools/Test262Runner -help`

### Test262 prioritization

Fix order:

1. `EngineInternal` / crash / corruption
2. compiler/VM contract bugs
3. parser correctness
4. runtime semantics (`TypeError`, `ReferenceError`, assertions)
5. unsupported paths (`NotSupported`)

## Collaboration Rules

- Do not revert user refactors unless explicitly requested.
- Prefer minimal/local fixes with nearby regression tests.
- For generated sources, do not reintroduce monolithic generated files (keep split workflow).
- When changing public API in `src/Okojo`, document the intended stable layer and the internal layer boundary.
