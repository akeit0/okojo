# Okojo Node Runtime Plan

## Purpose

This document defines how Okojo should support running JavaScript in a Node-like host profile.

Target outcome:

- Okojo can execute code in a practical Node-style embedding mode.
- CommonJS and ECMAScript modules can coexist with Node-like resolution rules.
- Node-facing globals and built-in modules are provided by a host/runtime layer, not by core `Okojo`.
- The design stays aligned with the current Okojo direction:
  - `Okojo` = ECMAScript engine/container
  - `Okojo.Hosting` = default host/runtime infrastructure
  - `Okojo.WebPlatform` = portable web-runtime API layer
  - `Okojo.Browser` = browser-style host profile
  - `Okojo.Node` = Node-style host profile

This is a design and implementation plan, not a commitment to clone Node API surface all at once.

## Why This Should Exist

Okojo currently has:

- strong ECMA-262 engine work
- growing browser-style hosting work
- module/runtime refactors moving toward cleaner host boundaries

But "execute as Node" is a separate host profile with different observable behavior:

- CommonJS module wrapper and `require(...)`
- package and file-extension rules
- `node:` built-in modules
- `process`, `Buffer`, timers, and console behavior
- Node-style host module resolution and caching

Trying to force that into core `Okojo` would be the wrong boundary.

## Design Goal

The goal is not "put Node into `Okojo.csproj`".

The goal is:

1. keep `Okojo` as the ECMAScript engine and low-level host seam
2. add a full default Node-like runtime profile outside core
3. keep low-level host control available for embedders
4. make the default Node-like profile practical enough that real code can run without ad hoc sandbox glue

## Reference Sources

Primary behavioral references for this plan:

- Node.js CommonJS modules:
  - [Node.js Modules: CommonJS modules](https://nodejs.org/api/modules.html)
  - relevant sections:
    - `Modules: CommonJS modules`
    - `The module wrapper`
    - `require(id)`
    - `module.exports`
    - `module.filename`
    - `module.paths`
- Node.js ECMAScript modules:
  - [Node.js Modules: ECMAScript modules](https://nodejs.org/api/esm.html)
  - relevant sections:
    - `import.meta.resolve(specifier)`
- Node.js packages:
  - [Node.js Modules: Packages](https://nodejs.org/api/packages.html)
  - relevant sections:
    - `Determining module system`
    - `package.json and file extensions`
    - `Package entry points`
    - `Subpath exports`
- Node.js `node:module` API:
  - [Node.js Modules: node:module API](https://nodejs.org/api/module.html)
  - relevant sections:
    - `module.builtinModules`
    - `module.createRequire(filename)`
- Node.js process:
  - [Node.js Process](https://nodejs.org/api/process.html)
  - relevant sections:
    - `process.cwd()`
    - `process.env`
    - `process.argv`
- Node.js timers:
  - [Node.js Timers](https://nodejs.org/api/timers.html)
  - relevant sections:
    - `setImmediate()`
    - `setTimeout()`
    - `setInterval()`

Supporting language/runtime references:

- ECMA-262 Jobs and Host Operations:
  - [ECMA-262 9.5 Jobs and Host Operations to Enqueue Jobs](https://tc39.es/ecma262/multipage/executable-code-and-execution-contexts.html#sec-jobs-and-host-operations-to-enqueue-jobs)
- WHATWG HTML event loop model, only as host/event-loop background:
  - [HTML event loops](https://html.spec.whatwg.org/multipage/webappapis.html#event-loops)
  - [HTML event loop processing model](https://html.spec.whatwg.org/multipage/webappapis.html#event-loop-processing-model)

## Non-Goals

This plan does not require immediate support for:

- native addons
- Node-API / C++ addons
- exact loader hook parity with modern Node
- exact CLI/process boot parity
- inspector parity
- worker_threads parity on day one
- every built-in `node:*` module

This plan also does not require Node policy in core `Okojo`.

## Current Sandbox Checkpoint

There is now a small file-backed Node runtime sandbox at:

- `sandbox/OkojoNodeRuntimeSandbox`

It is intentionally minimal, but it gives a real on-disk checkpoint for:

- CommonJS package loading
- ESM package `"exports"` loading
- first Node built-ins (`process`, `path`, `Buffer`)

Use it as the first "real app layout" validation step before attempting larger package targets.

## What "Execute As Node" Means

For planning purposes, "execute as Node" means:

1. code can run with a Node-style entrypoint model
2. CommonJS modules work
3. Node-style ESM/package resolution works well enough for practical usage
4. Node-facing host globals and modules exist
5. host scheduling feels Node-like enough for real async code

Minimum practical observable surface:

- CommonJS:
  - `require`
  - `module`
  - `exports`
  - `__filename`
  - `__dirname`
- Node globals:
  - `process`
  - `Buffer`
  - `console`
  - timers:
    - `setTimeout`
    - `clearTimeout`
    - `setInterval`
    - `clearInterval`
    - `setImmediate`
    - `clearImmediate`
- module/builtin loading:
  - relative file loading
  - package lookup
  - `node:` built-ins

## Architecture Direction

### Core `Okojo`

`Okojo` should keep:

- ECMAScript parser/compiler/VM/runtime
- ESM semantics
- ECMAScript jobs/microtasks
- low-level host seams
- module runtime semantics

`Okojo` should not gain:

- CommonJS wrapper policy
- filesystem/package resolution policy
- `process`, `Buffer`, `fs`, `path`, `os`, or other Node host modules
- Node-specific host globals

### `Okojo.Hosting`

`Okojo.Hosting` should keep:

- general host loop/task queue infrastructure
- module source loading contracts
- background scheduler/task-pump/default runtime helpers
- default host/runtime implementations usable by multiple host profiles

### New `Okojo.Node`

Recommended new assembly:

- `src/Okojo.Node`

Responsibilities:

- Node-style runtime profile
- CommonJS loader/runtime
- Node-style module/package resolution
- Node-facing globals
- Node built-in module registry
- Node-style host helpers on top of `Okojo` + `Okojo.Hosting`

This should be the Node counterpart to `Okojo.WebPlatform`, not to `Okojo.Browser`.

## Core Design Rule

Node behavior should be built as a host/runtime profile, not as a special case in the core engine.

That means:

- CommonJS is not a new parser mode in core `Okojo`
- CommonJS modules are produced by host/runtime wrapping policy
- Node APIs are provided as host modules/globals
- Node resolution is done by a Node host resolver, not by generic `Okojo` module resolution

## Required Major Pieces

## 1. CommonJS Runtime

This is the first required feature.

### Target Behavior

A CommonJS file should behave as if wrapped in the Node-style module wrapper.

Reference:

- Node docs: `The module wrapper`
  - [Modules: CommonJS modules](https://nodejs.org/api/modules.html)

### Required Host-Visible Bindings

Per CommonJS module:

- `exports`
- `module`
- `require`
- `__filename`
- `__dirname`

### Recommended Okojo Shape

Do not teach core `Okojo` a "CommonJS bytecode kind".

Instead:

1. load source through a CommonJS loader
2. wrap source as a function body with the Node parameter list
3. compile as ordinary Okojo script/function code
4. invoke with host-created `module`, `exports`, `require`, `__filename`, `__dirname`

Conceptually:

```js
(function (exports, require, module, __filename, __dirname) {
  // original CommonJS source
});
```

### Why This Is Better

- stays close to Node observable behavior
- keeps CommonJS out of core language semantics
- reuses existing function/script compiler/runtime paths
- makes it easier to cache compiled wrapper functions by file/module id

## 2. Node Module Resolution

This is the second required feature.

### Required Support

- relative/absolute file resolution
- extension probing policy where applicable
- `node_modules` lookup
- `package.json` `"type"`
- `package.json` `"main"`
- `package.json` `"exports"` minimum practical subset
- `node:` builtin resolution

References:

- [Modules: Packages](https://nodejs.org/api/packages.html)
  - `Determining module system`
  - `package.json and file extensions`
  - `Package entry points`
  - `Subpath exports`
- [Modules: CommonJS modules](https://nodejs.org/api/modules.html)
  - `Loading from node_modules folders`
- [Modules: ECMAScript modules](https://nodejs.org/api/esm.html)
  - `import.meta.resolve(specifier)`

### Recommended Okojo Shape

Add a Node-specific resolver/loader pair in `Okojo.Node`, for example:

- `NodeModuleResolver`
- `NodeModuleLoader`
- `NodeBuiltInModuleRegistry`

Keep the existing low-level `IModuleSourceLoader` seam in `Okojo` as the engine-facing contract.

`Okojo.Node` should adapt Node resolution into those engine-facing loader hooks.

## 3. Mixed ESM + CommonJS Interop

This is required for practical Node compatibility.

### Needed Directions

- CommonJS entry loading
- ESM entry loading
- ESM importing CommonJS
- CommonJS requiring CommonJS
- CommonJS requiring ESM where supported policy is defined

### Initial Recommendation

Do not try to match every modern Node interop nuance in the first slice.

Initial target:

- robust CommonJS runtime
- robust Node-style ESM resolution
- practical ESM -> CommonJS interop
- `module.createRequire(...)` support for ESM-side CommonJS access

Reference:

- [Modules: node:module API](https://nodejs.org/api/module.html)
  - `module.createRequire(filename)`

### Why This Order

This gets useful behavior early without committing to every edge case before the base runtime is stable.

## 4. Node Globals

### Must-Have Early Globals

- `process`
- `Buffer`
- `console`
- timers:
  - `setTimeout`
  - `clearTimeout`
  - `setInterval`
  - `clearInterval`
  - `setImmediate`
  - `clearImmediate`

### Where They Should Live

In `Okojo.Node`, not in `Okojo`.

### `process`

Initial practical subset:

- `process.cwd()`
- `process.env`
- `process.argv`
- `process.platform`
- `process.version`
- `process.versions`
- `process.nextTick(...)`

Reference:

- [Process](https://nodejs.org/api/process.html)

### `Buffer`

This is important for ecosystem compatibility.

Recommendation:

- provide a Node-facing `Buffer` implementation in `Okojo.Node`
- internally it may reuse existing Okojo binary/typed-array primitives where possible
- keep the Node API surface in `Okojo.Node`

### `console`

Use host-backed console methods rather than hardcoded core globals.

## 5. Node Scheduling

Node is not the same as browser scheduling.

Important host queues:

- ECMAScript promise jobs/microtasks
- `process.nextTick(...)`
- timers
- immediates
- I/O callbacks later

### Immediate Design Rule

Do not reuse the browser-style queue map blindly.

`Okojo.Node` should define a Node host profile on top of `Okojo.Hosting` multi-queue support.

Recommended queue keys:

- `next_tick`
- `microtask` (engine-owned semantics, but visible in runtime profile docs)
- `timers`
- `immediates`
- `default`

### Required Ordering Work

This plan needs a focused note later on exact Node ordering between:

- `process.nextTick`
- promise jobs
- timers
- immediates

That should be treated as a dedicated compliance slice, not improvised during initial API work.

## 6. Built-In Node Modules

Do not attempt all built-ins first.

### Phase 1 Built-Ins

Recommended early set:

- `node:path`
- `node:url`
- `node:process`
- `node:module`
- `node:timers`
- `node:timers/promises`
- `node:buffer`
- `node:console`

### Phase 2 Built-Ins

Add based on real usage pressure:

- `node:fs`
- `node:fs/promises`
- `node:events`
- `node:util`
- `node:stream`
- `node:os`

### Implementation Rule

These should be registered in a built-in module registry in `Okojo.Node`, not hardwired into core `Okojo`.

## 7. Runtime Construction API

Use builder composition, not raw constructor growth.

Recommended high-level shape:

```csharp
using var runtime = JsRuntime.CreateBuilder()
    .UseHosting(hosting => hosting.UseThreadPoolDefaults())
    .UseNode(node => node.UseNodeRuntime())
    .Build();
```

Possible advanced shape:

```csharp
using var runtime = JsRuntime.CreateBuilder()
    .UseHost(host =>
    {
        host.UseModuleSourceLoader(...);
        host.UseWorkerScriptSourceLoader(...);
    })
    .UseNode(node =>
    {
        node.UseFileSystem(...);
        node.UseProcessInfo(...);
        node.UseBuiltInModules(...);
        node.UseCommonJs();
    })
    .Build();
```

### Recommended New Builder Layer

Add a Node-specific builder extension in `Okojo.Node`, for example:

- `UseNode(...)`
- `UseNodeRuntime()`
- `UseNodeCommonJs()`
- `UseNodeBuiltIns()`

## 8. Caching and Module Identity

For practical CommonJS support, the runtime needs:

- a CommonJS module cache
- module identity by resolved filename / URL
- circular CommonJS loading behavior

Reference:

- [Modules: CommonJS modules](https://nodejs.org/api/modules.html)
  - `Caching`
  - `Cycles`

This is host/runtime policy and belongs in `Okojo.Node`.

## Recommended Implementation Phases

## Phase 1: Node Runtime Design and Assembly Setup

Deliverables:

- create `docs/OKOJO_NODE_RUNTIME_PLAN.md`
- create `src/Okojo.Node`
- add clear dependency rule:
  - `Okojo.Node` depends on `Okojo` and `Okojo.Hosting`

## Phase 2: CommonJS Core Runtime

Deliverables:

- CommonJS wrapper execution
- `module` / `exports` / `require`
- CommonJS module cache
- `__filename` / `__dirname`
- file-based CommonJS loading

Tests:

- focused Okojo tests for:
  - `exports.foo = ...`
  - `module.exports = ...`
  - cyclic CommonJS load
  - `require.cache`
  - `__dirname` / `__filename`

## Phase 3: Node Resolution and Packages

Deliverables:

- package `"type"`
- package `"main"`
- practical `"exports"` subset
- `node_modules` lookup
- `node:` built-in resolution

Tests:

- package boundary tests
- `.js/.cjs/.mjs` behavior tests
- `node_modules` resolution tests

Focused implementation note for the current first resolver slice:

- [OKOJO_NODE_COMMONJS_RESOLUTION_NOTE.md](docs\OKOJO_NODE_COMMONJS_RESOLUTION_NOTE.md)
- [OKOJO_NODE_MODULE_SYSTEM_NOTE.md](docs\OKOJO_NODE_MODULE_SYSTEM_NOTE.md)
- [OKOJO_NODE_PACKAGE_EXPORTS_NOTE.md](docs\OKOJO_NODE_PACKAGE_EXPORTS_NOTE.md)
- [OKOJO_NODE_BUILTINS_NOTE.md](docs\OKOJO_NODE_BUILTINS_NOTE.md)

## Phase 4: Node Globals and Built-Ins

Deliverables:

- `process`
- `Buffer`
- `console`
- Node timers
- early `node:*` built-ins

Tests:

- observable API tests
- small real-world integration samples

## Phase 5: Node Async Ordering

Deliverables:

- `process.nextTick(...)`
- `setImmediate(...)`
- Node queue ordering note
- event-loop profile docs for Node host behavior

Tests:

- `nextTick` vs promise jobs
- timer vs immediate ordering
- nested scheduling order

## Phase 6: Sandbox and Tooling

Deliverables:

- new sandbox project demonstrating Node-like runtime usage
- sample CommonJS app
- sample mixed ESM/CommonJS app
- bytecode/disasm visibility for CommonJS wrapper code if useful

## API and Boundary Recommendations

## What Should Be Public in Core `Okojo`

Keep public:

- low-level host seams
- runtime/realm/module execution APIs
- module source loader seam

Do not add as core public APIs:

- `require(...)`
- CommonJS `module` object types
- Node built-in module registry
- `process` host APIs

## What Should Be Public in `Okojo.Node`

This is the right place for:

- Node runtime builder extensions
- CommonJS loader/resolver
- Node built-in modules
- Node process/buffer/console/timer setup

## Main Risks

1. Mixing Node and browser policy in the same assembly or builder path.
2. Putting CommonJS/runtime policy into core `Okojo`.
3. Underestimating Node resolution/package complexity.
4. Getting async ordering mostly-right but observably wrong.
5. Exposing too many unstable internal Node runtime types as default public API.

## Recommended Near-Term Next Slice

If this plan is approved, the best first implementation slice is:

1. create `src/Okojo.Node`
2. implement CommonJS wrapper execution
3. implement per-runtime CommonJS module cache
4. implement minimal `require(...)` + `module.exports`
5. add a Node sandbox project
6. defer package `"exports"` complexity until the basic runtime is green

That is the smallest slice that creates real Node-style execution value without overcommitting early.
