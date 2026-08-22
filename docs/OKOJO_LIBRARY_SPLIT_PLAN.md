# Okojo Library Split Plan

## Status and authority

This is the canonical document for Okojo assembly, package, namespace, and API ownership.

`OKOJO_BROWSER_COMPATIBILITY_PLAN.md` remains the product/compatibility roadmap. Focused feature notes may describe implementation details, but must not define a conflicting package boundary.

The split is intentionally not backward compatible. The repository is still pre-release, so the old `Okojo` package and namespace will not be kept as a facade unless a real published-consumer requirement appears.

## Goals

- keep engine-independent JavaScript-compatible algorithms usable without the engine
- make the ECMAScript engine independent of embedding, I/O, worker, and event-loop policy
- provide one small embedding layer above the engine
- let browser and other host profiles completely own task selection, waiting, and microtask-checkpoint timing
- preserve applicable ECMAScript and host-profile semantics throughout the migration, not only after the final split
- keep host profiles and optional tooling outside both core assemblies
- preserve behavior while moving code; namespace cleanup is a separate mechanical step

## Naming decisions

| Name | Meaning |
| --- | --- |
| `Okojo.JavaScript` | ECMAScript engine package, assembly, and root namespace |
| `Okojo.JavaScript.Embedding` | Embedding/container package and namespace (`JsRuntime`, builder, options) |
| `Okojo.Hosting` | Optional .NET host implementations and event-loop helpers |
| `Okojo.Diagnostics` | Optional engine diagnostics and rendering |
| `Okojo.Reflection` | Optional CLR reflection binding |
| `Okojo.WebPlatform`, `Okojo.Browser`, `Okojo.Node` | Host profiles above the embedding layer |

These names are deliberately not used:

- `Okojo.Core`: “core” does not identify a responsibility.
- `Okojo.Engine`: the `Okojo.JavaScript` package already is the engine.
- `Okojo.Runtime`: it is ambiguous with the current VM/execution namespace.
- `Okojo.JavaScript.Engine`: redundant and longer than `Okojo.JavaScript`.

The current engine implementation namespace `Okojo.Runtime` must not be mechanically renamed to `Okojo.JavaScript.Embedding`; that name belongs to the embedding package. Engine execution types move to `Okojo.JavaScript.Execution`.

Public types keep domain names such as `JsValue`, `JsRealm`, `JsAgent`, and `JsRuntime`. Do not add `Okojo` or `Engine` prefixes merely to repeat package identity.

## Target dependency graph

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

All arrows point from a dependency to a consumer. Cycles are forbidden.

`Okojo.Diagnostics` depends on the engine, not the embedding layer. Profile and integration packages may also reference `Okojo.JavaScript` directly when their public API mentions engine value types, but host policy must enter through `Okojo.JavaScript.Embedding`.

## Library ownership

### Engine-independent libraries

The following projects are already extracted and remain independent of `JsValue`, `JsRealm`, and the VM:

- `Okojo.Text.Unicode`: UTF-16/code-point operations, Unicode data, casing, segmentation
- `Okojo.Numerics`: ECMAScript numeric formatting, exact arithmetic helpers, `BigInt`
- `Okojo.Text.RegularExpressions`: the single ECMAScript regular-expression engine
- `Okojo.Globalization`: portable ECMA-402 data and algorithms

Do not create more leaf libraries unless a real non-engine consumer exists. In particular, parser, compiler, bytecode, and VM stay together.

### `Okojo.JavaScript`

Owns ECMAScript semantics:

- values and object model
- parser, compiler, bytecode, and VM
- realms, agents, execution contexts, generators, and intrinsics
- script and Promise jobs
- module records, graph, linking, and evaluation
- ordered Promise-job storage and an explicit, non-reentrant checkpoint operation
- a minimal module-loading contract required by the module graph
- generic host-call and host-object contracts required to invoke callbacks
- debugger/checkpoint primitives required while executing code

Does not own:

- `JsRuntime` or builder policy
- file/network module loaders
- host task queues, event-loop pumping, timers, or delayed scheduling
- worker lifecycle or message-serialization implementation and policy
- CLR reflection binding
- source-map loading/registration policy
- diagnostic text rendering, REPLs, or debug servers

Recommended namespaces:

- `Okojo.JavaScript`
- `Okojo.JavaScript.Values`
- `Okojo.JavaScript.Objects`
- `Okojo.JavaScript.Parsing`
- `Okojo.JavaScript.Compiler`
- `Okojo.JavaScript.Bytecode`
- `Okojo.JavaScript.Execution`
- `Okojo.JavaScript.Intrinsics`

Do not split compiler or bytecode into new production assemblies. They share contracts with the VM, and separating them would add a lower “core” assembly or a dependency cycle without providing a current consumer benefit.

### `Okojo.JavaScript.Embedding`

Owns embedding and process/container concerns:

- `JsRuntime`, `JsRuntimeBuilder`, and runtime options
- engine/agent/realm creation and lifetime composition
- file module and worker-script loader implementations
- worker creation, cross-agent messaging, and message serialization
- host scheduling contracts and independently callable operations used to connect an event loop
- source-map registry and embedding-side debugger glue
- explicit global/module installation composition

The embedding layer may implement engine-owned contracts, but the engine must never reference the embedding assembly.

The embedding layer may offer a default convenience pump, but it must not define the only execution path. A browser must be able to choose a runnable task, run it, request the required microtask checkpoint, perform rendering work, and decide whether or how to wait without hidden pumping.

### Optional layers

- `Okojo.Hosting`: concrete schedulers, event loops, pumps, turn runners, and default worker infrastructure
- `Okojo.Diagnostics`: disassembly, formatting, and inspection over engine types
- `Okojo.Reflection`: reflection-based CLR binding; depends on the embedding layer plus the engine contracts it implements
- `Okojo.DotNet.Modules`: .NET module/profile integration above `Okojo.Reflection`
- `Okojo.WebAssembly`: WebAssembly integration above the embedding layer
- `Okojo.WebPlatform`, `Okojo.Browser`, `Okojo.Node`: host APIs and profile policy

## Boundary decisions from the current code

### `JsAgent` and `JsRealm` stay whole in the engine

Both are partial classes. A partial type cannot span assemblies, so there is no “engine half” and “runtime half” of either type.

Before the physical split:

- remove host task queues and `HostTaskScheduler` ownership from `JsAgent`
- move worker messaging behavior out of `JsRealm`
- retain small engine entry points for Promise jobs, callbacks, and host-defined delivery
- let the runtime own agent creation and attach host behavior by composition

The engine owns only ECMAScript job state. `HostJobs`, `HostPriorityJobs`, `PumpJobs` policy, queue keys, and host wait handles belong above it.

### Event-loop ownership and specification gate

ECMAScript and host event loops are separate contracts:

- [ECMA-262 `HostEnqueuePromiseJob`](https://tc39.es/ecma262/multipage/executable-code-and-execution-contexts.html#sec-hostenqueuepromisejob) is host-defined, but Promise jobs must execute in the order in which they were enqueued.
- The [HTML event loop](https://html.spec.whatwg.org/multipage/webappapis.html#event-loops) selects a runnable task and then performs a microtask checkpoint.
- An HTML [microtask checkpoint](https://html.spec.whatwg.org/multipage/webappapis.html#perform-a-microtask-checkpoint) is non-reentrant and drains the queue, including microtasks enqueued while the checkpoint runs.
- Node priority queues such as `nextTick` are Node profile policy and must not be imposed on browser hosts.

The target API separates mechanism from policy:

- the engine stores and executes Promise jobs without selecting host tasks
- the runtime exposes task enqueueing, one-task execution, Promise checkpoint, pending-state, and wake-up primitives without prescribing a loop
- `Okojo.Hosting` composes those primitives into optional default pumps
- `Okojo.Browser` owns task-source selection, timers, networking, worker delivery, rendering opportunities, checkpoint timing, waiting, and fairness

No engine evaluation call may force a browser to run unrelated host tasks. Convenience APIs may pump only when explicitly requested and must be implemented through the same low-level operations available to custom hosts.

This is a migration gate, not a final cleanup item. Before every behavior-bearing boundary change:

1. preserve Promise FIFO, recursive Promise-job draining, non-reentrant checkpoints, and run-to-completion
2. preserve the active host profile's observable task order and checkpoint points
3. attach host queues before invoking realm or agent initialization callbacks
4. add focused regressions for the changed ordering or lifecycle boundary
5. require warning-free builds and a fully passing applicable test suite; do not carry a known failure forward

### Replace the broad runtime-host dependency

`IJsRuntimeHost` was transitional and has been removed. It exposed `JsRuntimeOptions`, source maps, worker loading, CLR state, and agent creation to engine types.

Before moving projects, continue reducing the direct inputs left on `JsAgent` to the smallest engine-owned inputs actually required by each subsystem. Prefer constructor inputs and narrow callbacks. Add a cohesive public host interface only where an external runtime genuinely must implement one; do not replace one broad interface with several speculative interfaces.

### Module loading is split by contract and implementation

The engine module graph currently consumes `IModuleSourceLoader`, so the minimal resolve/load contract stays in `Okojo.JavaScript` to avoid a cycle. File, network, Node, and worker-script implementations live in the embedding or profile packages.

Module parsing, linking, live bindings, and evaluation stay entirely in the engine.

### Host interop is split by role, not directory

Do not move `Runtime/Interop/*` wholesale.

- generic callback ABI and host-object contracts (`CallInfo`, host functions, required descriptors) stay in the engine
- reflection/type discovery and CLR conversion implementation move to `Okojo.Reflection`
- runtime composition exposes opt-in registration without making reflection a required dependency

### Worker messaging is split by mechanism and profile API

`IHostMessageSerializer` remains an engine-owned host contract. It is cohesive, and its operations cross the `JsRealm`/`JsValue` boundary. The default implementation belongs to `Okojo.JavaScript.Embedding`.

Move worker lifecycle, queue selection, per-realm worker state, dispatch wiring, `IWorkerHost`, `WorkerHostBinding`, `DefaultWorkerHost`, and `WorkerHandleFactory` to runtime ownership. Use one concrete `WorkerMessaging` component rather than adding another interface.

Keep `JsAgent.PostMessage` and `MessageReceived` for this slice as the narrow delivery mechanism implemented next to the agent's queues. Runtime owns worker policy, serialization choices, queue choice, and JS-facing dispatch. Revisit the delivery API only if the physical split demonstrates a concrete missing contract.

Cross-realm value bridging remains an engine operation and must be separated from the current worker partial. Web-facing `Worker`, `postMessage`, `onmessage`, `onmessageerror`, and event objects belong to `Okojo.WebPlatform`. The non-standard `createWorker` helper may remain an opt-in `Okojo.Hosting` API, but enabling the browser profile must not install it.

### Friend assemblies are temporary migration aids

The current broad `InternalsVisibleTo` list is evidence of missing boundaries. The target is:

- engine internals visible only to engine tests and explicitly experimental compiler/tooling projects
- runtime internals visible only to runtime tests
- host/profile projects use supported contracts

One temporary engine-to-runtime friend relationship is acceptable during the move, but it must not become the final API.

## Migration plan

### Phase 1 — independent libraries: complete

- `Okojo.Text.Unicode`
- `Okojo.Numerics`
- `Okojo.Text.RegularExpressions`
- `Okojo.Globalization`

The standalone projects exist and the monolith references them. Update stale friend-assembly names when the engine assembly is renamed.

### Phase 2 — make the boundary real inside the current assembly: complete

Behavior must remain unchanged while these couplings are removed:

1. detach `JsAgent` from concrete `JsRuntime` and `JsRuntimeOptions`
2. move host queues/scheduler pumping out of `JsAgent` while keeping Promise checkpoints independently host-controlled
3. move worker messaging state, lifecycle, and the default serializer implementation out of `JsRealm`; retain the `IHostMessageSerializer` contract
4. split generic host callback contracts from reflection implementation
5. narrow `InternalsVisibleTo` consumers

Progress: the broad `IJsRuntimeHost` seam has been removed. `JsAgent` now receives
the concrete module, timing, interop, wait, scheduler, identity, and
`IHostMessageSerializer` inputs it actually uses; the same engine-owned serializer
contract is also used by embedding-owned `WorkerMessaging` for raw worker transport.
Worker policy and serialization are composed through that concrete component;
projection modules are created per runtime from reusable option factories, while
the shared registration sequence preserves builder call order.
`JsRealm` exposes engine-facing values through its agent and keeps only the
cross-realm bridge. `HostJobQueue` owns host
and host-priority queues, scheduler delivery, and the default convenience-pump
policy. Agent construction, host-queue attachment, and user initialization are
separate stages. Promise checkpoints are FIFO and non-reentrant, while
`RunOneHostJob()` and `RunPromiseJobs()` remain independently callable for a
browser-controlled loop. `UseWebWorkers()` no longer installs the Hosting-only
`createWorker` helper; `UseWorkerGlobals()` is the explicit opt-in.

Worker state/policy is now removed from `JsRealm`, and `JsAgent` no longer stores
the worker host or worker-message queue key; its serializer input remains for
direct `PostMessage` delivery. WebPlatform/Hosting modules project raw delivery
into their own globals, handles, event objects, and handlers. The behavior-bearing
boundary is complete; the concrete embedding move is recorded in Phase 3.

Keep focused module, agent, Promise, timer, worker, interop, and execution-check tests green after each step.

### Phase 3 — physical project split: active

Create only two projects:

```text
src/Okojo.JavaScript/Okojo.JavaScript.csproj
src/Okojo.JavaScript.Embedding/Okojo.JavaScript.Embedding.csproj
```

Move files with history. Initially preserve namespaces so compiler errors identify assembly-boundary violations separately from namespace-renaming errors.

Project references:

- `Okojo.JavaScript` → Unicode, Numerics, RegularExpressions, Globalization
- `Okojo.JavaScript.Embedding` → `Okojo.JavaScript`

Each project must build independently before continuing.

Progress: the two projects exist and build independently. The old
`Okojo.csproj` and its solution entry are gone. The physical move is complete;
the namespace migration is recorded in Phase 4. Stale engine friends for
`Okojo.Browser` and `Test262Runner` were removed; friends still required by
existing internal host/profile paths remain for a later supported-contract
cleanup.

### Phase 4 — namespace rename: complete

Perform the namespace migration as one mechanical pass after both assemblies build:

- `Okojo` → `Okojo.JavaScript`
- `Okojo.Values` → `Okojo.JavaScript.Values`
- `Okojo.Objects` → `Okojo.JavaScript.Objects`
- `Okojo.Parsing` → `Okojo.JavaScript.Parsing`
- `Okojo.Compiler` → `Okojo.JavaScript.Compiler`
- `Okojo.Bytecode` → `Okojo.JavaScript.Bytecode`
- `Okojo.RegExp` → `Okojo.JavaScript.RegExp`
- `Okojo.SourceMaps` → `Okojo.JavaScript.SourceMaps`
- `Okojo.Internals` → `Okojo.JavaScript.Internals`
- engine-owned `Okojo.Runtime` → `Okojo.JavaScript.Execution`
- engine-owned `Okojo.Runtime.Interop` → `Okojo.JavaScript.Execution.Interop`
- embedding types → `Okojo.JavaScript.Embedding`

Update global usings, generated-source inputs, XML documentation references,
and consumer code in the same mechanical pass. Assembly-name friend entries
remain unchanged; friend-assembly cleanup is the next separate slice.

Progress: all production consumers compile with the new namespaces, the engine
`Runtime` directory is now `Execution`, and the embedding `Runtime` directory
is removed. `Okojo.SourceMaps` remains physically owned by the engine in this
slice. The supported-contract and friend-assembly cleanup remains deferred.

### Phase 5 — consumers, tests, and packages

- `Okojo.Diagnostics` and `Okojo.Compiler.Experimental` reference the engine
- `Okojo.Hosting` references the runtime
- Reflection, WebAssembly, WebPlatform, Browser, and Node reference the smallest required layer
- Test262Runner and integration tests reference the embedding/profile packages they execute

### Phase 5A — production friend audit: complete

Each production candidate was tested independently by removing one friend
entry and building its direct consumer in Release mode. The result is an
inventory, not a public-API redesign.

| Friend assembly | Granting assembly | Result | Concrete dependency or evidence |
| --- | --- | --- | --- |
| `Okojo.Browser` | `Okojo.JavaScript.Embedding` | removed as stale | Direct `Okojo.Browser` build succeeds without embedding internals. |
| `Okojo.Node` | `Okojo.JavaScript.Embedding` | removed as stale | Direct `Okojo.Node` build succeeds without embedding internals. |
| `Okojo.WebAssembly` | `Okojo.JavaScript.Embedding` | removed as stale | Direct `Okojo.WebAssembly` build succeeds without embedding internals. |
| `Okojo.WebAssembly.Wasmtime` | `Okojo.JavaScript.Embedding` | removed as stale | Direct `Okojo.WebAssembly.Wasmtime` build succeeds without embedding internals. |
| `Okojo.Hosting` | `Okojo.JavaScript.Embedding` | retained | `WorkerMessaging` in `WorkerGlobalsApiModule.cs`; later cleanup should expose a supported worker-projection composition path. |
| `Okojo.Reflection` | `Okojo.JavaScript.Embedding` | retained | `EnableClrAccess` on `JsRuntimeBuilder`, `JsRuntimeOptions`, and `JsRuntimeCoreOptions` in `ClrAccessExtensions.cs`; later cleanup should define the smallest supported CLR-access configuration contract. |
| `Okojo.WebPlatform` | `Okojo.JavaScript.Embedding` | retained | `WorkerMessaging` in `WebWorkerApiModule.cs`; later cleanup should use the supported worker-projection composition path. |
| `Okojo.JavaScript.Embedding` | `Okojo.JavaScript` | retained | `ISharedWaiterControllerFactory`, `JsArrayBufferObject.ISharedWaiterController`, `JsArrayBufferObject.SharedWaiter`, and `IClrAccessProvider` are consumed by runtime options and default wait/CLR composition. |
| `Okojo.Hosting` | `Okojo.JavaScript` | retained | `JsRealm.GetCurrentModuleResolvedIdOrNull`, `JsRealm.BridgeFromOtherRealm`, `JsRealm.InvokeFunction`, `JsAgent.EvaluateModule`, `JsAgent.PendingJobCount`, `ITimerFactory`, `ITimerFactory.CreateJsTimer`, `JsGlobalObject.TryGetPropertyAtom`, and `JsPlainObject.TryGetPropertyAtom` are used by worker and host-loop implementations. |
| `Okojo.Reflection` | `Okojo.JavaScript` | retained | CLR wrappers consume `IClrAccessProvider`, `HostTypeDescriptor`, `IClrTypedNullReference`, `IClrNamespaceReference`, `IClrByRefPlaceholder`, `IClrTypeFunctionData`, `HostRealmLayoutInfo`, `JsObject.TryGetPropertyAtomWithReceiverValue`, `JsObject.TryGetOwnNamedPropertyDescriptorAtom`, `JsObject.CollectOwnNamedPropertyAtoms`, and `JsObject.SetPropertyAtomWithReceiver`. |
| `Okojo.Node` | `Okojo.JavaScript` | retained | Node built-ins use internal conversion/invocation, module/job, promise, prototype, typed-array, compiler, and property APIs, including `JsRealm.ToNumber`, `JsRealm.ToJsStringSlowPath`, `JsAgent.EvaluateModule`, `JsAgent.EnqueueHostPriorityJob`, `JsPromiseObject.State`, `TypedArrayElementKind`, and `JsObject.Prototype`. |
| `Okojo.WebPlatform` | `Okojo.JavaScript` | retained | Web APIs use `JsRealm.WrapTaskOnHostQueue`, `JsRealm.InvokeFunction`, `JsObject.DefineDataPropertyAtom`, `JsObject.DefineAccessorPropertyAtom`, `JsGlobalObject.TryGetPropertyAtom`, `JsPlainObject.TryGetPropertyAtom`, `JsHostFunction.InitializePrototypeProperty`, and `JsRealm.GetCurrentModuleResolvedIdOrNull`. |
| `Okojo.WebAssembly` | `Okojo.JavaScript` | retained | `WebAssemblyInstaller.cs` uses `JsRealm.PromiseResolveValue`, `JsRealm.PromiseRejectByConstructor`, `JsRealm.CreateErrorObjectFromException`, `JsRealm.InvokeFunction`, `JsRealm.ToIntegerOrInfinity`, `JsRealm.ToUint32`, `JsRealm.ToNumber`, `JsRealm.ToJsStringSlowPath`, `JsArrayBufferObject.GetByte`, `Intrinsics.CreateNativeErrorConstructor`, `Intrinsics.PromiseConstructor`, `Intrinsics.ErrorPrototype`, `Intrinsics.ErrorConstructor`, `JsRealm.ObjectPrototype`, `JsHostFunction.InitializePrototypeProperty`, and `JsObject.Prototype`. |
| `Okojo.WebAssembly.Wasmtime` | `Okojo.JavaScript` | retained | `WasmtimeMemoryWrapper.cs` uses `JsRealm.SharedArrayBufferPrototype` and `JsRealm.ArrayBufferPrototype`. |

The existing test/tooling friends were intentionally not audited in this
slice. In particular, `Test262Runner` still needs the engine shared-waiter
and timer internals plus embedding `UseSharedWaiterControllerFactory`.

### Phase 5B — worker projection composition: complete

`WorkerMessaging` remains a concrete runtime-owned component with an internal
constructor. `JsRuntimeOptions.UseWorkerMessaging` is the supported composition
path; it passes the runtime-created component to Hosting and WebPlatform
projection modules. Worker-handle atom selection remains inside Embedding, and
the projections receive only worker creation, receiver registration, and value
message operations. The `Okojo.Hosting` and `Okojo.WebPlatform` friends were
removed from `Okojo.JavaScript.Embedding` after independent consumer builds.
No transport interface or compatibility facade was added.

Do not reorganize all tests before the production split compiles. Keep `tests/Okojo.Tests` as the conformance/integration loop first; create `Okojo.JavaScript.Tests` and `Okojo.JavaScript.Embedding.Tests` only while moving tests that clearly belong to each boundary.

Finally update:

- `Okojo.slnx`
- root and package READMEs
- `eng/PackageVersions.props`
- `.github/workflows/publish-packages.yml`
- examples, tools, and package workflow documentation

Delete `src/Okojo/Okojo.csproj` after all references are gone. This was completed in the first Phase 3 slice; do not retain an empty compatibility package by default.

## Verification

For each behavior-bearing boundary change:

```powershell
dotnet test tests/Okojo.Tests/Okojo.Tests.csproj
```

For repeated focused work, build first and then run filtered tests with `--no-build`; do not build and test in parallel.

High-risk areas:

- agent and Promise job ordering
- recursively queued Promise jobs and checkpoint reentrancy
- browser-selected task execution with no hidden runtime pump
- HTML task-to-microtask-checkpoint ordering
- modules and dynamic import
- worker lifecycle and messaging
- host interop
- execution limits/checkpoints
- Node and WebPlatform host loops

After the split, build the engine and runtime projects independently, then run the full conformance and profile test suites.

## Retired documents

The following documents were deleted because their active decisions are now captured here and their old package model conflicted with this plan:

- `docs/OKOJO_API_POLICY.md`
- `docs/OKOJO_CONCRETE_ARCHITECTURE.md`
- `docs/OKOJO_CORE_API_REFINEMENT_PLAN.md`
- `docs/OKOJO_COMPILER_ASSEMBLY_SPLIT.md`
- `docs/OKOJO_ECMA262_JOB_QUEUE.md`
- `docs/OKOJO_HOST_EVENT_LOOP_DESIGN.md`

Git history remains the source for historical rationale. Do not add tombstone documents for deleted plans.

## Deferred until evidence exists

- an `Okojo` compatibility facade
- separate parser/compiler/bytecode packages
- new abstractions for hypothetical embedders
- synchronized versioning for every Okojo package
