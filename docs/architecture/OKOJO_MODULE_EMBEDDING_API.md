# Okojo Module Embedding API

## Purpose

This note explains the intended embedding-facing module API in Okojo.

It is written for readers who know JavaScript modules and C#, but do not know Okojo internals.

## Current Recommended Public Shape

For normal embedding code, use:

- `JsRuntime.LoadModule(...)`
- `JsRealm.LoadModule(...)`

Both return `JsModuleLoadResult`.

That is the primary public module-loading API.

## Why This Shape

JavaScript modules have two different completion modes:

- synchronous completion
- asynchronous completion because of top-level `await`

C# embedding code should not need two unrelated public entry points for those cases.

Instead, Okojo now uses one sync-first API:

- call `LoadModule(...)`
- inspect exports immediately if the module is already complete
- optionally await completion from C# if the module is still pending

This matches how JavaScript behaves more closely:

- module loading starts now
- completion may already be finished
- or completion may still be represented by a promise

## `JsModuleLoadResult`

`JsModuleLoadResult` is the embedding-facing module handle.

It exposes:

- `ResolvedId`
- `Realm`
- `Namespace`
- `Object`
- `IsCompleted`
- `CompletionValue`
- `GetExport(...)`
- `TryGetExport(...)`
- `GetFunctionExport(...)`
- `CallExport(...)`
- `ToTask(...)`

## Completion Model

### Sync module

For a synchronously completed module:

- `IsCompleted == true`
- `CompletionValue` is the namespace object
- exports are ready immediately

### Top-level-`await` module

For an asynchronously completing module:

- `IsCompleted == false`
- `CompletionValue` is a promise that resolves to the namespace object
- C# may await `ToTask()` or `Realm.ToTask(result.CompletionValue)`

This means the C# host decides whether to:

- inspect the handle only
- await completion immediately
- pass the result onward and await later

## Recommended Usage

### Synchronous module

```csharp
var module = runtime.LoadModule("/mods/value.js");
var value = module.GetExport("value");
```

### Top-level-`await` module

```csharp
var module = runtime.LoadModule("/mods/async.js");

if (!module.IsCompleted)
{
    await module.ToTask();
}

var value = module.GetExport("value");
```

### If the caller wants raw promise-style control

```csharp
var module = runtime.LoadModule("/mods/async.js");
var settled = await runtime.MainRealm.ToTask(module.CompletionValue);
```

## Public vs Lower-Level API

Recommended layering:

- `JsRuntime.LoadModule(...)`
- `JsRealm.LoadModule(...)`

for normal embedding code

and:

- `JsAgent.Modules.Resolve(...)`
- `JsAgent.Modules.Link(...)`
- `JsAgent.Modules.Evaluate(...)`
- `JsAgent.Modules.EvaluateNamespace(...)`
- `JsAgent.Modules.TryGetCachedNamespace(...)`
- `JsAgent.Modules.GetState(...)`

for advanced control, diagnostics, and runtime-internal workflows

The lower-level module API is still useful, but it should not be the first API embedders discover.

## What This API Avoids

This shape intentionally avoids forcing embedders to choose between:

- a raw `JsValue`
- a namespace-only helper
- a separate async module-loading method

Those distinctions matter internally, but they are not a good default public boundary.

## Remaining Cleanup

The main follow-up work is:

1. document `LoadModule(...)` as the primary public entry point in more examples
2. keep lower-level module lifecycle APIs clearly marked as advanced
3. keep `JsAgent.Modules` focused on explicit lifecycle/cache/diagnostic operations rather than embedding convenience

## Relationship To Host/Event Loop Work

This module API fits the newer host/event-loop direction well.

Reason:

- the module load call is synchronous from C#
- async completion is represented as a JS promise
- the host can decide when and whether to await that promise

That keeps module completion aligned with JavaScript promise semantics instead of inventing a separate host-only async model.
