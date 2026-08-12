# Okojo Library Split Plan

## Purpose

This document defines the target architecture for splitting the current monolithic `src/Okojo` project into a set of clean, independently usable libraries.

Goals:

- JS-compatible C# implementation libraries that are **engine-independent** (no dependency on `JsValue`, `JsRealm`, or the VM)
- a JS **engine** assembly that depends on those libraries
- a JS **runtime** (embedding/container) assembly on top of the engine
- ECMA-262-correct job queue semantics instead of host event-loop queues baked into the core agent
- one regex engine (`Okojo.Text.RegularExpressions`) replacing the current two-and-a-half engines

Backward compatibility is explicitly not a goal. This plan optimizes for the best dependency shape.

## Current State Summary

`src/Okojo` is a single assembly (~131k lines, zero NuGet deps) containing:

- parser (`Okojo.Parsing`), compiler (`Okojo.Compiler`), bytecode (`Okojo.Bytecode`)
- object model (`Okojo.Values`, `Okojo.Objects`), VM + intrinsics (`Okojo.Runtime`)
- an embedded Scratch regex engine + Unicode tables (`Okojo.RegExp`, 32 files, ~39k lines)
- Intl/globalization logic (`Runtime/Intl/*`, `Intrinsics.Intl.cs`, `Js*Objects.cs`, embedded `.txt` data)
- host concepts mixed into the core: agent task queues, workers, CLR interop, module source loading, host scheduling

Separate standalone projects already exist:

- `EcmaRegex/` — an engine-independent ECMAScript regex library (git submodule), exposed to the engine through the `Okojo.RegExp.EcmaRegex` adapter implementing `IRegExpEngine`
- `Okojo.Hosting`, `Okojo.WebPlatform`, `Okojo.Browser`, `Okojo.Node`, etc. — host/profile layers

Key problems this plan fixes:

1. `Okojo` mixes engine, runtime, and host policy in one assembly.
2. The regex subsystem is triplicated: Scratch engine, EcmaRegex library, and a .NET `System.Text.RegularExpressions` fallback bridge in `JsRegExpRuntime.cs`.
3. Unicode data is duplicated in different encodings (`ScratchUnicode*` vs `EcmaRegex/Internal/Unicode*`).
4. `JsAgent` models `priorityMicrotasks` / `microtasks` / `tasks` queues — an HTML/Node event-loop shape, not the ECMA-262 job queue model.
5. Intl logic is split between engine-coupled `Js*Objects` and portable data/logic that should live in a standalone globalization library.

## Target Architecture

### Dependency graph

```text
Okojo.Text.Unicode          Okojo.Numerics          (leaf libraries, no Okojo deps)
        │                         │
        │                         │
        ▼                         ▼
Okojo.Text.RegularExpressions  Okojo.Globalization  (mid libraries)
        │                   │ (uses Unicode + Numerics)
        └────────────┬────────────┘
                     ▼
            Okojo.JavaScript        (JS engine: parser, compiler, bytecode, VM,
                     │               object model, ECMA-262 intrinsics, job queues)
                     ▼
       Okojo.JavaScript.Runtime     (embedding runtime: JsRuntime, builder, agents,
                     │               modules, workers, host scheduling seams, CLR interop)
                     ▼
       Okojo.Hosting / Okojo.WebPlatform / Okojo.Browser / Okojo.Node / ...
```

Direction of the dependency arrows (`A ↑ B` reads "B depends on A"):

```text
Okojo.Text.Unicode
    ↑
Okojo.Text.RegularExpressions
    ↑
Okojo.JavaScript

Okojo.Globalization
    ↑
Okojo.JavaScript

Okojo.Numerics
    ↑
Okojo.JavaScript
```

### Repository layout

```text
src/
  Okojo.Text.Unicode/
  Okojo.Numerics/
  Okojo.Text.RegularExpressions/
  Okojo.Globalization/
  Okojo.JavaScript/                   # the engine
  Okojo.JavaScript.Runtime/           # the embedding runtime
  Okojo.Hosting/
  Okojo.WebPlatform/
  Okojo.Browser/
  Okojo.Node/
  ...                                # diagnostics, debug server, repl, wasm,
                                     # dotnet/reflection interop, annotations, sourcegen
tests/
  Okojo.Text.Unicode.Tests/
  Okojo.Numerics.Tests/
  Okojo.Text.RegularExpressions.Tests/
  Okojo.Globalization.Tests/
  Okojo.JavaScript.Tests/
  Okojo.JavaScript.Runtime.Tests/
  Okojo.Tests/                        # test262 + integration (becomes the JS conformance suite)
```

The `EcmaRegex/` submodule is vendored into `src/Okojo.Text.RegularExpressions` and the submodule is removed. The repo no longer depends on an external regex repo.

## Library Definitions

### 1. `Okojo.Text.Unicode`

Engine-independent, zero dependencies (BCL only).

Owns:

- code point / UTF-16 utilities: surrogate pair decoding, `AdvanceStringIndex`, code point iteration over `ReadOnlySpan<char>`
- Unicode data tables (single source of truth): General_Category, binary properties (ID_Start/ID_Continue, WhiteSpace, Emoji, ...), Script / Script_Extensions, string properties (RGI_Emoji etc.), case-folding equivalence classes
- case mapping algorithms (default + special casing, final sigma)
- Unicode segmentation algorithms and data (grapheme / word / sentence, UAX #29)
- generated-tooling: the table generator that currently lives in `EcmaRegex/tools/generate_unicode.py` is moved here so tables are reproducible

Sources:

- `EcmaRegex/src/EcmaRegex/Internal/UnicodePropertyDatabase.cs`, `UnicodePropertyData*.Generated.cs`, `UnicodeStringPropertyData.Generated.cs`, `UnicodeCaseFolding*.cs`, `Utf16Utility.cs`
- `src/Okojo/Runtime/JsStringCaseOperations.cs` (algorithm over `ReadOnlySpan<char>` instead of `JsString`)
- `src/Okojo/Runtime/JsSegmenterObjects.cs` segmentation cores
- Scratch Unicode tables in `src/Okojo/RegExp/ScratchUnicode*` are **deleted**; the `Okojo.Text.Unicode` tables replace them

### 2. `Okojo.Numerics`

Engine-independent, zero dependencies (BCL only).

Owns:

- `BigInt` — ECMAScript arbitrary-precision integer semantics (value type; thin wrapper over `System.Numerics.BigInteger` with spec-radix parse/format, `AsIntN`/`AsUintN`, `NumberToBigInt`)
- exact decimal (`ExactDecimal`) — `BigInteger` unscaled + scale + rounding modes, currently private inside `JsNumberFormatObject`
- double → string (ECMAScript `Number::toString(10)`) and radix 2..36 conversions
- exact-precision formatting (`FormatExponential`, `FormatPrecision`, `RoundToSignificantDigits`)
- `SumPrecise` (Shewchuk exact summation)
- ECMA-262 time math: `MakeDay`/`MakeTime`/`MakeDate`/`TimeClip`, civil ↔ day-number, ISO/legacy date parsing and UTC formatting

Sources:

- `src/Okojo/Values/JsBigInt.cs`
- `src/Okojo/Runtime/JsNumberFormatting.cs`, `JsNumberPrecisionFormatting.cs`
- `src/Okojo/Internals/Sumprecise.cs`
- `src/Okojo/Runtime/Intrinsics.BigInt.cs` (pure helpers only)
- `src/Okojo/Runtime/Intrinsics.Date.cs` (pure math portion)
- `src/Okojo/Runtime/Intrinsics.NumberPrototype.cs` (radix conversion helpers)
- `src/Okojo/Runtime/JsNumberFormatObjects.cs` (`ExactDecimalValue` + rounding/grouping core)
- `src/Okojo/Runtime/JsDurationFormatObjects.cs` (fractional/BigInteger math)

### 3. `Okojo.Text.RegularExpressions`

The single ECMAScript regex engine. Depends only on `Okojo.Text.Unicode`.

This is the vendored + re-namespaced EcmaRegex library.

- project/assembly: `Okojo.Text.RegularExpressions`
- root namespace: `Okojo.Text.RegularExpressions` (mirrors `System.Text.RegularExpressions`)
- public types: `EcmaRegex` (compiled regex), `EcmaRegexOptions`, `EcmaRegexFlagSet`, `EcmaMatch`/`EcmaCapture`, `MatchEnumerable`, exceptions
- internal pipeline: parser → capture pre-scan → character-class algebra → prioritized bytecode + backtracking VM → optional linear NFA

Intentional differences vs today:

- delete `src/Okojo/RegExp/Scratch*` engine (parser, bytecode, matcher, Unicode tables) — `EcmaRegex` becomes the one engine
- delete `src/Okojo.RegExp.EcmaRegex` adapter project
- delete the .NET `System.Text.RegularExpressions` fallback bridge in `src/Okojo/Runtime/JsRegExpRuntime.cs`
- delete the `IRegExpEngine` seam; the engine's `RegExp` built-in calls `Okojo.Text.RegularExpressions` directly (a small internal wrapper in the engine can keep the old `Compile`/`Exec` shape)
- Unicode property data moves to `Okojo.Text.Unicode`; the regex library consumes it

### 4. `Okojo.Globalization`

ECMA-402 (Intl)-compatible cores. Depends on `Okojo.Text.Unicode` and `Okojo.Numerics`.

Owns all portable Intl data and algorithms:

- locale data: tag mappings, likely subtags, canonicalization/validation algorithms (from `OkojoIntl*Data` + the pure cluster in `Intrinsics.Intl.cs`)
- collation: `CollatorCore`
- number formatting: `NumberFormatterCore` (compact/scientific/currency/unit, grouping incl. Indian, rounding modes, exact decimal)
- date/time formatting: `DateTimeFormatCore` (field/part building over `CultureInfo` + portable date-parts struct + calendar data)
- plural rules: `PluralRulesCore`
- relative time: `RelativeTimeFormatCore`
- list format: `ListFormatCore`
- display names: `DisplayNamesCore`
- duration format: `DurationParserCore` + fractional formatting
- segmentation: `SegmenterCore`
- locale-aware string casing (Turkic/Lithuanian/sigma) over `ReadOnlySpan<char>`
- numbering-system data + digit transliteration
- time zone canonicalization data
- embedded data resources (`CalendarData.txt`, `LocaleData.txt`, `LikelySubtags.txt`) move with this assembly

The engine keeps only thin `Js*Object` wrappers (state + part-object/array creation + bound callbacks + `JsValue` argument coercion), delegating to these cores.

### 5. `Okojo.JavaScript` — the JS engine

Pure ECMAScript engine. Depends on `Okojo.Text.Unicode`, `Okojo.Numerics`, `Okojo.Text.RegularExpressions`, `Okojo.Globalization`.

Owns:

- parser (`Parsing`), compiler (`Compiler`), bytecode (`Bytecode`)
- object model: `JsValue`, `JsString`, `JsObject`, shapes/layouts, all `Objects/*`
- VM, realms, agents (ECMA-262 agent part), execution contexts, call frames, generators
- ECMAScript intrinsics (Object, Array, String, Number, BigInt, Promise, RegExp, Intl wrappers, ...)
- ECMA-262 module graph, linking, and evaluation semantics
- ECMA-262 job queue model (see below) — **script jobs and promise jobs only**

Does not own:

- host task scheduling, event loops, timers
- module source loaders, file/network I/O
- workers/messaging, CLR interop
- debug server, repl, diagnostics rendering

Recommended namespaces (rename from current `Okojo.*`):

- `Okojo.JavaScript` (core: `JsValue`, `Tag`)
- `Okojo.JavaScript.Values`
- `Okojo.JavaScript.Objects`
- `Okojo.JavaScript.Parsing`
- `Okojo.JavaScript.Compiler`
- `Okojo.JavaScript.Bytecode`
- `Okojo.JavaScript.Execution` (realms/VM/jobs — formerly `Okojo.Runtime`)
- `Okojo.JavaScript.Intrinsics`

### 6. `Okojo.JavaScript.Runtime` — the embedding runtime

Depends on `Okojo.JavaScript`.

Owns:

- `JsRuntime`, `JsRuntimeBuilder`, `JsRuntimeOptions`/`Core`/`Host`/`LowLevelHost` options
- `JsAgent` host-side surface: worker hosting, cross-agent messaging, host job sources
- `HostPump`, `JsAgentRunner`
- module source loading (`IModuleSourceLoader`, file/worker script loaders)
- host scheduling seams (`IHostTaskScheduler`, `IHostDelayScheduler`, `IQueuedHostDelayScheduler`, `HostTaskQueueKey`)
- CLR interop (`Runtime/Interop/*`, `Okojo.Reflection`, `Okojo.DotNet.Modules` surface)
- source maps, debugger/checkpoint glue, `JsGlobalInstaller`
- `Okojo.SourceGenerator` + `Okojo.Annotations` remain build-time tooling for this layer

Host profiles (`Okojo.Hosting`, `Okojo.WebPlatform`, `Okojo.Browser`, `Okojo.Node`) keep their roles on top of the runtime.

## ECMA-262 Job Queue Correction

### What is wrong today

`JsAgent` (in the engine) owns three queues — `priorityMicrotasks`, `microtasks`, `tasks` — and `PumpJobsCore` drains priority-microtasks → microtasks → one task, repeatedly. That is an HTML/Node event-loop shape:

- Node `nextTick` priority
- HTML microtask checkpoint
- HTML task queue

ECMA-262 defines none of that. It defines:

- a **Job Queue**: a FIFO queue of PendingJobs, named (spec examples: `ScriptJobs`, `PromiseJobs`; hosts may define more)
- **ScriptJobs** created by ScriptEvaluation/ModuleEvaluation
- **PromiseJobs** created by `HostEnqueuePromiseJob` (promise reactions, async/await continuations, `queueMicrotask`)
- the host deciding job-class ordering via `HostEnqueueJob` / `HostCallJobCallback`

Baking `tasks`/`microtasks`/`priorityMicrotasks` into the agent couples the language core to a specific host's event loop and leaks host policy into every embedder.

### Target model

Engine (`Okojo.JavaScript`) owns only the ECMA-262 job queue:

```text
JsAgent (engine part)
  ├─ ScriptJobs queue        # script/module evaluation
  ├─ PromiseJobs queue       # reactions, async continuations, queueMicrotask
  └─ EnqueueJob(queueName, job)   # host-defined named queues allowed
     RunJobs() / PumpJobs()       # FIFO within each queue
     HostEnqueuePromiseJob seam
```

- The engine exposes `EnqueueScriptJob`, `EnqueuePromiseJob`, and `HostEnqueueJob(queueName, job)`.
- Job execution is FIFO per queue. The host decides ordering across job classes through a scheduling seam; the engine does not hardcode a "task vs microtask" drain.
- `queueMicrotask` is a host-installed global that enqueues into the engine's `PromiseJobs` queue (per HTML's `HostEnqueuePromiseJob` mapping).
- HTML task sources (timers, messages, rendering, Node `check`/`nextTick`) are **host job sources**, implemented in `Okojo.Hosting` / `Okojo.WebPlatform` / `Okojo.Browser` / `Okojo.Node`, injecting work into the engine through `HostEnqueueJob` and the `IHostTaskScheduler` seam.
- The engine never stores `Action` task queues or host queue keys. `HostTaskQueueKey`/`IHostTaskQueuePump`/`ThreadAffinityHostLoop`/`ManualHostEventLoop` already live in `Okojo.Hosting` and stay there.

### Split of `JsAgent`

- engine part (in `Okojo.JavaScript`): realm list, symbol registry, private brands, module graph, execution contexts, `ScriptJobs`/`PromiseJobs` queues, breakpoint registry
- runtime part (in `Okojo.JavaScript.Runtime`): host task scheduling, worker messaging, `PostMessage`, host job enqueueing, `IHostTaskScheduler` wiring, pump/runner

## Migration Map

### From `EcmaRegex/` (vendored)

| Current | New home |
|---|---|
| `src/EcmaRegex/EcmaRegex.cs`, `EcmaMatch.cs`, `EcmaCapture.cs`, `EcmaRegexOptions.cs`, `EcmaRegexFlags.cs`, `EcmaRegexExceptions.cs`, `EcmaRegexPattern.cs`, `MatchEnumerable.cs` | `src/Okojo.Text.RegularExpressions/`, namespace `Okojo.Text.RegularExpressions` |
| `Internal/Ast.cs`, `RegexParser.cs`, `RegexCompiler.cs`, `BacktrackingVm.cs`, `LinearNfa.cs`, `CharacterClass.cs`, `RegexProgram.cs`, `ExecutionBudget.cs`, `ValueStack.cs` | `src/Okojo.Text.RegularExpressions/Internal/` |
| `Internal/Unicode*.Generated.cs`, `UnicodeCaseFolding*.cs`, `UnicodePropertyDatabase.cs`, `Utf16Utility.cs` | `src/Okojo.Text.Unicode/` |
| `tools/generate_unicode.py`, string-property generators | `src/Okojo.Text.Unicode/tools/` |
| `benchmarks/EcmaRegex.Benchmarks`, `samples`, `tests` | fold into `Okojo.Text.RegularExpressions` counterparts |

### From `src/Okojo/` (engine → libraries)

| Current location | New home |
|---|---|
| `RegExp/ScratchUnicode*` | **delete** (replaced by `Okojo.Text.Unicode` tables) |
| `RegExp/ScratchRegExp*`, `RegExp/CompiledProgram.cs`, `RegExp/RegExpIr.cs`, `RegExp/RegExpBytecode.cs`, `RegExp/RegExpCharacterSet.cs`, `RegExp/RegExpEngine.cs`, `RegExp/ScratchPooled*` | **delete** (replaced by `Okojo.Text.RegularExpressions`) |
| `RegExp/IRegExpEngine.cs`, `RegExpCompiledPattern.cs`, `RegExpMatchResult.cs`, `RegExpRuntimeFlags.cs` | **delete** seam; engine calls `Okojo.Text.RegularExpressions` directly |
| `Runtime/Intl/*.cs` | `Okojo.Globalization` |
| `Runtime/Intl/Data/*.txt` | `Okojo.Globalization` resources |
| `Runtime/JsStringCaseOperations.cs`, `JsStringLocaleCaseOperations.cs` | `Okojo.Text.Unicode` (algorithm) + `Okojo.Globalization` (locale-aware wrapper) |
| `Runtime/JsSegmenterObjects.cs` (segmentation cores) | `Okojo.Text.Unicode` |
| `Runtime/JsNumberFormatting.cs`, `JsNumberPrecisionFormatting.cs` | `Okojo.Numerics` |
| `Runtime/JsCollatorObject.cs`, `JsNumberFormatObject.cs`, `JsDateTimeFormatObject.cs`, `JsPluralRulesObject.cs`, `JsRelativeTimeFormatObject.cs`, `JsListFormatObject.cs`, `JsDisplayNamesObject.cs`, `JsDurationFormatObject.cs`, `JsLocaleObject.cs` | cores → `Okojo.Globalization`; thin `Js*Object` wrappers stay in `Okojo.JavaScript` |
| `Runtime/Intrinsics.Intl.cs` | pure string-tag/validation/canonicalization cluster → `Okojo.Globalization`; constructor/prototype glue stays in engine |
| `Values/JsBigInt.cs`, `Internals/Sumprecise.cs` | `Okojo.Numerics` |
| `Runtime/Intrinsics.BigInt.cs` (pure helpers), `Intrinsics.Date.cs` (pure math), `Intrinsics.NumberPrototype.cs` (radix helpers) | `Okojo.Numerics` |
| `Runtime/JsRuntime.cs`, `JsRuntimeBuilder.cs`, `JsRuntime*Options.cs` | `Okojo.JavaScript.Runtime` |
| `Runtime/Interop/*`, `Runtime/Worker*`, `Runtime/DefaultHostTaskScheduler.cs`, `Runtime/HostTask*`, `Runtime/HostPump.cs`, `Runtime/JsAgentRunner.cs`, `Runtime/FileModuleSourceLoader.cs`, `Runtime/*WorkerScriptSourceLoader*`, `Runtime/IModuleSourceLoader.cs`, `Runtime/IHostTaskScheduler.cs`, `Runtime/IHostDelayScheduler.cs`, `Runtime/ITimerFactory.cs`, `Runtime/IBackgroundScheduler.cs`, `Runtime/IHostMessageSerializer.cs`, `Runtime/JsDefaultHostMessageSerializer.cs` | `Okojo.JavaScript.Runtime` |
| `SourceMaps/*` | `Okojo.JavaScript.Runtime` |
| everything else (Parsing, Compiler, Bytecode, Objects, Values, JsRealm/VM, intrinsics, module graph, agent job queues) | `Okojo.JavaScript` |

### Project references (after split)

- `Okojo.Text.RegularExpressions` → `Okojo.Text.Unicode`
- `Okojo.Globalization` → `Okojo.Text.Unicode`, `Okojo.Numerics`
- `Okojo.JavaScript` → Text.Unicode, Numerics, Text.RegularExpressions, Globalization
- `Okojo.JavaScript.Runtime` → `Okojo.JavaScript`
- `Okojo.Hosting` / `Okojo.WebPlatform` / `Okojo.Browser` / `Okojo.Node` → `Okojo.JavaScript.Runtime` (+ each other as today)
- `tools/Test262Runner` → `Okojo.JavaScript.Runtime` (+ `Okojo.Hosting`, `Okojo.WebPlatform`); drop `Okojo.RegExp.EcmaRegex` reference and the `--regexp-engine ecmaregex` switch
- `tests/Okojo.Tests` → `Okojo.JavaScript.Runtime` (+ profiles used by specific tests)

## Tooling / Solution / Test Strategy

- `Okojo.slnx` is reorganized to the new `src/` layout; a second solution section (or directory-build-props) keeps the leaf libraries independently buildable.
- Library test projects:
  - `Okojo.Text.Unicode.Tests` — tables, code points, case mapping, segmentation
  - `Okojo.Numerics.Tests` — BigInt, exact decimal, double↔string, time math
  - `Okojo.Text.RegularExpressions.Tests` — the existing `EcmaRegex.Tests` + `EcmaRegex.Test262` projects move here (namespace + package rename only)
  - `Okojo.Globalization.Tests` — collation/number/date-time/plural/relative/list/display/duration/segmenter cores
  - `Okojo.JavaScript.Tests` — parser/compiler/VM/object-model unit tests
  - `Okojo.JavaScript.Runtime.Tests` — embedding, jobs, modules, workers, interop
  - `Okojo.Tests` — test262 + end-to-end conformance (existing `Intl*`, `RegExp*`, `BigInt*`, `AgentJobQueue*` tests move with their component)
- `dotnet test tests/Okojo.Tests/Okojo.Tests.csproj` remains the fast conformance loop; the `Okojo.Compiler.Tests` split approach continues inside `Okojo.JavaScript.Tests`.
- test262 runs target `Okojo.JavaScript.Runtime` through `Test262Runner`.

## Migration Phases

1. ✅ **Extract `Okojo.Text.Unicode` + `Okojo.Numerics`** (pure, no reverse deps).
   - Done: `Okojo.Numerics` (NumberFormatting, NumberPrecisionFormatting, SumPrecise, PooledList) and `Okojo.Text.Unicode` (Utf16, UnicodeCaseFolding + generated data, generator tooling).
2. ✅ **Vendor `EcmaRegex` as `Okojo.Text.RegularExpressions`**, re-pointing its Unicode data at `Okojo.Text.Unicode`.
   - Done: submodule vendored, namespace `Okojo.Text.RegularExpressions`, engine rewired to the library as the single regex engine. Scratch engine, `IRegExpEngine` seam, `.NET Regex` bridge, `Okojo.RegExp.EcmaRegex` adapter, and `--regexp-engine` variants deleted. Full test262 (non-staging, non-annexB) passes with zero regex regressions.
3. **Extract `Okojo.Globalization`** cores from the `Js*Objects` + `Intrinsics.Intl.cs` pure cluster + `Runtime/Intl` data.
   - Engine `Js*Object` wrappers become thin delegates.
4. **Split `Okojo` into `Okojo.JavaScript` (engine) and `Okojo.JavaScript.Runtime` (runtime)**.
   - Move host/embedding/interop files out; keep engine dependency-free of host concepts.
5. **Fix the job queue model** in the engine (ScriptJobs/PromiseJobs/`HostEnqueueJob`), move host task sources to the runtime + `Okojo.Hosting`.
   - Regression-targeted by `AgentJobQueueTests`, `TimerTests`, `WorkerAgentTests`, `WebWorkerTests`, `AsyncPromiseTests`.
6. ✅ **Delete dead paths**: Scratch engine, `IRegExpEngine`, `.NET Regex` bridge, `Okojo.RegExp.EcmaRegex`, `--regexp-engine` variants.
   - Done as part of the regex consolidation in phase 2.
7. **Rename engine namespaces** to `Okojo.JavaScript.*` (mechanical, after behavior is green).
8. **Update planning docs** (`OKOJO_BROWSER_COMPATIBILITY_PLAN.md`, `OKOJO_CONCRETE_ARCHITECTURE.md`, `OKOJO_API_POLICY.md`, `OKOJO_CORE_API_REFINEMENT_PLAN.md`) to the new layer names.

## Decisions Made

- Each library presents a **standalone, obvious public API**: ECMA-402 type names (`PluralRules`, `Collator`, `ListFormat`, `RelativeTimeFormat`, `NumberFormat`, `DateTimeFormat`) with spec-aligned `*Options` records, never `*Core`-suffixed or positional-constructor-heavy surfaces.
- Data classes use clean domain names (`LocaleData`, `CalendarData`, `NumberingSystemData`, `TimeZoneData`, `UnitData`, `LunisolarCalendar`, `LikelySubtags`), not `Okojo*`-prefixed names.
- The regex library is `EcmaRegex` in `Okojo.Text.RegularExpressions`; `Ecma`-prefixed types are intentional (ECMAScript semantics), consistent within the library.
- Keep `BigInt` as a `BigInteger`-backed value type; do not write a limb-level bignum unless profiling requires it.
- Keep `.NET` culture data as the backing store for `Okojo.Globalization` (via `CultureInfo`); the library supplies ECMA-402 algorithm logic on top.
- `Okojo.Text.*` mirrors `System.Text`: `Okojo.Text.Unicode` (code points, tables, case, segmentation) and `Okojo.Text.RegularExpressions` (regex) form the text-processing library family.
- The engine may keep `InternalsVisibleTo` to `Okojo.JavaScript.Tests` and `Okojo.Compiler.Experimental`, but not to host projects.
- Task queues and host scheduling live in the runtime/host layer only; the engine owns only ECMA-262 job queues.

## Risks

- ✅ **Behavior parity during the regex consolidation** — resolved: full test262 (non-staging, non-annexB) passes after Scratch deletion; only intentional message-text differences were adjusted in tests.
- ✅ **Intl formatter extraction churn** — resolved: `Js*Objects` are thin wrappers delegating to `Okojo.Globalization` formatters; pure date-math glue stays in the engine for now (a future `Okojo.Numerics` ECMA time-math slice can absorb `Intrinsics.GetEcmaDateTimePartsForIntl`).
- **Job queue regression** — the current drain order is relied on by host profiles; migrate `Okojo.Hosting` loop implementations to the new seam before removing old agent queues.
- **Compiler split history** — the compiler cannot leave the engine assembly until bytecode/VM types share an assembly; the engine assembly is the natural home for both, so `Okojo.JavaScript` keeps parser+compiler+VM together (per `OKOJO_COMPILER_ASSEMBLY_SPLIT.md`, the lower shared layer becomes `Okojo.JavaScript` itself).
