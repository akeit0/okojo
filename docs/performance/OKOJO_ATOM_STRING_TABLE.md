# Append-only atom string index

The string side of `AtomTable` uses one dense entry array and an integer bucket
array. Each entry stores the canonical string, its hash and its next link. The
entry position is the positive atom ID, so reverse lookup needs no separate
list. Symbols retain their separate negative-ID representation. Numeric-index
classification and predefined atom IDs retain their existing behavior.

The implementation is in
[AtomStringTable.cs](../../src/Okojo.JavaScript/Execution/AtomStringTable.cs).
This is an internal runtime representation. Its span interning entry point is
internal; it does not extend the stable embedding API. The existing JSON parser
is retained in this adoption.

## Invariants

- Append only; no deletion and no concurrent writer. Callers synchronize access
  through the owning engine/agent as before.
- String hits preserve the canonical string reference. Span hits allocate
  nothing; a span miss creates one canonical string.
- Resizing preserves entry positions and rebuilds bucket links from stored
  hashes. Array references used for append must be obtained after growth.
- Ordinary hashing reads guarded pairs of UTF-16 code units. The final odd
  code unit and empty input never require an out-of-range read.
- A miss that traverses more than 100 entries triggers a fresh per-table Marvin
  seed and rehashes the dense entries. Later growth keeps that seed and the same
  layout. There is no second long-lived Dictionary after randomization.

## Adoption evidence

The 2026-09-12 comparison used .NET 10.0.12 on Windows x64/i7-13700F, three
rotated processes, Release builds, tiered compilation/PGO enabled, ReadyToRun
disabled and call-counting delay disabled after verifying Tier1 generation.
These are controlled throughput measurements, not default startup measurements.

A captured distinct-name corpus, including generated growth-fixture names,
measured 18.136 to 11.253 ns/key for building a table and 272,024 to 154,648 B
allocated per build. Full runtime creation allocated 28,352 B less. Whole-engine
throughput was broadly near neutral with process variation; this change does
not claim a general JSON, RegExp or base64 speedup.

After collision-triggered randomization and growth to 32,768 names, compact
array payload was 655,372 B. A preallocated single-probe Dictionary/List control
reached 1,483,700 B at its actual capacities. These totals exclude headers and
string data. The compact table also reduced build allocation in that comparison.

Array-field caching, span-local caching and range-loop termination were tested
separately. They were not retained: isolated instruction reductions did not
establish a consistent engine benefit. In particular, one range-loop form
removed an entry bounds check but introduced an entry-reference spill on every
chain link. A narrower form avoided that spill but had mixed engine results.

## Validation

[AtomStringTableTests.cs](../../tests/Okojo.Tests/AtomStringTableTests.cs) covers
stable predefined/symbol IDs, arbitrary UTF-16 and interior spans, canonical
references, zero-allocation span hits, actual bucket collisions, and 100,000
additions after randomization. The seeded hash is checked against the runtime's
Marvin implementation in tests only; production uses no private runtime API.

Focused atom/JSON-growth tests passed (7). The full Release suite passed
2,284 tests with 4 skips. The JSON/property/symbol/growth fixture returned true
in Node, and its emitted Okojo bytecode was identical before and after adoption.
