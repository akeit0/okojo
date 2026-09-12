# E019 JSON property-name reuse: rejected

Two measured candidates against dc7e024 use ordinal span lookup to avoid
allocating names already interned in the realm. A returns the existing string;
B also carries its atom ID into object construction. Both save 100,000 bytes per
500-record fixed parse and roughly 3.2 MB in json-parse-modern, but fixed-parse
PGO time regresses 30.16% (A) and 20.39% (B). Numeric and escaped controls regress.

A full workload regresses 13.73%; B's full workload has extreme pair variance
(-36.03% to +67.91%) and cannot support a speed claim. Both production changes
were reverted. Four JsonPropertyNameReuseTests remain. Node v25.5.0 agrees;
each candidate passed full Release suite 2,275 passed, 4 skipped.

80 timing processes total, A five pairs/case and B three; five workloads, 500
warmups and 200 samples, tiering/PGO on, ReadyToRun off. i7-13700F, .NET 10.0.12,
SDK 11.0.100-preview.5.26302.115, no affinity. Captures ran separately.
ParseString FullOpts code: baseline 961 B, A 1,125 B, B 1,145 B. Native evidence
shows comparer type checking and alternate-lookup calls. The allocation reduction
does not establish a performance improvement. The standalone text-only E019
record preserves exact patches, workloads, all rounds, IL/native excerpts and
hashes. Possible future experiment: cache the alternate lookup view, measured
separately; this has not been attempted.
