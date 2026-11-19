# JavaScript engine comparison

## Merge candidate review

Three rotating fresh-process rounds used an 800 ms warm-up and a
550 ms sample target on Windows x64. The candidate includes the
concrete inline-slot access and generator lookup fixes. The study
baseline is `9bad891`. Times are medians per supplied `n` for
microbenchmarks and per invocation for v7. The final column uses
paired Okojo time ratios. The sections below record earlier study
checkpoints. [Harness and engine revisions](README.md)

| Workload | Unit | Okojo study | Okojo candidate | QuickJS | Ekuma | V8 jitless | Paired change |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Richards | ms | 3.038 | 3.109 | 2.700 | 2.917 | 1.418 | +2.3% |
| Decrypt | ms | 84.286 | 80.714 | 63.222 | 62.000 | 47.083 | −4.2% |
| RayTrace | ms | 29.368 | 30.211 | 27.571 | 19.750 | 10.200 | +2.9% |
| Splay | ms | 2.946 | 3.026 | 1.326 | 1.483 | 0.627 | +5.7% |
| NavierStokes | ms | 58.300 | 57.900 | 34.059 | 46.250 | 44.769 | −0.7% |
| array_push | ns | 16145.5 | 16290.9 | 14225.0 | 20926.6 | 7680.9 | +0.2% |
| local_destruct | ns | 177.5 | 191.3 | 306.2 | 168.4 | 162.4 | +5.6% |
| array_for_of | ns | 1733.5 | 1687.4 | 2600.7 | 1037.7 | 2160.3 | −4.6% |

Three additional paired runs compared the candidate with main's
engine sources at `c3a5ca1`. The main binary was built at `72cae11`
with the same engine sources. These runs used a 1,100 ms warm-up
and a 700–720 ms sample target.

| V8 v7 workload | Main (ms) | Candidate (ms) | Paired change |
| --- | ---: | ---: | ---: |
| Richards | 3.217 | 3.104 | −3.7% |
| Splay | 4.743 | 2.951 | −39.1% |
| NavierStokes | 62.000 | 57.615 | −7.3% |

## Synchronous iteration

Three rotating fresh-process rounds used an 850 ms warm-up and a
580 ms sample target on Windows x64. The inputs were the pinned
QuickJS release microbenchmarks and V8 v7 controls. Times are medians
per supplied `n` for microbenchmarks and per invocation for v7.
Changes use median paired Okojo time ratios against `ceef195`.
[Harness and engine revisions](README.md)

| Workload | Unit | Okojo before | Okojo after | QuickJS | Ekuma | V8 jitless | Paired change |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| array_for_of | ns | 2337.1 | 1712.2 | 2641.9 | 1045.4 | 2134.5 | −25.9% |
| local_destruct | ns | 246.0 | 178.9 | 301.2 | 168.6 | 161.1 | −28.0% |
| global_destruct | ns | 248.3 | 183.4 | 281.0 | 150.0 | 168.6 | −26.1% |
| global_destruct_strict | ns | 252.6 | 188.3 | 279.6 | 150.5 | 168.5 | −25.4% |
| Richards | ms | 3.046 | 3.024 | 2.674 | 2.897 | 1.419 | 0.0% |
| Decrypt | ms | 81.375 | 83.143 | 63.400 | 61.300 | 47.000 | +0.8% |
| Splay | ms | 2.992 | 2.984 | 1.314 | 1.458 | 0.631 | +0.7% |
| NavierStokes | ms | 55.000 | 57.545 | 33.833 | 45.846 | 44.923 | +5.0% |

The indexed iterator improved all four iteration cases in all three
pairs. NavierStokes slowed in each pair. Both builds emitted identical
JavaScript bytecode for that workload.
[Iterator semantics, prototypes and native-code findings](../../docs/performance/OKOJO_QUICKJS_MICROBENCH.md#synchronous-iteration-review)

## Array destructuring

Three rotating fresh-process rounds used a 1,100 ms warm-up and a
720 ms sample target on Windows x64. Each engine ran the same pinned
QuickJS release and V8 v7 inputs. Times are medians per supplied
`n` for the microbenchmarks and per invocation for v7. Changes use
median paired Okojo time ratios against `d8a5a9b`.
[Harness and engine revisions](README.md)

| Workload | Unit | Okojo before | Okojo after | QuickJS | Ekuma | V8 jitless | Paired change |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| local_destruct | ns | 641.0 | 244.7 | 301.5 | 167.6 | 162.0 | −62.3% |
| global_destruct | ns | 645.5 | 245.1 | 279.5 | 148.7 | 165.8 | −62.1% |
| global_destruct_strict | ns | 638.3 | 249.3 | 276.8 | 150.1 | 165.4 | −60.6% |
| array_for_of | ns | 2273.5 | 2280.9 | 2570.7 | 1034.1 | 2110.0 | +0.8% |
| Richards | ms | 3.029 | 3.033 | 2.669 | 2.888 | 1.411 | +0.7% |
| Decrypt | ms | 82.000 | 82.556 | 62.750 | 62.167 | 47.188 | +0.7% |
| Splay | ms | 2.969 | 3.019 | 1.328 | 1.489 | 0.631 | −0.1% |
| NavierStokes | ms | 58.000 | 55.357 | 34.182 | 46.188 | 45.062 | −4.7% |

Okojo finished the three destructuring workloads in less time than
native QuickJS, at 1.46–1.66× Ekuma's median times. The focused trace
went from 128 to 56 bytecode dispatches.
[Iterator semantics, allocation and native-code findings](../../docs/performance/OKOJO_QUICKJS_MICROBENCH.md#array-destructuring)

## QuickJS microbenchmark follow-up

The bundled QuickJS 2026-06-04 script completed 70 of 71 functions
across all engines in two screening rounds. The common-case
geometric means relative to Ekuma were 2.008× for Okojo, 0.874× for
native QuickJS and 0.483× for V8 jitless. Okojo's `date_parse` run
failed at the legacy `Date.prototype.toGMTString` alias.
[Suite coverage and native profiles](../../docs/performance/OKOJO_QUICKJS_MICROBENCH.md)

The table uses the pinned V8 v7 and Ekuma microbenchmark inputs.
Three rotating pairs used a 1,100 ms warm-up and a 720 ms sample
target on Windows x64. Times are medians per call for v7 and
per unit of supplied `n` for microbenchmarks. Changes use paired
time ratios. Okojo ran at `bd2203e` before and `eede2b9` after.

| Workload | Unit | Okojo before | Okojo after | QuickJS | Ekuma | V8 jitless | Paired change |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Richards | ms | 3.117 | 2.996 | 2.691 | 2.880 | 1.397 | −4.4% |
| Decrypt | ms | 82.556 | 82.333 | 62.000 | 61.833 | 47.250 | −0.3% |
| Splay | ms | 3.014 | 2.944 | 1.323 | 1.498 | 0.629 | −2.6% |
| NavierStokes | ms | 55.500 | 58.077 | 33.500 | 45.625 | 45.125 | +3.5% |
| array_prop_create | µs | 25.21 | 17.81 | 10.04 | 18.39 | 13.42 | −29.9% |
| array_push | µs | 26.92 | 15.97 | 14.25 | 21.12 | 7.72 | −41.0% |
| array_pop | µs | 25.79 | 16.10 | 32.08 | 19.78 | 8.05 | −37.6% |
| string_concat | ns | 960 | 469 | 720 | 735 | 199 | −51.1% |
| int_to_string | ns | 207 | 73 | 74 | 99 | 27 | −64.9% |
| float_toString | ns | 354 | 372 | 568 | 286 | 106 | +3.9% |
| JSON round-trip control | ms | 11.470 | 11.042 | 16.386 | 8.765 | 5.865 | −1.8% |
| Dromaeo cube control | ms | 2.158 | 2.106 | 1.453 | 1.439 | 1.437 | −2.4% |

## V8 v7 object-storage follow-up

Three rotating fresh-process rounds used a 1,100 ms warm-up and an
800 ms sample target. Times are median milliseconds per invocation.
The baseline includes the two review fixes at `572ca73`. The updated
build also shares read-only literal buffers and allocates polymorphic
cache entries per active site. Engine versions and host match the
preceding v7 comparison. Dromaeo cube is a separate control.
[Object-storage review and allocation measurements](../../docs/performance/OKOJO_SPLAY_OBJECT_STORAGE.md#follow-up-review)

| Workload | Okojo review baseline | Okojo updated | Ekuma | V8 jitless | Paired Okojo change |
| --- | ---: | ---: | ---: | ---: | ---: |
| Splay | 2.968 | 2.957 | 1.443 | 0.614 | −0.4% |
| Decrypt | 88.600 | 80.800 | 60.214 | 45.722 | −9.1% |
| NavierStokes | 53.533 | 54.133 | 44.944 | 43.842 | +1.2% |
| DeltaBlue | 7.087 | 7.078 | 4.881 | 2.219 | −1.1% |
| RayTrace | 28.714 | 28.172 | 19.442 | 9.765 | −1.9% |
| Richards | 2.951 | 3.019 | 2.828 | 1.368 | +2.3% |
| Dromaeo cube | 2.094 | 2.095 | 1.412 | 1.393 | +0.7% |

## V8 v7 Splay allocation pass

Four rotating fresh-process rounds used a 1,000 ms warm-up and a
750 ms sample target on Windows 11 with an Intel Core i7-13700F.
Times are median milliseconds per workload invocation. Changes use
the median of paired Okojo time ratios. Ekuma is `41a55ec` with Go
1.27.4 and V8 jitless is Node 26.12.1. Okojo ran on .NET 10.0.14.
[Allocation profiles and storage prototypes](../../docs/performance/OKOJO_V7_SPLAY_STORAGE.md)

| Workload | Okojo baseline | Okojo | Ekuma | V8 jitless | Paired Okojo change |
| --- | ---: | ---: | ---: | ---: | ---: |
| Richards | 3.068 | 3.136 | 2.921 | 1.418 | +2.2% |
| DeltaBlue | 7.192 | 7.112 | 5.058 | 2.294 | −0.7% |
| Encrypt | 4.467 | 4.503 | 3.365 | 2.520 | +0.7% |
| Decrypt | 83.061 | 82.250 | 61.192 | 46.382 | −1.7% |
| RayTrace | 38.209 | 40.117 | 19.563 | 10.113 | −1.1% |
| Earley | 5.753 | 5.597 | 4.040 | 1.359 | −3.1% |
| Boyer | 115.500 | 112.929 | 70.909 | 26.736 | −3.1% |
| RegExp | 71.909 | 71.136 | 35.143 | 17.026 | −1.9% |
| Splay | 5.029 | 4.666 | 1.478 | 0.629 | −6.1% |
| NavierStokes | 52.967 | 53.067 | 46.300 | 45.149 | +0.2% |
| Dromaeo cube | 2.195 | 2.160 | 1.434 | 1.426 | −1.6% |

The v7 geometric mean of paired Okojo time ratios was 0.985× the
baseline, 1.568× Ekuma, and 2.976× V8 jitless. Dromaeo is outside
that mean. Splay, Earley and Boyer improved in all four pairs.
Richards slowed in all four. RayTrace had one 13.9% slower pair
and three faster pairs, with a higher unpaired median.

## V8 v7 suite

Median milliseconds per workload invocation across three fresh-process
rounds with rotating engine order. Each engine ran the same pinned V8 v7
JavaScript with a 1,000 ms warm-up and a 750 ms sample target. Timings cover
warmed execution on Windows 11 with an Intel Core i7-13700F.
[Harness and reproduction steps](README.md)

| Workload | Okojo | Ekuma | V8 jitless | QuickJS (C) | V8 JIT | Jint |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Richards | 3.208 | 2.894 | 1.412 | 2.689 | 0.049 | 9.173 |
| DeltaBlue | 7.223 | 5.087 | 2.296 | 5.679 | 0.039 | 17.356 |
| Encrypt | 4.713 | 3.422 | 2.527 | 3.309 | 0.089 | 20.342 |
| Decrypt | 86.222 | 61.462 | 46.938 | 62.667 | 1.496 | 374.000 |
| RayTrace | 34.522 | 19.375 | 10.067 | 27.852 | 0.403 | 78.700 |
| Earley | 5.747 | 4.060 | 1.361 | 4.905 | 0.125 | 18.816 |
| Boyer | 111.857 | 70.455 | 26.679 | 77.400 | 2.099 | 329.000 |
| RegExp | 73.000 | 35.286 | 16.913 | 120.143 | 5.591 | 69.727 |
| Splay | 4.714 | 1.467 | 0.629 | 1.297 | 0.125 | 6.056 |
| NavierStokes | 61.462 | 45.588 | 45.333 | 33.783 | 2.292 | 283.667 |
| **Geometric mean relative to Okojo** | **1.000×** | **0.626×** | **0.330×** | **0.725×** | **0.020×** | **2.631×** |

The final row takes the geometric mean of the per-workload median paired
time ratios. Lower is faster.

- Both .NET runners used .NET 10.0.14 in Release mode. Okojo was built
  at `72cae11` (engine code through `983688a`). Jint `4.18.0` evaluated
  the prepared script with its default engine settings.
  [Jint package](https://www.nuget.org/packages/Jint/4.18.0)
- Ekuma `41a55ec` was built with Go 1.27.4.
- QuickJS `3d5e064` uses the native C executable.
- Both V8 columns use Node 26.12.1. The jitless column sets `--jitless`
  and the JIT column uses Node's default settings.

## Okojo integration versus main

Okojo `983688a` includes the retained performance changes and regression fixes
from `codex/ekuma-benchmark-study`. The table compares it with `main`
(`ff04298`) and the original study build (code through `741c9e1`).
All three ran the same pinned JavaScript on .NET 10.0.14 in Release mode.
Three fresh-process rounds rotated engine order on Windows 11 with an
Intel Core i7-13700F. Each round used a 1,200 ms warm-up and an 850 ms sample target.
[Harness and reproduction steps](README.md)

Times are median milliseconds per workload invocation. The final column is
the median of three paired integration-to-main time ratios.

| Workload | Main | Study | Integration | Integration / main |
| --- | ---: | ---: | ---: | ---: |
| Richards | 5.694 | 3.239 | 3.254 | 0.568× |
| DeltaBlue | 14.311 | 6.944 | 7.123 | 0.491× |
| Encrypt | 5.810 | 4.659 | 4.676 | 0.805× |
| Decrypt | 106.375 | 86.300 | 86.400 | 0.813× |
| RayTrace | 243.000 | 35.560 | 34.385 | 0.142× |
| Earley | 8.316 | 5.738 | 5.718 | 0.694× |
| Boyer | 188.200 | 115.750 | 113.875 | 0.614× |
| RegExp | 335.667 | 73.917 | 74.000 | 0.220× |
| Splay | 5.551 | 4.668 | 4.802 | 0.836× |
| NavierStokes | 92.700 | 62.429 | 62.429 | 0.668× |

The geometric mean of paired time ratios was 0.517× main
and 1.001× the study build.

The integration compiler emits identical disassemblies for all ten
workloads. An earlier compiler helper extraction slowed Encrypt and
Decrypt by 20.0% and 17.8% in an isolated three-pair screen. The
integration history retains the earlier compiler implementation.
