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

2026-09-12, ShortRun (`IterationCount=5`, `LaunchCount=1`, `WarmupCount=3`),
Jint 4.16.1, BenchmarkDotNet v0.15.8, Windows 11
(10.0.26200.9445/25H2/2025Update/HudsonValley2), i7-13700F / .NET 10.0.12
(SDK 11.0.100-preview.5.26302.115). Re-run with
`--filter *JintSuiteComparisonBenchmarks*`.
Ratios are Okojo / Jint; below 1.00 favors Okojo.

| Script | Parse Jint | Parse Okojo (ratio) | Parse alloc O/J | Exec Jint | Exec Okojo (ratio) | Exec alloc O/J |
| --- | ---:| ---:| ---:| ---:| ---:| ---:|
| array-stress | 18.49 us | 19.84 us (1.07) | 11,806 / 29,560 B (0.40) | 1.644 ms | 1.672 ms (1.02) | 1,083,444 / 1,055,000 B (1.03) |
| dromaeo-3d-cube-modern | 314.68 us | 359.01 us (1.14) | 161,555 / 300,800 B (0.54) | 3.350 ms | 2.335 ms (0.70) | 725,464 / 1,229,267 B (0.59) |
| dromaeo-core-eval-modern | 11.88 us | 28.56 us (2.40) | 22,922 / 20,928 B (1.10) | 592.27 us | 974.17 us (1.64) | 1,566,037 / 168,248 B (9.31) |
| dromaeo-object-array-modern | 32.68 us | 61.99 us (1.90) | 44,655 / 48,794 B (0.92) | 8.868 ms | 6.163 ms (0.69) | 4,330,966 / 9,298,754 B (0.47) |
| dromaeo-object-regexp-modern | 191.80 us | 359.65 us (1.88) | 240,741 / 234,304 B (1.03) | 37.432 ms | 33.761 ms (0.90) | 68,531,941 / 84,401,774 B (0.81) |
| dromaeo-object-string-modern | 106.91 us | 154.39 us (1.44) | 91,090 / 126,656 B (0.72) | 21.626 ms | 17.598 ms (0.81) | 9,770,114 / 21,746,386 B (0.45) |
| dromaeo-string-base64-modern | 93.94 us | 94.40 us (1.00) | 47,591 / 102,515 B (0.46) | 11.983 ms | 8.626 ms (0.72) | 1,798,512 / 1,605,864 B (1.12) |
| evaluation-modern | 6.90 us | 11.45 us (1.66) | 8,104 / 11,952 B (0.68) | 1.46 us | 733.84 ns (0.50) | 616 / 1,552 B (0.40) |
| json-parse-modern | 24.80 us | 31.04 us (1.25) | 19,764 / 37,728 B (0.52) | 12.992 ms | 10.989 ms (0.85) | 11,102,029 / 11,631,879 B (0.95) |
| linq-js | 2.182 ms | 3.275 ms (1.50) | 1,744,981 / 1,565,816 B (1.11) | 26.66 us | 18.15 us (0.68) | 58,240 / 75,897 B (0.77) |
| minimal | 944.51 ns | 3.35 us (3.54) | 3,333 / 3,296 B (1.01) | 84.61 ns | 95.87 ns (1.13) | 200 / 296 B (0.68) |
| stopwatch-modern | 27.72 us | 40.81 us (1.47) | 28,359 / 33,176 B (0.85) | 63.347 ms | 57.621 ms (0.91) | 10,976,032 / 12,349,184 B (0.89) |

Regenerate this table from the raw BenchmarkDotNet CSV:

```bash
bash benchmarks/jint-suite-comparison-to-readme.sh BenchmarkDotNet.Artifacts/results/JintSuiteComparisonBenchmarks-report.csv
```

Read: execution favors Okojo on 9 of 12 scripts; parse+compile favors Jint
on all 12 (roughly 1.0-2.4x, 3.54x on the tiny `minimal` script). The three
execution regressions are `dromaeo-core-eval-modern` (1.64x time, 9.31x
alloc — the outlier on both axes), `array-stress` (1.02x, near noise), and
`minimal` noise-level (1.13x on a sub-microsecond workload). Execution
allocation favors Okojo on 9 of 12; the other alloc regressions are
`array-stress` (1.03x) and `dromaeo-string-base64-modern` (1.12x).

## Known gaps under measurement

- Parse+compile is behind Jint across the ported suite (see table above);
  closing that gap is compiler-frontend work, not VM work.
- `dromaeo-core-eval-modern` execution is the one significant execution
  regression (1.64x time, 9.31x alloc); `eval`-heavy path work belongs here.
- RegExp split-path tuning continues against Jint 4.16.1. Detail:
  `docs/performance/reports/OKOJO_REGEXP_SPLIT_PERF_NOTE.md`.
- Deeper dotnet-trace methodology notes live under `docs/performance/`.
