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
