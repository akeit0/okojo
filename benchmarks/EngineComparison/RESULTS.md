# V8 v7 integration results

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

In a separate five-round comparison on the same machine, Jint 4.18.0
used 2.64× the study build’s time by geometric mean. The study build
won nine workloads. Jint won RegExp (69.31 ms versus 72.50 ms).
[Jint package](https://www.nuget.org/packages/Jint/4.18.0)
