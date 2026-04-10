# Okojo Core API Refinement Plan

## Purpose

This plan turns the current API cleanup into a concrete next pass for `src/`.

It is not a compatibility-preserving plan.
If a public name or surface is wrong, remove or rename it.

This plan is based on:

- current package split (`Okojo`, `Okojo.Hosting`, `Okojo.WebPlatform`, `Okojo.Browser`, `Okojo.Node`)
- current builder outlines from `dotprobe-csharp`
- embedder feedback from `examples/OkojoArtSandbox`
- remaining `Okojo*` name scan across `src/`

## Current Shape

Current builder surfaces are close to the intended layering:

- `JsRuntimeBuilder` is the main composition surface
- `HostingBuilder` is small and understandable
- `WebPlatformBuilder` is a profile builder over core and hosting
- `BrowserBuilder` is a browser-specific profile builder
- `NodeRuntimeBuilder` is mostly a thin wrapper plus `ConfigureRuntime(...)`

The main remaining API issues are:

1. `JsRuntimeBuilder` still exposes too many flat advanced `Use*` methods.
2. core still has remaining public `Okojo*` names that should be renamed or hidden.
3. some internal helper names still leak old branding even when they are not intended as entry-point API.
4. host-global installation is still too manual for embedders building non-trivial hosts.

## Core Rules

### 1. Entry-point rule

Public entry-point types should be short, domain-first names:

- `JsRuntimeBuilder`
- `HostingBuilder`
- `WebPlatformBuilder`
- `BrowserBuilder`
- `NodeRuntimeBuilder`

Do not add new `Okojo*` entry-point names.

### 2. Prefix rule

- do not use `Okojo` as a general type prefix
- use `Js*` only for JavaScript/runtime concepts
- use domain names directly for host, browser, node, web platform, diagnostics, and tooling concepts

### 3. Layer rule

- `Okojo`: required runtime/embedding/low-level host seams
- `Okojo.Hosting`: optional default host implementations and utilities
- `Okojo.WebPlatform`: portable Web-runtime APIs usable outside browsers
- `Okojo.Browser`: browser-specific globals and browser-only host policy
- `Okojo.Node`: Node-specific runtime/profile policy

## Refinement Targets

## A. Simplify `JsRuntimeBuilder`

### Problem

Current outline:

- `UseTimeProvider(...)`
- `UseModuleSourceLoader(...)`
- `UseWorkerScriptSourceLoader(...)`
- `UseRealmSetup(...)`
- `UseGlobal(...)`
- `UseCore(...)`
- `UseAgent(...)`
- `UseHost(...)`
- `UseLowLevelHost(...)`
- plus several flat advanced host helpers

The advanced low-level host methods are useful, but the top-level builder is becoming noisy.

### Plan

Keep these as the intended visible surface:

- `UseCore(...)`
- `UseAgent(...)`
- `UseHost(...)`
- `UseLowLevelHost(...)`
- `UseRealm(...)`
- `UseRealmSetup(...)`
- `UseGlobal(...)`

Keep advanced helpers only as convenience forwarding:

- `UseBackgroundScheduler(...)`
- `UseHostTaskScheduler(...)`
- `UseMessageSerializer(...)`
- `UseWorkerHost(...)`
- `UseWorkerMessageQueue(...)`

But treat them as secondary:

- hide from IntelliSense where appropriate
- document `UseLowLevelHost(...)` as the preferred advanced seam
- avoid adding more flat advanced `Use*` methods unless there is a strong embedder need

### Follow-up

Consider adding one small helper for host globals in core, for example a low-level installer/builder for explicit global registration.
This should reduce repetitive `JsHostFunction` wiring without moving reflection or policy into core.

## B. Keep Wrapper Builders Thin

### `NodeRuntimeBuilder`

Target shape:

- Node-specific methods visible
- `ConfigureRuntime(Action<JsRuntimeBuilder>)` as the main core escape hatch
- avoid growing pass-through core API duplication

Current hidden pass-throughs are acceptable as a transition step, but new ones should not be added by default.

### `WebPlatformBuilder`

Current responsibilities are mostly right:

- portable runtime globals
- fetch
- web workers
- server runtime/host presets

Next cleanup:

- keep it profile-oriented
- avoid turning it into a second core builder
- if it grows, split module-specific install helpers rather than adding more mixed state fields

### `BrowserBuilder`

Target shape:

- browser-only globals
- browser host/profile policy
- rendering/timing policy that should not live in `WebPlatform`

Avoid moving portable web-runtime concepts back into `BrowserBuilder`.

## C. Rename Remaining Public `Okojo*` Names

These should be reviewed next for public API rename or visibility reduction.


### Rule

If these are not intended as stable embedder API, prefer reducing visibility instead of carrying the wrong public names.

## D. Rename Remaining Profile/Interop `Okojo*` Names

These are not all public, but they should still be cleaned so the codebase stops teaching the old naming model.

### Current examples from `src/`

- `OkojoDebugConsole`
- `OkojoNamedPropertyIcEntry`
- `OkojoClrUsingImport`
- `OkojoClrTypeFunctionData`
- `OkojoEcmaDateTimeParts`
- `OkojoIntlCalendarData`
- `OkojoIntlLocaleData`
- `OkojoIntlNumberingSystemData`
- `OkojoIntlTimeZoneData`
- `OkojoIntlUnitData`
- `OkojoLunisolarCalendarHelper`
- `OkojoWorkerHandleAtoms`

### Intended direction

- debug/tooling names should be domain-first, for example `DebugConsole`
- interop names should be `Clr*`, `Host*`, or `Js*` depending on ownership
- Intl helpers should use `Intl*`
- worker handle helper names should use `Worker*`

Internal names matter because they become the examples future public APIs tend to copy.

## E. Make Host Global Installation Easier

The art sandbox exposed repeated embedder friction:

- many `JsHostFunction` allocations
- repetitive `globalThis` wiring
- manual host state threading

### Plan

Add one explicit low-level helper in `Okojo` for global installation.

Requirements:

- no reflection-first design
- no attribute-heavy framework
- no browser/node policy in core
- still explicit about names, values, and host callbacks

Possible shape:

- `JsGlobalInstaller`
- or `JsRealmGlobalsBuilder`

Minimum useful operations:

- register constant/global values
- register host functions
- optionally register grouped host APIs against one state object

This should be the next real embedder-oriented API improvement after naming cleanup.

## F. Clarify Recommended Host State Pattern

Current code uses:

- closure capture
- `JsHostFunction.UserData`
- host-owned runtime state keyed off realm/runtime

That flexibility is useful, but the project needs one default recommendation.

### Preferred direction

- simple stateless APIs: closure capture is fine
- grouped mutable host state: attach one host-owned state object per realm/runtime and reuse it
- per-function custom metadata: keep `UserData` as a narrow tool, not the default state model

This should be documented as an embedder guideline in the architecture docs.

## G. Tighten Public vs Internal Surface

The cleanup should not stop at renaming.
Some currently public types likely do not belong in the long-term stable surface.

Review categories:

- parser/compiler internals
- VM helper internals
- object model implementation helpers
- bytecode-only support types

If a type is not needed by embedders, diagnostics consumers, or low-level host integration, reduce visibility.

## Execution Order

1. rename remaining public `Okojo*` API names in core-facing assemblies
2. reduce visibility for wrongly-public internals instead of preserving bad names
3. keep `JsRuntimeBuilder` focused around `UseLowLevelHost(...)` for advanced configuration
4. add a small explicit host-global installer helper in `Okojo`
5. document the canonical host-state pattern

## Immediate Concrete Next Slice

Recommended next coding slice:

1. review and rename the remaining public `Okojo*` types listed above
2. reduce visibility where those names should not be public at all
3. add a first low-level global installer helper and migrate `OkojoArtSandbox` to it

That gives a concrete embedder win instead of doing naming cleanup only for aesthetics.
