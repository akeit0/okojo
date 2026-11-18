# JavaScript engine comparison

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
