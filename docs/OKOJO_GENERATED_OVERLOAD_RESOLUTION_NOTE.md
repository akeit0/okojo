# Okojo Generated Overload Resolution Note

## Purpose

This note documents the current overload-resolution behavior for generated Okojo host bindings.

The immediate goal is to make generated overload dispatch predictable for:

- `[GenerateJsGlobals]` global exports
- `[GenerateJsObject]` generated host-object methods
- `.d.ts` emission for repeated TypeScript overload declarations

This is the current implementation note, not a long-term final design freeze.

## Scope For This Iteration

In scope now:

- group exported CLR methods by JS-visible export name
- analyze overload sets once and share the model between global and object generation
- support optional/defaulted parameters in overload matching
- support trailing `ReadOnlySpan<T>` as an open-ended/rest-style argument family
- emit low-overhead generated dispatchers that score candidates and pick the best match
- reject clearly tie-prone overload sets with a source-generator diagnostic
- keep `.d.ts` output as repeated overload declarations

Out of scope for this exact slice:

- exhaustive semantic ambiguity solving for all coercive cases
- rich runtime tracing for why a particular overload won
- doc coalescing for repeated overload comments in `.d.ts`
- dedicated generator-side special handling for common color-overload families
- replacing the current score-based matcher with full CLR-style binder semantics

## Minimal JS Repros

Generated global overloads:

```js
background("orange");
background(10);
background(10, 20, 30);
background(10, 20, 30, 40);
```

Generated host-object overloads:

```js
GeneratedHostBindingSample.Pick("x");
GeneratedHostBindingSample.Pick(7);
GeneratedHostBindingSample.Pick(true);
```

Open-ended `ReadOnlySpan<T>` shape:

```js
consume();
consume(1);
consume(1, 2, 3, 4);
```

## Planned And Current Test Coverage

Current focused coverage lives in:

- `tests\Okojo.Tests\GeneratedGlobalInstallerTests.cs`
  - `Generated_Global_Installer_Exposes_Typed_Function_And_Properties`
- `tests\Okojo.Tests\HostInteropTests.cs`
  - `GeneratedHostBindingSupportsReadOnlySpanArguments`
- `tests\Okojo.Tests\GeneratedDocDeclarationTests.cs`
  - `DocGenerator_Emits_Rest_Params_For_ReadOnlySpan`
  - overload declaration assertions for `background(...)` and `Pick(...)`

Still worth adding later:

- explicit generator-driver coverage for `OKOJOGEN001`
- focused equal-score tie tests once debug/tie diagnostics exist

## Current Resolution Algorithm

### 1. Group by exported JS name

The generator does not emit one JS entrypoint per CLR overload.

Instead, methods are grouped by their exported JS-visible name and one dispatcher is generated for that export. This keeps generated globals and generated host objects aligned.

### 2. Analyze each overload set once

For each CLR candidate, the analysis computes:

- `RequiredCount`
- `MaxCount`
- `FixedCount`
- whether the last parameter is a trailing `ReadOnlySpan<T>`
- a per-parameter `OverloadParameterMatchKind`

`RequiredCount` follows function-length-style behavior by stopping at the first defaulted parameter.

`MaxCount` is:

- the CLR parameter count for normal methods
- `int.MaxValue` for trailing `ReadOnlySpan<T>`

### 3. Build count buckets first

Finite arities are bucketed by exact argument count before any scoring happens.

At runtime the dispatcher:

1. reads `info.ArgumentCount`
2. hoists `info.Arguments` once into a `ReadOnlySpan<JsValue>`
3. switches on argument count
4. evaluates only the overloads that can legally accept that count

Open-ended candidates such as trailing `ReadOnlySpan<T>` overloads are evaluated in the default branch with a count guard.

### 4. Hoist argument reads

Exact-count buckets hoist argument locals once:

```csharp
var __jsArg0 = __jsArgCount > 0 ? __jsArgs[0] : JsValue.Undefined;
```

The guarded form matters because optional/defaulted-parameter probes may still reference a later logical argument in a lower-arity bucket.

### 5. Score each candidate

Each candidate starts with:

- `__jsMatched = true`
- `__jsScore = 0`

Every parameter contributes either:

- a concrete score increase, or
- a hard mismatch that sets `__jsMatched = false`

The generated dispatcher picks the candidate with the lowest score.

### 6. Parameter scoring rules

Current fast paths:

- `JsValue`
  - always matches
  - score `0`
- `string`
  - string => score `0`
  - `null` => score `1`
  - other values => score `30`
- `bool`
  - bool => score `0`
  - other values => score `30`

Other categories defer to `CallInfo.TryGetConversionScore<T>(...)`, which delegates into `HostValueConverter`.

These categories include:

- numeric primitives
- `JsObject`
- `object`
- reference types
- other value types

Trailing `ReadOnlySpan<T>` parameters score each extra consumed argument individually, with an extra `+2` penalty per additional span element. This keeps exact fixed-arity overloads preferable when both shapes are otherwise compatible.

### 7. Optional/defaulted parameters

For defaulted parameters, the generated matcher only probes the argument when the caller actually supplied it.

Missing optional arguments do not add mismatch cost.

### 8. Tie behavior

The runtime dispatcher only replaces the current best candidate when:

```csharp
__jsScore < __jsBestScore
```

That means equal-score ties keep the earlier candidate encountered in generated order.

The generator tries to prevent the obvious bad cases before code is emitted, but tie prevention is intentionally conservative today rather than exhaustive.

## Ambiguity Diagnostics

Current diagnostic:

- `OKOJOGEN001` - ambiguous generated overload

The current analysis reports an error when two overloads:

1. overlap in accepted argument count, and
2. can clearly tie at one of those counts

The current tie detector is conservative. It treats these as tie-prone:

- same match kind on the same argument position
- any pair involving `JsValue`
- numeric-vs-numeric families

This is meant to catch the high-confidence bad cases cheaply. It is not a full semantic proof engine for every coercive conversion path.

## Match Kinds

Current analysis classifies parameters into these families:

- `JsValue`
- `String`
- `Boolean`
- `Numeric`
- `JsObject`
- `Object`
- `Reference`
- `Other`

and span variants of those same families for trailing `ReadOnlySpan<T>` element types.

The match kind is used for:

- cheap ambiguity screening
- specialized fast matcher emission
- deciding when the dispatcher can stay on a low-overhead path versus asking `CallInfo` for a conversion score

## `.d.ts` Shape

The TypeScript side intentionally stays as repeated overload declarations:

```ts
declare function background(color: string): void;
declare function background(gray: number): void;
declare function background(r: number, g: number, b: number): void;
declare function background(r: number, g: number, b: number, a: number): void;
```

This is preferred over tuple-union rest signatures for readability and editor ergonomics.

## Reference Observations

### V8 / Node

There is no direct V8 or Node runtime primitive that maps to "generated C# host-binding overload selection by JS export name".

So this work is not trying to copy a specific V8 or Node overload binder.

The relevant Node-facing goal is behavioral predictability for Okojo-owned host APIs, not byte-for-byte emulation of an external binder.

### TypeScript

For declaration output, the practical reference is ambient TypeScript overload style:

- repeated overload declarations are preferred
- default parameter initializers are not emitted in `.d.ts`
- optional/defaulted CLR parameters map to optional declaration positions rather than initializer syntax

### QuickJS

No specific QuickJS behavior is currently being copied here.

## Copy Vs Intentional Difference

Intentional differences from a full CLR overload binder:

- dispatch is score-based and generator-shaped, not runtime-reflection-based
- the generator prefers low overhead over full semantic completeness
- tie detection is conservative and compile-time, not exhaustive
- equal-score runtime ties currently fall back to earlier generated order if the generator did not reject the set

These are deliberate tradeoffs to keep the hot path simple and predictable for generated Okojo bindings.

## Perf Plan

Current hot-path choices:

- count-first dispatch bucketing
- one hoisted argument span read per dispatcher
- hoisted argument locals for exact-count buckets
- specialized `JsValue` / `string` / `bool` matchers before generic scoring
- slower or more complex conversion logic deferred to `CallInfo` / `HostValueConverter`

Near-term follow-up items already tracked:

1. doc coalescing for repeated overload comments
2. dedicated generator-side handling for color-overload families
3. tie-break/debug diagnostics for equal-score candidates

## Important Files

- `src\Okojo.SourceGenerator\OverloadGeneration\OverloadDispatchAnalysis.cs`
- `src\Okojo.SourceGenerator\OverloadGeneration\MethodOverloadDispatchEmitter.cs`
- `src\Okojo.SourceGenerator\OverloadGeneration\SourceGeneratorDiagnostics.cs`
- `src\Okojo\Runtime\CallInfo.cs`
- `src\Okojo\Runtime\Interop\HostValueConverter.cs`
- `tests\Okojo.Tests\GeneratedGlobalInstallerTests.cs`
- `tests\Okojo.Tests\HostInteropTests.cs`
- `tests\Okojo.Tests\GeneratedDocDeclarationTests.cs`
