# Okojo Browser Compatibility Plan

## Purpose

This document defines the top-level direction for turning `src/Okojo` into a much more browser-compatible JavaScript engine with stronger ECMA-262 alignment and a cleaner embedding API.

Target outcome:

- Okojo behaves close enough to a modern browser JavaScript engine that web-style code and browser-adjacent libraries run with fewer semantic surprises.
- Okojo passes all `test262` tests except:
  - intentionally unsupported legacy coverage
  - staging-only coverage
- `src/Okojo` exposes a smaller, cleaner, more durable public API focused on embedding and host integration instead of leaking broad runtime internals.

## Current status

Current baseline:

- the non-legacy, non-staging `test262` target is currently achieved
- the next compatibility phase is no longer core ECMAScript catch-up; it is browser-host validation and adjacent platform work
- API cleanup and performance work now matter more than raw core-compliance count reduction

Current next-phase priorities:

1. refine the stable embedding and host API shape
2. improve runtime performance and allocation behavior on hot paths
3. add selectively chosen staging features that are worth carrying, such as `Temporal`
4. complete and adopt the experimental compiler path
5. improve `Okojo.Node` compatibility against real Node-facing workloads
6. attempt real HTML/CSS renderer integration to test DOM-manipulation browser compatibility

## Remaining Problem

`src/Okojo` already contains substantial parser, compiler, VM, runtime, object-model, and built-in implementation work. The current engine surface is usable, but several concerns are still mixed together:

- embedding API
- realm/bootstrap API
- module and worker host hooks
- host interop and CLR exposure
- parser/bytecode/debug tooling types
- low-level runtime object types

That mixing makes it harder to:

- evolve internals without API churn
- enforce browser-grade observable behavior consistently
- reason about which APIs are stable vs experimental
- drive spec compliance work without exposing implementation details as public contracts

## Compatibility Goal

Primary compatibility target:

- ECMA-262 behavior first
- browser-like observable semantics for language/runtime behavior
- keep all non-legacy, non-staging `test262` tests passing while expanding host/browser compatibility

Compatibility statement for planning purposes:

- Okojo is not trying to clone one browser engine implementation.
- Okojo should match the ECMAScript spec and browser-observable behavior closely enough that browser-portable JavaScript behaves the same unless an intentional unsupported policy says otherwise.

## Non-Goals

These remain intentionally de-prioritized unless re-approved for a concrete use case:

- direct-eval-specific semantics
- `with`
- legacy `__proto__` quirks beyond required modern semantics
- legacy getter/setter helper APIs
- `Function.prototype.arguments`
- `Function.prototype.caller`
- `arguments.callee`
- staging proposal completeness before core ECMA-262 completion

## Reference Policy

Use references to reduce wrong local decisions:

- language, parser, compiler, VM semantics: V8 first
- built-ins and runtime APIs: Node first
- QuickJS: optional design reference, not primary behavior authority

Because Okojo is heavily influenced by V8:

- use V8 to understand intended execution model, bytecode/VM tradeoffs, and edge-case semantics
- do not copy V8-specific API layering blindly when Okojo embedding needs differ
- keep the legal boundary explicit: V8 remains separate third-party code under `./v8`

## API Split Goal

`src/Okojo` should be refactored toward four clear API layers.

Compiler architecture note:

- see [docs/OKOJO_MULTI_PASS_COMPILER_DESIGN.md](docs\OKOJO_MULTI_PASS_COMPILER_DESIGN.md) for the target multi-pass compiler direction, especially binding collection, capture planning, storage planning, and register allocation cleanup

### 1. Stable Embedding API

This is the default public surface for consumers embedding Okojo in applications.

Should cover:

- engine construction
- realm creation/use
- script/module execution
- value conversion and result handling
- exception reporting
- configurable standard host features

Should avoid exposing:

- low-level runtime object subclasses
- compiler internals
- AST internals as default API
- VM bytecode builder internals
- object-shape/layout internals

Candidate types that belong conceptually here:

- `JsRuntime`
- `JsRuntimeOptions` as reusable lower-level state, with host-facing conveniences kept in host packages
- a smaller stable realm/session API
- stable value wrappers or conversion helpers
- stable exception/result APIs

### 2. Host Integration API

This layer should handle environment-dependent services and browser-like embedding hooks.

Should cover:

- module resolution/loading
- worker script loading
- timers and scheduling
- microtask/job queue integration
- host callbacks and host object binding
- optional host capabilities (filesystem, diagnostics, interop, browser shims)

This layer should make browser-host-like embedding possible without coupling every host concern to core engine internals.

Candidate areas already present:

- `IModuleLoader`
- `IWorkerScriptLoader`
- timer/wait/scheduler abstractions
- host interop abstractions
- realm API modules

### 3. Diagnostics and Tooling API

This layer should support correctness and observability work without contaminating the stable embedding layer.

Should cover:

- parser access for tooling
- bytecode emission/disassembly
- VM tracing/debug formatting
- compliance instrumentation
- internal inspection utilities used by OkojoBytecodeTool/Test262 work

Candidate areas already present:

- `JavaScriptParser`
- `OkojoScript`
- `JsDisassembler`
- `OkojoBytecodeBuilder`
- trace/debug formatting utilities

These APIs may stay public if needed for tooling, but they should be documented as diagnostic/tooling-oriented rather than core embedding contracts.

### 4. Internal Runtime Surface

This is where Okojo should be free to change aggressively while pursuing spec compatibility and performance.

Includes:

- runtime object subclasses
- object model and shapes/layouts
- compiler contexts and scopes
- VM helper/runtime ID internals
- built-in bootstrap wiring
- inline caches and hot-path helpers

Rule:

- do not let internal runtime convenience decide the public API shape

## Immediate API Cleanup Targets

The current public surface appears broader than the desired stable embedding boundary. The next cleanup passes should focus on these areas.

### A. Reduce Accidental Public Runtime Types

Many concrete runtime object classes are public today. Most of them should not be the default user-facing contract.

Target direction:

- keep only clearly necessary public types public
- make implementation-only runtime object types internal where safe
- introduce stable abstractions for values/results instead of exposing object subclasses

### B. Separate Engine Construction from Host Policy

`JsRuntimeOptions` currently mixes stable settings with internal scheduler/wait infrastructure and host extension registration patterns.

Target direction:

- keep user-facing options focused on stable engine capabilities
- move host/environment wiring into clearer host configuration objects or builders
- keep direct `JsRuntime` constructors transitional, not the preferred embedding path
- make internal-only configuration internal by default

### C. Clarify Realm vs Engine Responsibilities

`JsRuntime`, `JsAgent`, and `JsRealm` should present a clearer ownership model.

Target direction:

- engine owns lifetime and shared services
- agent owns concurrency/isolation model
- realm owns execution environment and intrinsics

Public API should guide embedders into this model instead of forcing them to infer it from internals.

### D. Make Browser-Host Features Explicit

Browser-grade compatibility needs host-facing seams for:

- job/microtask handling
- event-loop integration
- module graph loading
- worker lifecycle
- host-defined globals
- DOM/Web API layering outside core ECMA-262

Core Okojo should not silently hardcode these policies. It should expose explicit hooks or extension points.

### E. Mark Diagnostic APIs Explicitly

Some APIs are very useful for tooling but should not be mistaken for the stable runtime embedding contract.

Target direction:

- document them as tooling/diagnostic APIs
- optionally move them under clearer namespaces later

## Compatibility Workstreams

### 1. Parser and Early Errors

Goals:

- close syntax correctness gaps
- align strict-mode and early-error behavior with spec
- keep unsupported legacy features intentionally gated

Success markers:

- parser-focused Test262 failures become mostly real unsupported-policy cases or resolved bugs

### 2. Compiler / VM Contract Correctness

Goals:

- keep frame layout and opcode ABI stable
- fix control-flow, binding, capture, and module execution mismatches
- inspect bytecode and V8 behavior before speculative changes

Success markers:

- fewer `EngineInternal`, crash, and miscompile failures
- fewer discrepancies between bytecode intent and runtime behavior

### 3. Object Model and Property Semantics

Goals:

- continue Proxy/Reflect trap ordering fixes
- tighten descriptor invariants
- preserve numeric-index policy
- maintain simple hot paths with explicit slow semantic fallback

Success markers:

- large reductions in Reflect/Object/Proxy failures
- fewer special-case regressions in exotic object behavior

### 4. Built-ins and Realm Initialization

Goals:

- complete constructor/prototype behavior
- tighten property attributes and error behavior
- remove bootstrap inconsistencies
- ensure realm startup produces browser-like observable built-ins

Success markers:

- built-in family failures trend toward corner cases rather than broad missing coverage

### 5. Modules, Jobs, and Async Semantics

Goals:

- simplify module path while preserving live-binding correctness
- define host job queue and async integration more cleanly
- improve worker/module coordination APIs

Success markers:

- module and async failures are diagnosable through stable host seams rather than ad hoc runtime coupling

### 6. Host Interop Boundaries

Goals:

- keep CLR interop opt-in
- avoid host convenience features distorting spec behavior
- define what belongs in core Okojo vs host extension packages/modules

Success markers:

- host interop remains useful without polluting ECMA-262 semantics

## Test262 Completion Strategy

Desired rule:

- every fixed failure gets a focused regression test near the implementation area when practical

Prioritization:

1. `EngineInternal`, crashes, corruption
2. compiler/VM contract bugs
3. parser correctness
4. runtime semantics (`TypeError`, `ReferenceError`, assertions)
5. unsupported paths that should become supported
6. intentional legacy/staging skips with clear reason

Execution model:

- work by failure family, not random single tests
- use V8/Node references before semantic changes
- record copy vs intentional difference
- keep unsupported legacy behavior explicitly documented rather than accidentally half-implemented

Definition of done for the main compliance goal:

- all `test262` tests pass except:
  - legacy exclusions approved by project policy
  - staging exclusions approved by project policy

## Suggested `src/Okojo` Package/Namespace Direction

This is a direction, not an immediate mandatory rename plan.

Potential future grouping:

- `Okojo`
  - stable embedding API
- `Okojo.Hosting`
  - module/worker/scheduler/host service contracts
- `Okojo.Diagnostics`
  - parser, bytecode, tracing, disassembly
- `Okojo.Interop`
  - host/CLR binding surface
- `Okojo.Internal` or internal-only namespaces
  - runtime/compiler/object model internals

The main goal is not aesthetic namespace cleanup. The main goal is to make compatibility work safer by reducing accidental public contracts.

## Documentation Rules For This Roadmap

When major work changes the plan, update this document together with code/docs.

Every substantial feature or compliance slice should still keep its own focused note under `docs/`, but this file is the top-level coordination document for:

- browser-compatibility target
- API split direction
- Test262 completion target
- stable vs internal Okojo surface policy

Current supporting top-level inventory:

- `OKOJO_PUBLIC_API_INVENTORY.md`
- `OKOJO_API_ASSEMBLY_TASK_QUEUE_PLAN.md`
- `docs/OKOJO_NODE_RUNTIME_PLAN.md`

## Licensing Note

Okojo should have its own root project license.

Also keep these boundaries explicit:

- Okojo uses V8 as a primary semantic and architectural reference
- Okojo is heavily influenced by V8 study

## 2026 Execution Priorities

1. finish narrowing the compatibility target to "all non-legacy, non-staging Test262"
2. split host configuration concerns from stable engine options
3. define task queue ownership so ECMAScript jobs stay in core and host tasks stay host-driven
4. define target assembly boundaries for `Okojo`, `Okojo.Hosting`, `Okojo.Diagnostics`, and `Okojo.WebPlatform`
5. tighten module/job/worker host seams for browser-like embedding
6. continue Proxy/Object/descriptor correctness work
7. keep shape/dictionary and property hot paths simple while preserving semantics
8. document stable API vs diagnostics vs internal runtime boundaries as changes land
