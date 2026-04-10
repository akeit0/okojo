# Okojo Host Event Loop Sandbox

This sandbox demonstrates practical Okojo embeddings on top of the new host scheduler boundary.

It shows five scenarios:

- multi-realm isolation inside one runtime
- single-thread parallel async using a manual multi-queue event loop
- thread-affine async using a blocking wake loop and timer-backed delayed work
- browser-style `fetch` + `setTimeout` + `async`/`await` on the main realm
- browser-style `requestAnimationFrame` render turns with microtask follow-up
- browser-style worker multi-agent flow using `Worker` and `postMessage`
- server-style module loading with top-level `await`, imports, host-installed globals, and `fetch`
- server-to-server gateway-style communication over `fetch`

The sandbox contains two host profiles:

- `DemoBrowserHost`
    - uses `UseBrowserGlobals(...)`
    - uses `UseWebWorkers()`
    - looks closer to a browser embedding
- `SingleThreadBrowserHost`
    - uses `ManualHostEventLoop`
    - uses `UseWebDelayScheduler(...)`
    - uses `UseWebTimerQueue(...)`
    - keeps JS execution on one explicit host-pumped thread
    - demonstrates parallel async work without background JS execution
- `ThreadAffinityBrowserHost`
    - uses `ThreadAffinityHostLoop`
    - uses explicit named host queues
    - uses blocking wakeups plus timer-backed delayed work
    - keeps JS execution thread-affine while avoiding busy polling
- `DemoServerHost`
    - uses `UseFetch(...)` without browser globals
    - installs a custom `host` global through `UseGlobal(...)`
    - loads a small module graph with top-level `await`
    - includes a gateway-style service module that calls upstream services over `fetch`
    - looks closer to a Node/Deno/Bun-style embedding where the host chooses globals explicitly

The sample hosts are intentionally small:

- `UseThreadPoolHosting()` provides the default host scheduler implementation
- `UseBrowserGlobals(...)` installs `window`, `self`, timers, `queueMicrotask`, `atob`/`btoa`, `fetch`, and
  animation-frame APIs
- `requestAnimationFrame(...)` and `cancelAnimationFrame(...)` now ride through an explicit rendering queue
- `UseWebDelayScheduler(...)` lets portable web timers run on a manual host loop instead of `.NET` timer callbacks
- `UseWebTimerQueue(...)` maps portable web timers to an explicit host task queue
- `UseFetchCompletionQueue(...)` maps fetch promise settlement onto an explicit host task queue
- `UseFetch(...)` gives a smaller server-style profile
- `UseWebWorkers()` installs browser-style worker entry points
- `ManualHostEventLoop` shows a manual single-thread multi-queue event loop
- `ThreadAffinityHostLoop` shows a thread-bound blocking host loop with wake signaling
- `HostTurnRunner` shows the recommended "one host turn, then ECMAScript checkpoint" helper
- host queue order is passed directly as an explicit queue array/span
- `IHostEventLoopDiagnostics` / `HostEventLoopSnapshot` expose queue and delayed-work state for debugging
- `DemoModuleLoader` supplies in-memory module sources
- `DemoFetchHandler` supplies deterministic HTTP responses

The code is split into multiple sample components instead of one script file:

- `BrowserThreadPoolHost.cs`
- `SingleThreadBrowserHost.cs`
- `ThreadAffinityBrowserHost.cs`
- `ServerThreadPoolHost.cs`
- `SandboxDemos.cs`
- shared helpers for modules, fetch, and host globals

Related review notes are in [API_FEEDBACK.md](.\OkojoHostEventLoopSandbox\API_FEEDBACK.md).

Run it with:

```powershell
dotnet run --project examples\OkojoHostEventLoopSandbox\OkojoHostEventLoopSandbox.csproj
```
