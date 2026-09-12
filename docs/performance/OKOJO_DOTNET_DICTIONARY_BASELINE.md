# .NET Dictionary baseline for AtomTable work

Scope: Dictionary<string,int>(StringComparer.Ordinal), .NET 10.0.12 x64.
This is source inspection and structural verification; no engine change or new
performance claim. The v10.0.12 source tag was inspected. The installed CoreLib
informational commit could not be fetched, so source-to-binary identity is not
asserted; the concrete observations below were checked on the installed binary.

## Baseline facts

- Storage is contiguous entries plus integer buckets and array-index chains,
  not one allocated node per entry. An entry stores hash, next, key and value.
  The measured string/int entry stride is 24 bytes; buckets use four-byte ints.
- Prime-capacity buckets use reciprocal multiplication for remainder on x64.
  Growing through 462 generated names gives capacities 3, 7, 17, 37, 89, 197,
  431, 919. Okojo separately stores canonical references in stringByAtom.
- Recognized ordinal string comparers are wrapped internally with a cheaper
  nonrandomized hash. The public Comparer property exposes the original comparer,
  so invoking its hash externally need not reproduce the table's internal path.
- The initial hash uses two uint accumulators, rotate-left-five/add/XOR updates
  over UTF-16 pairs and a final multiply/add combination. Span tails synthesize
  the string terminator effect without reading past the span.
- Public string/span GetHashCode uses Marvin32 with a runtime-wide seed.
  Collision fallback creates a separately seeded randomized comparer and rehashes
  entries; it is not simply the public string hash copied into the dictionary.
- New insertion after traversing more than 100 chain entries triggers fallback
  under the internal nonrandomized comparer. Ordinary lookup does not trigger it.
  Normal growth can reuse hashes; changing hash policy recomputes them.
- AlternateLookup is an eight-byte dictionary-reference view. It shares storage,
  reads the current comparer on use and remains valid through growth/rehashing.
  Acquiring the view checks compatibility; caching it avoids acquisition work,
  not hashing/equality. Caching a private comparer would break that distinction.

## Independently checked transition

A diagnostic probe chooses 102 different full hashes with the same initial bucket
at capacity 239. After 101 insertions the internal comparer is unchanged; insertion
102 switches it to RandomizedStringEqualityComparer without capacity growth.
All 102 values remain accessible by strings and a view cached before the switch.
The result repeats with alternate-span insertion, and the view survives a later
capacity increase. Twelve empty/odd/even/Unicode/NUL/surrogate string/slice pairs
also have matching hashes within each policy. Build and checks pass.

## Consequences for the replacement

E022 changed hash policy as well as layout: its custom tables always used public
randomized hashing. That comparison cannot establish that their storage schemes
are intrinsically worse. Preserve or deliberately justify changing the baseline's
combined fast-hash/collision-fallback policy before judging a replacement layout.

Current InternNoCheck performs TryGetValue then Add on a miss. A replacement can
reuse the miss search, retain canonical strings once and specialize IDs without
discarding the baseline's existing hashing/JIT advantages. A canonical-reference
hit still hashes before equality; an alternate-span hit does not allocate a key.
No private runtime comparer should become a production dependency.
