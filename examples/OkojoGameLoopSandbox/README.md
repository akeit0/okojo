# Okojo Game/App Sandbox

This sandbox tests Okojo from an app or game embedder point of view instead of a browser point of view.

It demonstrates:

- one JS agent on one host thread
- a 30 FPS host-owned frame loop
- file-backed user modules
- sync user update code plus async timer/microtask/frame-delay work
- host-owned C# delay and frame-delay primitives exposed through the app API
- per-frame instruction and wall-time budgets enforced by the host

Run it with:

```powershell
dotnet run --project examples\OkojoGameLoopSandbox\OkojoGameLoopSandbox.csproj
```

Main scenarios:

- `cooperative`: regular frame updates plus `async/await` over host-owned time delay and frame delay
- `runaway-sync`: user update code blows the instruction budget
- `runaway-async`: user async microtask flood blows the frame budget while draining queued work

This sandbox is intentionally not browser-shaped. It is meant to pressure-test Okojo as a flexible ECMAScript container
for app, game, and tool embedding.
