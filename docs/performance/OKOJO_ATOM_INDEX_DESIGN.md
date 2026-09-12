# General atom-index design after E021

Design only, 2026-09-12, inspected baseline 7217b92. No replacement implemented
or new performance result. E021's cached alternate lookup remains rejected.

## Scope and decision

Design for all AtomTable consumers, not one repeated JSON workload. The primary
target is an append-only index mapping ordinal names to stable atom IDs, with
canonical strings owned by the existing dense reverse store. Compare grouped
fingerprint/ID slots, scalar full-hash/ID slots and compact chaining. Keep the
production Dictionary unless broad measurements support replacement.

No hard-coded names, record widths/order, payload detection or benchmark-specific
dispatch. JSON key-sequence prediction is a separate deferred idea and must not
determine selection of the general table.

## Source contracts

- JsAgent constructs AtomTable; JsRealm.Atoms delegates to Agent.Atoms. Ownership
  is per agent, shared across its realms. Earlier realm-owned wording was wrong.
- This revision has 462 predefined named atoms. Preserve their IDs, insertion
  order for new names, reverse lookup and separate negative symbol IDs.
- Canonical array indices stay outside named interning. No deletion/tombstones
  are required in the current append-only table.
- Preserve string callers and their reference-identity equality shortcut. Provide
  span lookup/interning internally without copying on hits. Null string behavior
  must not silently become empty-span behavior.
- On a miss, reuse the search position for insertion unless growth invalidates it.
  Keep string values, object property layout and compiler/VM ABI outside this work.

## Candidate structures

| Candidate | Storage | Question to resolve |
|---|---|---|
| Compact chain | bucket heads, dense hash/next per atom | Can compact metadata outweigh dependent chain loads? |
| Scalar open table | eight-byte full hash + atom ID slot | Does simple probing win common hits and growth? |
| Grouped ID-only | byte fingerprint array + int atom array | Can vector filtering and smaller footprint win despite indirect name loads? |

All retain the reverse name store. Grouped full-hash or dense per-atom hashes are
follow-ups if rehashing long strings on growth is costly. Array layout estimates
are not measured retained-memory savings.

The grouped proposal uses aligned groups of 16, power-of-two group count,
seven-bit occupied fingerprints and a distinct empty marker. Test candidates
before using emptiness to terminate lookup; probe groups with bounded triangular
steps. No deletion states or unsafe tail overreads. Full equality is mandatory.
Start at 0.75 maximum occupancy and compare alternatives before choosing policy.

Use consistent string/span hashing. A public randomized hash is not necessarily
the same cost as Dictionary's internal ordinal default. Run both matched-hash
layout comparisons and actual production comparisons. Collision pressure needs
a bounded cold path, such as an ID-preserving dictionary fallback, whose costs
are part of the candidate. No worst-case constant-time claim.

## Required evidence

Measure canonical-reference string hits, equal-content string hits, spans,
misses, growth and startup; vary table size, key length, Unicode, locality and
hit ratio. Include forced hash/fingerprint collisions, stable IDs, symbols,
same-agent sharing and separate-agent isolation. Preserve failure consistency
across allocation/growth; never publish an ID without its canonical name.

Screen isolated implementations before changing the engine. Inspect hash/equality
dispatch, dependent loads, bounds checks, SIMD lowering, spills and native size.
Then integrate only AtomTable internals and run compilation, property lookup,
dynamic-name creation, module/host name lookup, JSON and startup workloads.
Allocation, retained memory and resize peaks are separate quantities.

Predeclare tolerances from baseline repeatability and report each category.
Do not average away a reproducible regression or select only the six-key case.
Retain full test workflow and frozen alternating-process benchmark discipline.

A later dedicated JSON span-to-atom parser is an independent consumer change.
Preserve current intern-after-value order first; eager interning can change new
ID assignment and retention after malformed input. E019-B already removed the
second lookup on hits and still regressed, so one fewer lookup alone is not proof
of a faster parser.
