# Okojo Concrete Architecture

## Purpose

This document defines the concrete package and runtime architecture for Okojo as it exists after the current API cleanup.

It replaces older speculative layering notes where they conflict with the current direction.

## Package Split

### `src/Okojo`

Required engine/container package.

Owns:

- ECMAScript runtime, realms, jobs, microtasks, and execution
- low-level embedder seams
- low-level host abstractions that advanced embedders must use without extra package dependencies
- debugger/checkpoint primitives
- fast embedder-facing APIs such as `CallInfo`, shape/layout APIs, and intrinsics when they are part of the intended advanced host path

Does not own:

- default host implementations
- browser policy
- Node policy
- portable Web APIs

### `src/Okojo.Hosting`

Optional default host layer.

Owns:

- default schedulers and event-loop implementations
- host-side adapters and convenience composition helpers
- worker/runtime utilities
- host turn runners and diagnostics

Does not own:

- required low-level abstractions needed by embedders
- browser or Node runtime policy

### `src/Okojo.WebPlatform` (`Okojo.WebPlatform`)

Portable Web-runtime layer for non-browser hosts.

Target hosts:

- Node-like hosts
- Deno-like hosts
- Bun-like hosts
- service/server hosts
- app/script hosts that want Web-style APIs without a real browser

Owns:

- `fetch`
- `AbortController` / `AbortSignal`
- timers
- `queueMicrotask`
- base64 helpers and similar runtime-safe Web APIs
- portable `Worker` APIs when they do not require browser-only policy

Does not own:

- `window`
- `self`
- rendering policy
- animation frame policy
- DOM abstractions

### `src/Okojo.Browser`

Optional browser-shaped runtime profile.

Owns:

- `window` / `self`
- `requestAnimationFrame` / `cancelAnimationFrame`
- rendering queue policy
- browser event-loop presets
- future DOM-adjacent abstractions

Depends on:

- `Okojo`
- `Okojo.Hosting`
- `Okojo.WebPlatform`

### `src/Okojo.Node`

Optional Node-shaped runtime profile.

Owns:

- Node globals and built-ins
- CommonJS and Node-specific module/runtime policy
- Node event-loop profile behavior such as `setImmediate`

Does not own:

- generic shared Web APIs like `AbortController`

## Event Loop Ownership

### Core engine

`Okojo` owns:

- ECMAScript jobs
- Promise job ordering
- microtask checkpoints

### Host layer

`Okojo.Hosting` owns:

- host task queues
- delayed scheduling
- host loop implementations
- queue pumping and turn observation

### Runtime profiles

`Okojo.WebPlatform`, `Okojo.Browser`, and `Okojo.Node` map runtime APIs onto host queues.

Current intended queue ownership:

- portable web timers: `Okojo.WebPlatform`
- browser rendering queue: `Okojo.Browser`
- Node check queue: `Okojo.Node`

Core `Okojo` should not standardize browser or Node queue names.

## Builder Policy

### `JsRuntimeBuilder`

This is the primary composition surface.

Keep:

- simple/common host-neutral configuration directly on `JsRuntimeBuilder`
- advanced host wiring under `UseLowLevelHost(...)`

### Profile builders

Profile builders must stay thin.

They should:

- expose profile-specific APIs
- expose one core escape hatch such as `ConfigureRuntime(...)`

They should not become parallel copies of `JsRuntimeBuilder`.

## Naming Policy

### Public package names

- `Okojo` = engine
- `Okojo.Hosting` = default host implementations and utilities
- `Okojo.WebPlatform` = portable Web-runtime API layer
- `Okojo.Browser` = browser-shaped profile
- `Okojo.Node` = Node-shaped profile

### Type prefixes

- do not use `Okojo` as a general type prefix
- use `Js` when the type names a JavaScript/runtime concept
- otherwise use the domain name directly, such as `HostingBuilder`, `WorkerRuntime`, or `WebAssemblyBuilder`
- namespace and package identity already communicate that the type belongs to Okojo

### JavaScript-visible names

Do not expose Okojo-internal bridge names like `__js*` on user-visible globals.

Host bridge state should live in:

- host-defined state on the C# side
- symbols or non-user-facing internal bridges when unavoidable

## Current Concrete Rules

1. Required low-level abstractions stay in `Okojo`.
2. Overridable defaults and helpers stay in `Okojo.Hosting`.
3. Portable Web APIs stay in `Okojo.WebPlatform`.
4. Browser-only policy stays in `Okojo.Browser`.
5. Node-only policy stays in `Okojo.Node`.
6. Backward compatibility is not a reason to keep the wrong package boundary.
