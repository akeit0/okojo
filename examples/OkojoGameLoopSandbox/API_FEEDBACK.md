# API Feedback

This sandbox uses Okojo as a single-thread frame-stepped scripting container. That shape is useful, but a few API gaps
show up immediately.

## Good

- `ManualHostEventLoop` fits single-thread host ownership well.
- `HostTurnRunner` gives a useful host-task-plus-job turn primitive.
- `JsAgent.SetMaxInstructions(...)`, `ResetExecutedInstructions()`, and `ResetExecutionTimeout(...)` are enough to
  express per-frame budgets without custom engine-side hooks.
- `UseWebRuntimeGlobals()` is enough to get timer and microtask behavior for non-browser app scripting.
- A small host-owned frame helper like `app.waitFrames(...)` composes naturally with normal JS `async/await`.
- A host-owned delay helper like `app.delayMs(...)` is also straightforward on top of
  `ManualHostEventLoop.ScheduleDelayed(...)`.

## Friction

### 1. Per-frame budgets are possible, but frame stepping is still manual

The sandbox still has to combine:

- call exported `update(...)`
- reset the per-frame execution budget
- run host turns
- drain microtasks/jobs
- sleep to frame cadence
- catch budget exceptions

That is acceptable for low-level control, but `Okojo.Hosting` could still provide a reusable sample-quality helper for:

- `RunFrame(...)`
- `RunFrames(...)`
- `RunUntilFrameBudgetExceeded(...)`

without hiding the low-level pieces.

### 2. Budget checks still depend on checkpoint density

This sandbox sets a small `CheckInterval` so runaway code trips quickly enough inside one frame budget.

That works, but the API story is still:

- host picks `CheckInterval`
- host decides the tradeoff between overhead and responsiveness

That should be documented more explicitly for embedders using Okojo as a sandboxed script container.

## Direction

The current architecture is good:

- `Okojo` stays the ECMAScript engine/container
- `Okojo.Hosting` owns the default runtime host patterns

The next improvement for this embedding style is not more hidden policy. It is a small, explicit, reusable frame-budget
helper in `Okojo.Hosting`.
