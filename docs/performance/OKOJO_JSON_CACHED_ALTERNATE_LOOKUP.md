# E021: cached JSON alternate lookup

Caching Dictionary<string,int>.AlternateLookup<ReadOnlySpan<char>> once in
AtomTable was tested against production 01809e8 and a freshly rebuilt E019-A.
The candidate restored E019-A property-name reuse and changed only view acquisition:
a readonly field initialized in the AtomTable constructor serves subsequent lookups.

Five alternating fresh-process pairs, 1,000 warmups and 500 samples per process,
Release net10.0, Windows x64 i7-13700F, .NET 10.0.12, ReadyToRun disabled and tiered
PGO enabled gave these medians of process medians:

| Comparison | A ns | B ns | B change |
|---|---:|---:|---:|
| Original vs cached fixed JSON parse | 169,600 | 213,600 | +25.94% |
| Fresh E019-A vs cached fixed parse | 216,500 | 210,300 | -2.86% |
| Original vs fresh E019-A fixed parse | 169,100 | 218,700 | +29.33% |

These are separate comparisons, not pooled measurements. Cached escaped-name
and numeric-name controls regress 6.18% and 11.60%; stringify changes +0.86%.
Fixed parse allocation falls 453,600 -> 353,600 B for both reuse variants.
The scoped gate failed, so no new full JSON/Jint comparison was run.

FullOpts ParseString shrinks 1,125 -> 1,064 native bytes relative to the replay,
removing CORINFO_HELP_ISINSTANCEOFANY. Original remains 961 bytes. The span
lookup IL shrinks 22 bytes / one local -> 14 bytes / zero locals. Hash lookup
and subsequent string InternNoCheck remain. These diagnostic captures do not
establish the native tiers used in the separate PGO timings.

Decision: reject and restore both production files. Retain two new growth and
Unicode-name regression cases in JsonAtomLookupGrowthTests.cs, alongside E019's
four cases. Node v25.5.0 agrees; emitted bytecode uses the expected CallProperty.
Focused restored-source tests: 6 passed. Full restored Release suite: 2,279 passed,
4 skipped, zero failures. Candidate had the same result.

## Next prototype boundary

Design a typed append-only atom index: an eight-byte hash + atom-ID slot array,
with canonical strings held by the existing reverse list. Compare linear probing
at several load factors before considering more complex probing. Preserve stable
predefined IDs, ordinal UTF-16 equality, numeric-key classification and symbol
storage. Use consistent randomized string/span hashing and test forced collisions,
growth, overflow and allocation-failure consistency.

More importantly, let JSON return the usable atom/index directly from key parsing,
avoiding span -> canonical string -> second dictionary lookup. A private packed
ulong return is a hypothesis to compare against E019-B's out-parameter shape.
No custom table is implemented or claimed faster by this experiment. Gate it on
lookup hits/misses, startup, retained memory, growth, Unicode, mixed workloads,
then JSON and non-JSON engine controls. Keep the current dictionary until it wins.

Follow-up: [general atom-index design](OKOJO_ATOM_INDEX_DESIGN.md) broadens the
comparison beyond JSON and corrects ownership to JsAgent (shared by its realms).
The replacement is unimplemented; E021 results above are unchanged.
