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
