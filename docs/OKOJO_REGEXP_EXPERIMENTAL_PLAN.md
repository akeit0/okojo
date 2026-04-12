# Okojo RegExp Experimental Engine Plan

## Scope

This note covers the first optimization-focused RegExp workstream for Okojo.

Implementation detail lives separately in:

- `docs\OKOJO_REGEXP_IMPLEMENTATION_PLAN.md`

Use this document for scope, rollout, validation surfaces, and repo wiring. Use the implementation plan for algorithm and optimization strategy.

Included in this iteration:

1. create a new experimental engine project at `src\Okojo.RegExp.Experimental`
2. clone the current external RegExp engine into that project behind the existing `IRegExpEngine` seam
3. add benchmark coverage that compares the current engine against the experimental engine
4. extend `tools\Test262Runner` so it can select the experimental engine directly
5. define the focused `test262` workflow for `test/built-ins/RegExp`
6. define the initial optimization direction using references from V8, JavaScriptCore, and QuickJS

Not included in this iteration:

1. replacing the built-in non-external RegExp path
2. introducing native-code JIT generation
3. widening support for intentionally de-prioritized legacy RegExp behavior
4. making the experimental engine the default engine before benchmark and `test262` evidence justify it

## Current Codebase Snapshot

Relevant existing seams:

1. `src\Okojo\RegExp\IRegExpEngine.cs`
   - shared abstraction already exists
2. `src\Okojo\Runtime\JsRuntimeBuilder.cs`
   - `UseRegExpEngine(IRegExpEngine engine)` already exposes the embedding seam
3. `src\Okojo\Runtime\JsRuntimeCoreOptions.cs`
   - default engine is `RegExpEngine.Default`
4. `src\Okojo\RegExp\OkojoRegExpEngine.cs`
   - current external RegExp engine implementation is still built inside `Okojo.csproj`
5. `tests\Okojo.Tests\RegExpExternalEngineTests.cs`
   - current external engine already has a strong direct-engine and JS-observable regression suite
6. `tools\Test262Runner\Test262Runner.Infrastructure.cs`
   - runner currently has only a boolean split: external engine vs built-in path
7. `tools\Test262Runner\Test262Runner.Execution.cs`
   - runner currently injects `RegExpEngine.Default` directly when `UseExternalRegExpEngine` is enabled
8. `benchmarks\Okojo.Benchmarks`
   - BenchmarkDotNet project already exists and is the right home for comparison benchmarks

Important design constraint discovered during planning:

- `RegExpCompiledPattern.EngineState` is `internal` inside `src\Okojo\RegExp\RegExpCompiledPattern.cs`
- a separate `Okojo.RegExp.Experimental` assembly cannot populate that state unless `Okojo` grants access
- first-slice options are:
  1. add `InternalsVisibleTo("Okojo.RegExp.Experimental")`
  2. add an internal factory/helper in `Okojo`
  3. move engine-state ownership behind another shared contract

For the first iteration, option 1 is the simplest and lowest-risk split path.

## Minimal JS Repros

```js
/a+/.exec("baaa");
```

```js
/(?<name>a)/.exec("a").groups.name;
```

```js
/[\u0390]/ui.test("\u1fd3");
```

```js
/(.*?)a(?!(a+)b\2c)\2(.*)/.exec("baaabaac");
```

```js
new RegExp(@"\ud834\udf06", "u").test("𝌆");
```

```js
Array.from("ab".matchAll(/a?/g));
```

These repros cover the first slice of correctness-sensitive behavior that must remain stable while performance work proceeds:

1. plain literal scanning
2. named captures
3. Unicode case-fold edges
4. lookahead plus backreference behavior
5. astral code point handling in Unicode mode
6. zero-width/global iteration behavior

## Planned Files and Project Changes

### 1. Experimental project split

Add:

1. `src\Okojo.RegExp.Experimental\Okojo.RegExp.Experimental.csproj`
2. cloned engine sources under `src\Okojo.RegExp.Experimental\`
3. `Okojo.slnx` entry for the new project

Project shape for the first slice:

1. reference `..\Okojo\Okojo.csproj`
2. use namespace `Okojo.RegExp.Experimental`
3. expose a concrete engine such as `ExperimentalRegExpEngine`
4. keep `IRegExpEngine`, `RegExpCompiledPattern`, `RegExpMatchResult`, and `RegExpRuntimeFlags` owned by `Okojo`

Required supporting change in `src\Okojo\InternalsVisibleTo.cs`:

1. add `InternalsVisibleTo("Okojo.RegExp.Experimental")`

Rationale:

- this keeps the experimental engine swappable through the existing embedding seam
- it avoids a larger abstraction split before measurements justify it
- it keeps `Okojo` as the owner of the public RegExp contract for now

### 2. Benchmark coverage

Add:

1. `benchmarks\Okojo.Benchmarks\RegExpEngineBenchmarks.cs`

Initial benchmark matrix:

1. compile-only
2. first exec after compile
3. warmed exec of reused compiled pattern
4. JS-runtime path using `JsRuntimeBuilder.UseRegExpEngine(...)`

Initial scenarios:

1. simple literal scan
2. alternation
3. captures and named captures
4. Unicode property or Unicode case-fold sample
5. lookahead/backreference regression
6. global zero-width progression
7. backtracking-heavy negative case

Comparison targets:

1. current engine: `Okojo.RegExp.RegExpEngine.Default`
2. new engine: `Okojo.RegExp.Experimental.ExperimentalRegExpEngine.Default`

Benchmark command:

```powershell
dnrelay bench --project benchmarks\Okojo.Benchmarks\Okojo.Benchmarks.csproj --select RegExp
```

Benchmark policy note:

- do not treat benchmark runs as the main edit-compile inner loop
- use benchmarks after large engine changes once focused tests and targeted `test262` passes are green
- ShortRun is acceptable for directional comparison in this workstream

### 3. Test262 runner engine selection

Current state:

- runner only exposes `--no-external-regexp`
- that is no longer enough once there are three runtime choices:
  1. built-in path
  2. current external engine
  3. experimental external engine

Planned change:

1. replace the boolean option with an explicit engine mode
2. keep the old boolean alias temporarily if needed for local script compatibility

Proposed CLI:

```text
--regexp-engine built-in
--regexp-engine current
--regexp-engine experimental
```

Target runner wiring:

1. parse engine mode in `Test262Runner.Infrastructure.cs`
2. resolve the selected engine in `Test262Runner.Execution.cs`
3. add a project reference from `tools\Test262Runner\Test262Runner.csproj` to `src\Okojo.RegExp.Experimental\Okojo.RegExp.Experimental.csproj`

Focused compatibility command for this workstream:

```powershell
dnrelay run --project tools\Test262Runner\Test262Runner.csproj -- --regexp-engine experimental --filter test/built-ins/RegExp --skip-passed --full-path
```

This command should become the default local loop for experimental-engine ECMA-262 compatibility work.

## Planned Tests

### Existing regression surface to keep using

1. `tests\Okojo.Tests\RegExpExternalEngineTests.cs`
2. broader RegExp-related assertions in:
   - `tests\Okojo.Tests\GlobalConstructorsTests.cs`
   - `tests\Okojo.Tests\ObjectConstructorProgressTests.cs`
   - `tests\Okojo.Tests\RegExpModifierTests.cs`
   - `tests\Okojo.Tests\StringPrototypeTests.cs`

### Planned additions

1. shared engine-agnostic direct tests so current and experimental engines can run the same contract suite
2. focused runner option parsing tests if the runner has an existing options-test home
3. benchmark class for perf comparison
4. targeted `test262` reruns under `test/built-ins/RegExp`

Immediate validation commands:

```powershell
dnrelay test tests\Okojo.Tests\Okojo.Tests.csproj -- --filter RegExpExternalEngineTests
```

```powershell
dnrelay run --project tools\Test262Runner\Test262Runner.csproj -- --regexp-engine experimental --filter test/built-ins/RegExp --full-path --skip-passed
```

## Reference Observations

Primary references gathered for the planning pass:

1. V8 Irregexp tier-up and interpreter work
   - <https://v8.dev/blog/regexp-tier-up>
2. V8 RegExp fast-path discussion
   - <https://v8.dev/blog/speeding-up-regular-expressions>
3. JavaScriptCore Yarr overview and JIT/backtracking notes
   - <https://adrian.geek.nz/webkit_docs/regular-expressions.html>
   - <https://github.com/WebKit/WebKit/blob/main/Source/JavaScriptCore/yarr/YarrJIT.cpp>
4. QuickJS implementation notes
   - <https://bellard.org/quickjs/>
   - <https://quickjs-ng.github.io/quickjs/developer-guide/internals/>

Observed optimization patterns worth borrowing:

1. V8
   - bytecode/interpreter first, then tier-up for hot cases
   - centralized execution-path selection
   - dispatch overhead reduction
   - peephole fusion for common bytecode sequences
2. JavaScriptCore Yarr
   - parser to specialized internal pattern form
   - interpreter plus JIT split
   - explicit backtracking state handling
   - specialized code for character classes, control flow, and hot simple cases
3. QuickJS
   - direct regex-bytecode generation
   - explicit backtracking stack instead of native recursion
   - compact VM with compile-time metadata and small-footprint tables

## Copy vs Intentional Difference

Behavior and architecture copied in spirit:

1. use bytecode or similarly compact internal execution form
2. keep explicit backtracking state instead of leaning on managed recursion
3. split simple fast paths from the general matcher
4. centralize execution-path selection so call sites stay small
5. use compile-time metadata to avoid repeated setup work at exec time

Intentional differences for the first Okojo experimental slice:

1. no native JIT generation yet
2. stay managed and NativeAOT-friendly
3. keep builder-based injection as the public selection mechanism
4. use `test262` and existing JS-observable tests as the correctness authority rather than chasing engine-internal similarity for its own sake
5. prefer measurable low-allocation improvements before attempting broader architectural churn

## Performance Plan

Detailed algorithm and optimization planning now lives in `docs\OKOJO_REGEXP_IMPLEMENTATION_PLAN.md`.

High-level optimization targets for this workstream remain:

1. reduce exec-time allocations
2. reduce dispatch and branching overhead on common scans
3. precompute more search/class/Unicode metadata at compile time
4. keep uncommon semantics on explicit slow paths

## Implementation Phases

### Phase 1 - Mechanical split

1. create `src\Okojo.RegExp.Experimental`
2. clone the current engine into the new project with a distinct namespace and engine type name
3. add `InternalsVisibleTo("Okojo.RegExp.Experimental")`
4. add project entries and references

Exit condition:

- both current and experimental engines can compile and run the same direct contract tests

### Phase 2 - Benchmark harness

1. add `RegExpEngineBenchmarks`
2. compare compile, first exec, warmed exec, and JS-observable runtime paths
3. establish current-engine baseline numbers before optimization changes

Exit condition:

- the repo has a repeatable benchmark path for current vs experimental comparison

### Phase 3 - Runner integration

1. add `--regexp-engine` selection
2. wire current, experimental, and built-in choices
3. confirm `test/built-ins/RegExp` can run against the experimental engine

Exit condition:

- `Test262Runner` can target the experimental engine without code edits

### Phase 4 - Optimization passes

1. profile benchmark outliers
2. apply measured fast-path and allocation reductions
3. rerun unit tests, focused `test262`, and benchmarks after each batch

Exit condition:

- the experimental engine shows clear wins on targeted scenarios without compatibility regressions

## Operational Notes

Tooling expectations for this work:

1. use `dnrelay` for build, test, run, and bench commands
2. use `dotprobe-csharp` for symbol and workspace inspection when searching the C# surface
3. use `rg` for regex/text search and `ast-grep` for syntax-aware structural search/replace
4. keep the unit-test and focused `test262` commands short and repeatable for frequent local iteration
5. treat benchmark runs as milestone validation after larger changes rather than per-edit validation

Suggested recurring commands:

```powershell
dnrelay build .
```

```powershell
dnrelay test tests\Okojo.Tests\Okojo.Tests.csproj -- --filter RegExp
```

```powershell
dnrelay bench --project benchmarks\Okojo.Benchmarks\Okojo.Benchmarks.csproj --select RegExp
```

```powershell
dnrelay run --project tools\Test262Runner\Test262Runner.csproj -- --regexp-engine experimental --filter test/built-ins/RegExp --skip-passed --full-path
```
