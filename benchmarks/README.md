# Okojo Benchmarks

BenchmarkDotNet suite for the Okojo engine. This is the single home for
benchmark methodology, the pinned comparison baseline, and the latest measured
numbers. Do not quote numbers without re-running.

## Comparison baseline

- Comparison target: [Jint](https://github.com/sebastienros/jint), pinned via
  `benchmarks/Okojo.Benchmarks/Okojo.Benchmarks.csproj` (currently **4.16.1**).
- Okojo RegExp runs on its own spec-compatible backtracking VM
  (`src/Okojo.Text.RegularExpressions`).

## Suites

| Class | What it measures |
| --- | --- |
| `VsJintBenchmarks` | Micro scenarios (`benchmarks/Okojo.Benchmarks/scripts/*.js`: `for-loop-sum`, `smi-sum-loop`, `arith`, `lexical-block`, `closure-heavy`, `pure-function-call`, `math-call`, `named-get`, `object`, `many-object`, `indexing`), Okojo vs Jint execution |
| `JintSuiteComparisonBenchmarks` | Port of Jint's `EngineComparisonBenchmark` (dromaeo/linq/json modern workloads), split into parse+compile and execution lanes, Okojo vs Jint |
| `OkojoCompileBenchmarks`, `ParseCompileBenchmarks` | Parse/compile cost |
| `OkojoPromiseBenchmarks`, `OkojoAwaitBenchmarks`, `OkojoAsyncGeneratorBenchmarks` | Async/promise paths |
| `JsObjectPathBenchmarks`, `OkojoGlobalBindingBenchmarks`, `OkojoNamedPropertyLayoutBenchmarks` | Property and binding paths |
| `OkojoJsonBenchmarks` | JSON paths |
| `FunctionCallBenchmarks`, `VmLoopDispatchBenchmarks` | Call and dispatch overhead |
| `RegExpEngineBenchmarks` | RegExp engine scenarios |
| `CodeInstanceSplitBenchmarks` | Compile/link/closure costs for the code/instance split |

## How to run

Full suite (takes a while):

```powershell
dotnet run --project benchmarks/Okojo.Benchmarks/Okojo.Benchmarks.csproj -c Release
```

Filtered run (BenchmarkDotNet filter syntax):

```powershell
dotnet run --project benchmarks/Okojo.Benchmarks/Okojo.Benchmarks.csproj -c Release -- --filter *VsJintBenchmarks*
```

`JintSuiteComparisonBenchmarks` supports two environment switches:

- `OKOJO_BENCH_CASE=<key>` — run a single script (e.g. `minimal`,
  `dromaeo-object-regexp-modern`) instead of the full set.
- `OKOJO_BENCH_QUICK=1` — dry single-invocation run for dev-iteration
  verification. Numbers from quick runs are not publishable results.

## Latest results

`VsJintBenchmarks`, 2026-09-11, ShortRun (`DOTNET_TieredPGO=0`), Jint 4.16.1.
Re-run with `--filter *VsJintBenchmarks*`. Ratio baseline is Jint execution;
lower is better for Okojo.

| Scenario | Jint mean | Okojo mean | Ratio | Jint alloc/op | Okojo alloc/op |
| --- | ---:| ---:| ---:| ---:| ---:|
| arith | 3.01 us | 1.02 us | 0.34 | 144 B | - |
| closure-heavy | 74.31 us | 21.61 us | 0.29 | 17528 B | 1064 B |
| for-loop-sum | 421.23 us | 168.71 us | 0.40 | 144 B | - |
| indexing | 38369.73 us | 11802.44 us | 0.31 | 25361160 B | 222499 B |
| lexical-block | 7897.41 us | 3809.68 us | 0.48 | 144 B | - |
| many-object | 640.97 us | 279.06 us | 0.44 | 711088 B | 432000 B |
| math-call | 1321.75 us | 702.95 us | 0.53 | 1063536 B | - |
| named-get | 4777.07 us | 2743.71 us | 0.57 | 256 B | 112 B |
| object | 62.67 us | 33.70 us | 0.54 | 96592 B | 43416 B |
| pure-function-call | 903.46 us | 282.79 us | 0.31 | 504 B | 200 B |
| smi-sum-loop | 3492.38 us | 1881.77 us | 0.54 | 2872464 B | - |

On these micro execution scenarios Okojo leads Jint 4.16.1 by roughly 2-3x
with consistently lower allocation. This does not generalize to every
workload, so refresh this table with a new run before quoting it, and use
`JintSuiteComparisonBenchmarks` for the broader ported-suite picture.

### `JintSuiteComparisonBenchmarks`

2026-09-11, ShortRun (`DOTNET_TieredPGO=0`), Jint 4.16.1, i7-13700F / .NET
10.0.12. Re-run with `--filter *JintSuiteComparisonBenchmarks*`.
Ratios are Okojo / Jint; below 1.00 favors Okojo.

| Script | Parse Jint | Parse Okojo (ratio) | Parse alloc O/J | Exec Jint | Exec Okojo (ratio) | Exec alloc O/J |
| --- | ---:| ---:| ---:| ---:| ---:| ---:|
| array-stress | 19.35 us | 19.63 us (1.01) | 11,806 / 29,560 B (0.40) | 1.719 ms | 1.630 ms (0.95) | 1,083,437 / 1,055,000 B (1.03) |
| dromaeo-3d-cube-modern | 304.30 us | 341.35 us (1.12) | 161,555 / 300,800 B (0.54) | 3.377 ms | 2.337 ms (0.69) | 725,464 / 1,229,267 B (0.59) |
| dromaeo-core-eval-modern | 11.54 us | 29.25 us (2.53) | 22,922 / 20,928 B (1.10) | 0.601 ms | 0.974 ms (1.62) | 1,797,331 / 168,248 B (10.68) |
| dromaeo-object-array-modern | 33.51 us | 63.20 us (1.89) | 44,655 / 48,792 B (0.92) | 9.171 ms | 6.777 ms (0.74) | 4,330,926 / 9,298,742 B (0.47) |
| dromaeo-object-regexp-modern | 184.71 us | 370.57 us (2.01) | 240,741 / 234,304 B (1.03) | 39.538 ms | 32.856 ms (0.83) | 68,542,808 / 86,904,371 B (0.79) |
| dromaeo-object-string-modern | 102.07 us | 156.46 us (1.53) | 91,090 / 126,656 B (0.72) | 20.103 ms | 17.730 ms (0.88) | 9,855,406 / 21,755,680 B (0.45) |
| dromaeo-string-base64-modern | 92.42 us | 97.70 us (1.06) | 47,591 / 102,515 B (0.46) | 12.623 ms | 8.890 ms (0.70) | 2,102,840 / 1,605,864 B (1.31) |
| evaluation-modern | 6.68 us | 11.05 us (1.65) | 8,104 / 11,952 B (0.68) | 1.471 us | 0.760 us (0.52) | 616 / 1,552 B (0.40) |
| json-parse-modern | 25.23 us | 31.56 us (1.25) | 19,764 / 37,728 B (0.52) | 13.260 ms | 11.119 ms (0.84) | 15,581,895 / 11,631,924 B (1.34) |
| linq-js | 2.165 ms | 3.403 ms (1.57) | 1,752,707 / 1,565,816 B (1.12) | 26.44 us | 17.52 us (0.66) | 58,240 / 75,897 B (0.77) |
| minimal | 1.02 us | 3.50 us (3.43) | 3,333 / 3,296 B (1.01) | 81.45 ns | 97.28 ns (1.19) | 200 / 296 B (0.68) |
| stopwatch-modern | 28.16 us | 40.37 us (1.43) | 28,359 / 33,176 B (0.85) | 63.912 ms | 56.876 ms (0.89) | 10,976,032 / 12,349,184 B (0.89) |

Read: execution favors Okojo on 10 of 12 scripts; parse+compile favors Jint
on all 12 (roughly 1.0-2.5x, 3.4x on the tiny `minimal` script). The two
execution regressions are `dromaeo-core-eval-modern` (1.62x time, 10.68x
alloc — the outlier on both axes) and `minimal` noise-level (1.19x on a
sub-microsecond workload). Execution allocation favors Okojo on 8 of 12;
the other alloc regressions are `dromaeo-string-base64-modern` (1.31x) and
`json-parse-modern` (1.34x).

## Known gaps under measurement

- Parse+compile is behind Jint across the ported suite (see table above);
  closing that gap is compiler-frontend work, not VM work.
- `dromaeo-core-eval-modern` execution is the one significant execution
  regression (1.62x time, 10.68x alloc); `eval`-heavy path work belongs here.
- RegExp split-path tuning continues against Jint 4.16.1. Detail:
  `docs/performance/reports/OKOJO_REGEXP_SPLIT_PERF_NOTE.md`.
- Deeper dotnet-trace methodology notes live under `docs/performance/`.
