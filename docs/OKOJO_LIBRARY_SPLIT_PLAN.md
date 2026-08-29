# Okojo Library Split: Remaining Work

## Purpose

This document contains only unfinished assembly and API-boundary work. Git
history is the record for completed phases.

Delete this document and its references when the completion conditions below
are satisfied. The split is intentionally not backward compatible; do not add
facades for the former `Okojo` assembly, namespaces, or type names.

## Current package boundaries

```text
Okojo.Text.Unicode                 Okojo.Numerics
        │                                │
        ▼                                ▼
Okojo.Text.RegularExpressions     Okojo.Globalization
        └──────────────┬─────────────────┘
                       ▼
              Okojo.JavaScript
                 ┌─────┴──────────┐
                 ▼                ▼
Okojo.JavaScript.Embedding   Okojo.Diagnostics
        │
        ├──────────────► Okojo.Hosting
        ├──────────────► Okojo.Reflection
        ├──────────────► Okojo.WebAssembly
        └──────────────► Okojo.WebPlatform / Okojo.Browser / Okojo.Node
```

Arrows point from a dependency to a consumer. Cycles are forbidden.

- `Okojo.JavaScript` owns ECMAScript values, object model, parser, compiler,
  bytecode, VM, realms, agents, Promise jobs, and module semantics.
- `Okojo.JavaScript.Embedding` owns `JsRuntime`, builders, host integration
  contracts, and runtime composition. It does not install a scheduler, event
  loop, worker implementation, or message serializer.
- `Okojo.Hosting` owns optional .NET host implementations such as thread-pool
  scheduling, host pumps, worker helpers, and the default message serializer.
- Hosting, Reflection, WebAssembly, WebPlatform, Browser, and Node remain
  optional consumers. Host policy must not move into the engine.
- Engine-independent text, numeric, and globalization libraries must not depend
  on `Okojo.JavaScript` or `Okojo.JavaScript.Embedding`.

## Required behavior boundaries

- Browser and other host profiles own task readiness, selection, waiting,
  fairness, rendering opportunities, and microtask-checkpoint timing.
- Hosts control whether an agent may suspend for `Atomics.wait` and how
  synchronous waits and `Atomics.waitAsync` timeouts are scheduled through the
  public `IAtomicsWaitPolicy` contract.
- The engine owns Promise-job state and an independently callable,
  non-reentrant checkpoint that preserves FIFO order and drains recursively
  queued Promise jobs.
- Host-aware top-level-await evaluation is explicit: `JsRealm.EvaluateAsyncWithHostPump` lets an
  embedder run or await one host task between Promise-job checkpoints without moving host-task
  policy into the engine.
- No engine evaluation API may silently run an unrelated host task.
- Node-specific policy such as `nextTick` priority must not be imposed on
  browser hosts.
- Host-owned dynamic non-index string property collections extend
  `JsDynamicNamedObject`; `JsIndexedObject` builds on that seam so a host object
  can combine indexed and dynamic named properties. The engine keeps VM hook
  overrides internal, so external hosts do not need an `InternalsVisibleTo`
  relationship.
- Boundary changes must preserve run-to-completion, module behavior, worker
  delivery, timers, and cross-realm ownership checks. A warning or failing test
  is a defect, not an accepted migration state.

## Friend access policy

`InternalsVisibleTo` is allowed for official packages that are versioned and
changed in lockstep in this repository. Removing every friend relationship is
not a goal. For each use, classify it as:

- stale access to remove;
- a capability required by external embedders, which needs a supported public
  contract; or
- intentional privileged access for performance, invariant preservation, or
  tightly coupled implementation work.

Retain the third category when its reason is concrete and document that reason
next to the assembly attribute. Do not publish unstable object-model, atom,
shape, VM, or typed-array internals, and do not add wrappers or one-use
interfaces solely to eliminate a friend relationship.

## Remaining work

### Diagnostic and tooling friends

Audit the remaining non-test friends in `Okojo.JavaScript`, including
Diagnostics, DebugServer, BytecodeTool, benchmarks, and the experimental
compiler. Keep a friend only when the project is intentionally coupled tooling
and a supported public API would expose unstable implementation details. Remove
stale entries and record any intentional exceptions next to the assembly
attribute, not in another plan document.

After production boundaries are clean, review Test262Runner and test-only friend
entries separately. Test262 work and benchmark execution are not part of an API
boundary slice unless that slice directly changes them.

## Verification

For each boundary slice:

1. Run the smallest focused tests covering the changed contract.
2. Run the full `Okojo.Tests` suite after focused tests pass.
3. Build every directly affected consumer independently and build the solution
   before completion.
4. Require zero compiler warnings and zero test failures.
5. Run CSharpier on changed C# files, `git diff --check`, and the LF check.

Do not run benchmarks without a performance claim. Use the Test262 workflow
only for Test262 changes.

## Completion conditions

The split is complete when:

- every production, compiler, diagnostic, test, and tooling friend relationship
  has been reviewed;
- stale access is removed and intentional lockstep access is minimal and
  documented next to its declaration;
- engine, Embedding, Reflection, Node, and the full solution build independently
  without warnings;
- focused and full tests pass without accepted failures; and
- package, README, workflow, and example references match the final dependency
  graph.

Then delete this document and remove its references from `AGENTS.md`, README,
the browser compatibility roadmap, and package workflow documentation.
