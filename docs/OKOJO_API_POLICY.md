# Okojo API Policy

## Purpose

This document defines the intended public API policy for the Okojo `src/` projects.

It exists to prevent the public surface from drifting into:

- accidental implementation exposure
- duplicate wrapper APIs across profile packages
- unclear ownership between core engine seams and optional hosting utilities

This policy is stricter than "public because it is useful today".
A public API must belong to a named layer with a clear ownership reason.

## Core Rule

`Okojo` is the required engine package.

It owns:

- ECMAScript runtime/container APIs
- required low-level embedder seams
- required extensibility points for high-performance hosts
- required debugger/checkpoint primitives

`Okojo.Hosting` is optional.

It owns:

- overridable default implementations
- host-side adapters
- event-loop helpers
- worker/runtime utilities
- convenience composition helpers for common host setups

Do not move required low-level host abstractions out of `Okojo` just because they are "host-related".
If an embedder needs the abstraction to extend Okojo without a `Okojo.Hosting` dependency, it belongs in `Okojo`.

## Project Split

### `src/Okojo`

Owns:

- `JsRuntime`, `JsRealm`, `JsValue`, exceptions, execution entry points
- low-level host seams such as module loading, worker loading, scheduling, and queue targeting
- fast host interop APIs such as host functions, `CallInfo`, and shape/layout APIs when they are part of the intended advanced embedder path
- required debugger/checkpoint/runtime observability APIs

Does not own:

- default file/module loader implementations when they are just convenience
- optional event-loop helpers
- browser profile policy
- Node profile policy
- optional diagnostics/rendering helpers

### `src/Okojo.Hosting`

Owns:

- default host schedulers and loops
- delay schedulers
- worker runtime helpers
- hosting adapters such as message-serializer and JS-worker-host wrappers
- convenience presets such as thread-pool hosting
- helper APIs such as `HostTurnRunner`

Does not own:

- the core low-level host abstractions themselves
- required embedder-facing configuration APIs that should work without a `Okojo.Hosting` reference

### `src/Okojo.Diagnostics`

Owns:

- optional tooling
- disassembly/formatting helpers
- optional debug/report rendering

Keeps out:

- required runtime debugger/checkpoint primitives needed by embedders

### `src/Okojo.WebPlatform` (`Okojo.WebPlatform`)

Owns:

- portable web-runtime APIs that do not require a real browser or DOM
- shared platform JS APIs that are not Node-specific, such as `AbortController`
- web worker/profile composition that remains runtime-portable
- fetch/timer/microtask/global installation for non-browser hosts

### `src/Okojo.Browser`

Owns:

- browser-shaped globals and runtime presets
- rendering and animation APIs
- future DOM-adjacent abstractions
- browser-only host policy layered on top of `Okojo.WebPlatform`

### `src/Okojo.Node`

Owns:

- Node runtime/profile composition
- Node built-ins
- CommonJS/Node resolution and Node-specific policy

Does not own:

- generic platform APIs that are shared with web/server hosts

## Builder Policy

### `JsRuntimeBuilder`

This is the primary composition surface.

Its public methods should be split conceptually into:

1. simple/common configuration
2. advanced low-level host configuration

#### Simple/common configuration

Keep directly on the builder when broadly useful and host-neutral:

- `UseTimeProvider(...)`
- `UseModuleSourceLoader(...)`
- `UseWorkerScriptSourceLoader(...)`
- `UseRealmSetup(...)`
- `UseGlobal(...)`
- `UseCore(...)`
- `UseAgent(...)`
- `UseHost(...)`
- `UseRealm(...)`

#### Advanced low-level host configuration

Prefer one explicit grouping surface:

- `UseLowLevelHost(Action<JsRuntimeLowLevelHostOptions>)`

This is the preferred API for:

- custom schedulers
- worker host integration
- message serializers
- worker message queue targeting
- other non-default host seam wiring

Flat helper methods for those advanced seams may exist for compatibility, but they are not the preferred shape and should not be the model for new API growth.

### `JsRuntimeOptions`

Follow the same rule as `JsRuntimeBuilder`.

Simple/common configuration can stay as direct methods.
Advanced host seam wiring should prefer `UseLowLevelHost(...)`.

### Wrapper builders

Profile builders such as `OkojoNodeRuntimeBuilder` must stay thin.

They should:

- expose profile-specific APIs
- expose one core escape hatch, such as `ConfigureRuntime(...)`

They should not indefinitely duplicate every core builder method.

If a new core builder API is needed inside a profile builder, prefer:

- `ConfigureRuntime(builder => ...)`

over adding another forwarding method.

Forwarding methods may remain temporarily for compatibility or convenience, but they should not be the primary design direction.

## Public API Review Rules

Before adding or keeping a public API, classify it:

1. required core embedding API
2. required advanced embedder API
3. optional hosting utility/default
4. optional diagnostics/tooling API
5. profile-specific API
6. internal implementation detail

If it is category 6, it must not be public.

If it is category 3, it belongs in `Okojo.Hosting`, not `Okojo`.

If it is category 1 or 2, it must stay usable without requiring `Okojo.Hosting`.

## Current Direction Decisions

These are explicit current decisions:

- `AbortController` / `AbortSignal` are shared platform APIs, not Node-owned APIs
- debugger/checkpoint primitives remain core `Okojo` APIs
- low-level host abstractions remain in `Okojo`
- `Okojo.Hosting` provides defaults/utilities/adapters on top of those abstractions
- `OkojoNodeRuntimeBuilder` should move toward `ConfigureRuntime(...)` plus Node-specific methods, not broad forwarding
- advanced host seam configuration should prefer `UseLowLevelHost(...)`

## Naming Rule

Public and internal type naming should follow these rules unless there is a strong compatibility reason not to:

1. do not add a `Okojo` prefix to new types
2. if the type is primarily about JavaScript semantics or values, prefer a `Js` prefix
3. if the type is host-side, use a host/runtime/domain name instead of `Okojo`
4. package identity should come from the namespace and assembly, not from repeating `Okojo` in every type name

Examples:

- use `JsValue`, not `OkojoJsValue`
- use `WorkerRuntime`, not `OkojoWorkerRuntime`
- use `HostingBuilder`, not `HostingBuilder`
- use `WebAssemblyBuilder`, not `OkojoWebAssemblyBuilder`

Existing `Okojo*` names should be treated as cleanup candidates unless they are:

- package/product entry points with deliberate branding value
- compatibility shims kept temporarily during migration
- generated or external-facing protocol names that cannot be changed yet

## Anti-Patterns

Do not:

- add a public type just because tests or one sandbox can use it
- move required core seams into `Okojo.Hosting`
- copy core builder methods into every profile builder
- add browser/shared platform APIs under `Okojo.Node`
- expose parser/compiler/bytecode internals as default embedding API without an explicit tooling policy
- keep duplicate public entry points forever once a clearer primary API exists

## Practical Review Checklist

When reviewing a new public API:

- Which project should own it?
- Can an advanced embedder use this without `Okojo.Hosting` if needed?
- Is this a required seam or only a default utility?
- Does this belong on the flat builder surface, or inside `UseLowLevelHost(...)`?
- Is this profile-specific or shared across Node/Web/server hosts?
- Would this still make sense in generated API docs as intentional public API?
