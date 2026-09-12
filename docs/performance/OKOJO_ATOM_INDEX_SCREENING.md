# E022: general atom-index screening

Baseline: 2d3cfca. Standalone prototypes only; production unchanged. The general
design was tested using scalar full-hash/ID slots, grouped fingerprint/ID slots
and compact chaining, against production-style and matched-hash dictionaries.
No JSON-specific names or record ordering were used in the implementations.

Three table sizes (462, 4,096, 32,768), three generated corpora (short, Unicode,
long-prefix), five lookup operations and index creation/growth produced 54 cases
per process. Five rotating process rounds per implementation and three protocols
produced 75 processes. Environment: Windows x64 i7-13700F, .NET 10.0.12,
SDK 11.0.100-preview.5.26302.115, Release, ReadyToRun disabled, no affinity pinning.

Initial PGO warmup was insufficient and those rows were retained as diagnostic
data. The repeated PGO protocol explicitly prewarms hits, misses and insertion
for 400 ms per corpus, then allows background compilation time. FullOpts is a
separate comparison, not evidence of identical Tier1 code.

| 462 generated short names, prewarmed PGO | Dictionary | Scalar | Group | Chain |
|---|---:|---:|---:|---:|
| Canonical-reference hit, ns | 3.47 | 5.74 | 6.35 | 6.47 |
| Equal-content string hit, ns | 6.01 | 8.25 | 8.52 | 8.96 |
| Span hit, ns | 7.47 | 7.62 | 8.45 | 8.32 |
| Span miss, ns | 8.28 | 10.91 | 6.67 | 5.98 |
| Calculated index payload, B | 29,828 | 12,288 | 9,216 | 10,240 |

Payload excludes headers and string objects; Dictionary Entry is modeled as
24 bytes. These are synthetic reduced-harness costs, not application performance
or measured retained heap size. All timed lookup batches allocated zero bytes.
Long-name lookup also regresses substantially. A matched randomized-hash dictionary
loses on long keys too, implicating hash/comparer work without isolating an exact
causal percentage. Smaller slot storage alone does not establish a better table.

Differential tests passed for 5,011 ordinary names and 611 collision-stress names,
including case/Unicode/NUL/surrogates, growth, string/span agreement, identity and
stable insertion IDs. Group SIMD and scalar masks were exercised. Both Release
builds had zero warnings/errors. Full engine tests were not run because no engine
code changed. Null contracts, actual predefined/symbol behavior, allocation-failure
injection, collision fallback and real workload weighting remain integration gates.

FullOpts code sizes: Scalar Locate 297 B, Group Locate 423 B, Group Mask 116 B,
Chain FindCore 281 B. Group Mask emits vector comparison/mask extraction but is
called separately for fingerprint and empty checks. These are prototype code-shape
costs; they do not reject the entire data-structure family.

Decision: reject all three current implementations for general replacement. Keep
the reduced-memory designs as prototypes. Next isolate hash policy and dispatch
while preserving ordinary string identity performance and collision behavior.
No deterministic benchmark-friendly hashing or JSON-only acceptance criterion.
