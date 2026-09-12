# JSON object layout initialization

JSON text and JsonElement conversion create objects in dynamic named-property
mode. Each object already owns an empty `DynamicNamedPropertyLayout`. Bulk
initialization now fills that layout instead of allocating a second layout and
replacing the first.

This is an internal allocation change. Parsing, atom lookup, property order,
duplicate handling and public APIs are unchanged. Entries and values remain
owned by each object; this does not share mutable layouts between objects.

The initializer requires a fresh layout. Up to 15 named properties use the
existing linear representation; larger objects use the existing Swiss map.
Numeric indices remain separate. JSON calls the initializer at the final
staging flush or before its first duplicate-name fallback, before the result
is exposed to script or the reviver.

## Measured allocation

Release/net10.0, .NET 10.0.12 x64, Intel i7-13700F, engine baseline `fc74209`:

| Complete action | Before | After |
|---|---:|---:|
| Construct and initialize six named properties | 408 B | 328 B |
| Parse six numeric-valued named properties | 640 B | 560 B |
| json-parse-modern execution | 15,580,896 B | 14,300,896 B |

The workload parses 500 records 32 times. Removing one 80-byte layout per
record accounts exactly for the 1,280,000-byte reduction (8.22%). The full
workload also includes stringify and traversal; its allocation is not solely
JSON.parse allocation. Empty and indexed-only objects are unchanged.

Three alternating process pairs with tiering/PGO enabled and ReadyToRun disabled
gave overlapping full-workload timing ranges: 12.302–12.720 ms before and
12.244–12.852 ms after. Treat this as an allocation improvement, not a demonstrated
throughput gain. Each action warmed one second before 31 samples; process
medians were compared on logical CPU 0.

A separate three-pair FullOpts series (tiering disabled) gave 12.793 ms before
versus 12.864 ms after, again with overlapping ranges. Direct 64-property
initialization was slower in that series (404.2 to 421.6 ns); complete parsing
was overlapping. The change retains checked layout access and a freshness guard.

Validation: 52 focused JSON tests, then the full suite with 2,291 passes and
four skips. Regression coverage includes the 15/16-property boundary, independent
mutation, duplicate keys, reviver deletion, property ordering and descriptors.

## Wide-object entry-array ownership

The map builder now takes ownership of a fresh dense entry array when its length
already matches the required capacity. Previously it allocated another Entry
array and copied into it. Every caller supplies newly allocated or copied live
entries; no mutable array is shared between objects. When promotion/rehash needs
more capacity, the existing allocation-and-copy path remains in use.

Against baseline `6c8b11b`, direct numeric-valued JSON parsing on .NET 10.0.12 x64
allocates:

| Named properties | Before | After | Saved |
|---|---:|---:|---:|
| 15 | 1,064 B | 1,064 B | 0 B |
| 16 | 1,480 B | 1,328 B | 152 B |
| 17 | 1,544 B | 1,384 B | 160 B |
| 64 | 5,032 B | 4,496 B | 536 B |
| 256 | 19,240 B | 17,168 B | 2,072 B |

For N > 15, this removes the 24-byte array header plus 8*N bytes of entries.
Control/index arrays and value slots are unchanged. The six-property modern
JSON workload receives no additional allocation saving from this step.

Three alternating tiered process pairs measured complete 64-property
initialization at 306.5 -> 285.2 ns and parsing at 3,064 -> 2,944 ns. Parsing at
256 properties had overlapping timing ranges. These measurements use the same
one-second warmup and 31-sample protocol; direct parser timings omit built-in
argument conversion and reviver processing. Do not translate saved bytes into
a proportional parsing speedup.

Validation after this step: 60 focused JSON tests, then 2,299 full-suite passes
and four skips. Tests cover the 15/16/17 boundary and wide-object growth,
compaction back to linear storage, promotion, descriptors and independent mutation.

## Unescaped property-name spans

The text parser now resolves property names in a separate non-inlined helper
before recursively parsing their values. Unescaped names use a span for numeric
index classification and atom interning, avoiding redundant substrings on hits.
New atoms still allocate their canonical string. Backslash-containing names use
the existing decoder, and value-string parsing is unchanged.

Against `a7a69c4`, the modern workload's allocation falls from 14,300,896 to
11,100,896 B (22.38%). Its 16,000 records each previously allocated 200 B of
id/name/active/score/tags/ts name strings. Three process pairs measured median
execution at 11.975 -> 11.321 ms with tiering, and 12.502 -> 11.787 ms with
FullOpts, using the same .NET 10.0.12 x64 environment and one-second warmup.

This has an escaped-name tradeoff. An all-escaped short-name cohort is about
3% slower with tiering and 7% slower with FullOpts, with unchanged allocation.
The helper rescans through the existing decoder on that path. Longer escaped
prefixes have mixed results. A fresh-realm 64-name cohort allocates 6,544 B on
both sides and has overlapping timing ranges. This is not allocation-free
parsing or a speedup for every input distribution.

Interning precedes value parsing, so parent/child first-intern order can change
internal atom IDs. Existing IDs and observable property order remain stable.
Failed parses do not roll back atom interning; the new path may intern a name
before a later syntax error. Parsed objects are not exposed before completion.

Validation: 65 focused JSON tests, then 2,304 full-suite passes and four skips.
Coverage includes raw controls, escaped controls, lone surrogates, numeric
boundaries, malformed input, long names, duplicate keys and nested atom growth.
