# Host API Feedback

This note records API friction found while building the browser-style and server-style sandbox hosts.

## Main issues

### 1. No first-class host presets above raw builder composition

Current composition works, but it is still fairly mechanical:

- browser-style host:
    - `UseThreadPoolHosting()`
    - `UseModuleSourceLoader(...)`
    - `UseBrowserGlobals(...)`
    - `UseWebWorkers()`
- server-style host:
    - `UseThreadPoolHosting()`
    - `UseModuleSourceLoader(...)`
    - `UseFetch(...)`
    - `ConfigureOptions(... AddRealmApiModule(...))`

Suggested improvement:

- add higher-level presets in `Okojo.Hosting` / `Okojo.WebPlatform`, for example:
    - `UseBrowserHost(...)`
    - `UseServerHost(...)`

Those should still be thin composition helpers, but they would make intended host profiles much easier to discover.

### 2. Simple host globals are better now, but structured host setup still needs a higher-level surface

The new `UseRealmSetup(...)` / `UseGlobal(...)` helpers are a good improvement.
The server sample no longer needs a custom `IRealmApiModule` just to install one host object.

The remaining gap is for larger host setup that still wants reusable composition without dropping back to module-like
plumbing.

Suggested improvement:

- keep `UseRealmSetup(...)` / `UseGlobal(...)`
- consider one more reusable host composition surface for grouped host features

### 3. Scheduler binding is better now, but still implementation-first

`IHostTaskScheduler` plus `HostTaskTarget` is a workable boundary.
It is much better than passing `JsAgent` directly.

But embedders still need to understand:

- `HostTaskTarget`
- `IHostAgentScheduler`
- `IHostDelayScheduler` when they want manual timer/event-loop control

Suggested improvement:

- provide a default helper base or adapter in `Okojo.Hosting`, for example:
    - `HostTaskSchedulerBase`
    - `TimeProviderDelayScheduler`
    - `ManualDelayScheduler`
    - `ThreadAffinityHostLoop`

That would reduce boilerplate for custom hosts.

### 4. Pumping is available, but host-runner ergonomics are still thin

The sample still needed custom `PumpUntil(...)` loops around:

- `ManualDelayScheduler.PumpReady()` + `realm.PumpJobs()`
- or `ThreadAffinityHostLoop.RunOneTurn(...)`

Suggested improvement:

- add more host-runner helpers in `Okojo.Hosting`, for example:
    - `HostPump.RunUntil(Func<bool> condition, TimeSpan timeout)`
    - `HostPump.RunUntilIdleOrTimeout(...)`

Those should stay helper APIs, not part of the scheduler contract.

Status:

- `HostTurnRunner` now covers the common "host turn + ECMAScript checkpoint" path
- queue-order is now passed directly as explicit queue arrays/spans on the hot path

### 5. Browser-style worker setup is split across two different concepts

Today the browser worker path is:

- `UseWebWorkers()`
- internally uses hosting worker globals and a worker host

This is correct architecturally, but reviewers need to understand both hosting and web layers to see the whole picture.

Suggested improvement:

- document `UseWebWorkers()` as a full browser worker preset
- consider adding one clearer entry point name if the current split remains confusing

### 6. There is no obvious “server runtime” preset yet

The server-style sample intentionally used:

- `fetch`
- modules
- custom host globals
- no `window`
- no `Worker`

That feels close to Node/Deno/Bun-style usage, but there is no named preset for it.

Suggested improvement:

- add a small `UseServerRuntime(...)` or `UseServerGlobals(...)` composition helper
- keep it explicit about which globals it installs

## Secondary issues

### 7. Module loader setup is still more manual than ideal for in-memory/demo hosts

The in-memory module loader in the sandbox is simple, but embedders will likely repeat this pattern.

Suggested improvement:

- add a tiny in-memory loader helper in `Okojo.Hosting` test/sandbox support or samples

### 8. `queueMicrotask` installation remains web-facing even though behavior is core-like

This is already known in the design notes.
The sandbox reinforced it:

- browser host wants `queueMicrotask`
- server host may want it too

Suggested improvement:

- decide whether there should be a smaller non-browser globals preset that includes `queueMicrotask`

### 9. Module loading should collapse to one primary public API

The server sample now uses `LoadModule(...)`, which returns a `JsModuleLoadResult`.

- sync modules expose the namespace immediately
- async modules expose a completion value that C# can await
- the result object gives a stable export-reading facade

That is the right embedding shape. The remaining problem is naming overlap in lower-level APIs.

Suggested direction:

- keep `LoadModule(...) -> JsModuleLoadResult` as the only primary embedding entry point on `JsRuntime` / `JsRealm`
- keep raw evaluation on lower-level module APIs only
- document that `CompletionValue` is immediate for sync modules and promise-backed for TLA modules

## Recommended next API slice

1. Keep the new scheduler boundary as-is for now.
2. Add one higher-level browser host preset.
3. Add one higher-level server/runtime preset.
4. Add a lightweight host-global installation helper.
5. Add `HostPump` convenience helpers for `RunUntil(...)` style usage.

## New issues from the gateway-style server sample

### 10. Server-side `fetch` composition works, but request-building is still too manual

The gateway sample can call upstream services correctly, but embedder-side code still has to hand-assemble too much
low-level behavior around:

- URLs
- JSON parsing
- status validation
- request/response conventions

Suggested improvement:

- keep `fetch` itself low-level
- add sample-quality helpers or a small optional host utility layer for common JSON request patterns

### 11. Multi-service sandboxing is possible, but there is no first-class in-memory service harness

The sandbox currently fakes upstream services through `HttpMessageHandler`.
That is workable, but embedders and tests will likely rebuild the same pattern.

Suggested improvement:

- add a tiny in-memory HTTP/service harness for samples and tests
- keep it outside core `Okojo`

### 12. Runtime presets still do not advertise “gateway/server service” usage clearly enough

The server sample now covers:

- modules
- top-level `await`
- host globals
- `fetch`
- service aggregation

That is enough to justify a clearer runtime profile story for service-side embedding.

Suggested improvement:

- document a server/gateway profile explicitly in `Okojo.WebPlatform`
- show one canonical sample around `LoadModule(...)` + `fetch` + host globals

### 13. Host queue debugging needed a first-class surface

Multi-queue hosting is much easier to debug when the host can inspect:

- pending tasks per queue
- pending delayed work count
- next delayed due time

Status:

- `ManualHostEventLoop` and `ThreadAffinityHostLoop` now expose `IHostEventLoopDiagnostics`
- `HostEventLoopSnapshot` / `HostTaskQueueSnapshot` give a simple diagnostic view

Remaining improvement:

- surface these snapshots in sandbox/demo output when troubleshooting async ordering

### 14. Turn-level observation is useful and should stay low-level

Queue snapshots help with static state, but async ordering bugs often need turn-by-turn visibility:

- which host queue supplied the task
- whether delayed work became ready this turn
- whether a microtask checkpoint happened
- whether pending jobs remained after the turn

Status:

- `HostTurnRunner` now supports `IHostTurnObserver`
- the observer sees `BeforeTurn`, `AfterHostTask`, `BeforeMicrotaskCheckpoint`, `AfterMicrotaskCheckpoint`, and
  `AfterTurn`

Design note:

- this is the right kind of utility surface
- it preserves low-level control and observability instead of hiding the host loop behind a black box
