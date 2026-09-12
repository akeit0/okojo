# E020: rope cache compaction

Retained variant B against af82c63; JSON candidates were reverted before this
baseline. Rope nodes no longer carry a separate cached-flat string field.
Flatten already stores the result in Left and empties Right. Concat never creates
a node with an empty operand, so that terminal state identifies a string in Left.
B uses Unsafe.As<string> only behind the sentinel guard; all constructor sites
were audited. Public JsString signatures, string content and slice semantics are
unchanged. No new concurrent-access guarantees are implied.

## Allocation

The isolated constructor probe measures 48 -> 40 bytes/node. On the actual
dromaeo-string-base64-modern source, allocations fall 2,102,040 -> 1,797,712 B,
saving 304,328 B/execution (14.48%). Fixed encode/decode probes save 35,008/26,064 B.
A 4,096-character append-and-flatten probe saves 32,000 B; cached reads remain
allocation-free. This measures allocated bytes, not retained heap or GC pauses.

## Timing and rejected variant

A used a checked cast after the sentinel. Cached reads regressed consistently:
+6.47% PGO and +3.34% FullOpts. A is rejected and preserved in the independent
text-only experiment record. B removes only that cast check under the same
invariant; cached-read regressions no longer reproduce consistently.

B FullOpts: full base64 +0.87%, encode -0.69%, decode -0.95%, cached reads -1.51%,
append control +1.51%. The append cost repeats in all three pairs and is an
explicit tradeoff. B PGO initial decode +8.90% did not reproduce with longer
warmup: aggregate median -3.11%, paired median +0.64%, paired range -5.99% to +2.80%.
Do not interpret that confirmation as a robust speedup. Full PGO base64 -1.56%
has mixed pairs. The retained change is an allocation optimization with broadly
neutral throughput, not a universal speed gain.

## Evidence and validation

150 timing processes: A PGO five pairs/case, A FullOpts three; B PGO/FullOpts
three each; B decode confirmation five. Five workloads. PGO warmup/sample counts
500/200, FullOpts 100/100, confirmation 2000/500. Every protocol is preserved
separately. Captures and builds were outside timing. Windows x64, i7-13700F,
.NET 10.0.12, SDK 11.0.100-preview.5.26302.115, unpinned, default GC, ReadyToRun=0.
Probe strict=False; these are engine A/B results, not a refresh of the strict,
PGO-disabled Jint comparison table. Full workload random input is unchanged.

TryGetFlat IL: baseline 17 B, A/B 38 B, zero locals; B replaces castclass with
Unsafe.As. Flatten IL: 110 -> 100 B, locals 1 -> 2. Native FullOpts Flatten:
baseline 258 B, A 299 B, B 266 B. Thus smaller allocation, IL size and native size
are distinct measurements. Exact patches, IL/native excerpts, allocation kernel,
binary hashes, workload generator, tests and all timing rows live in the E020
knowledge-collection record without external local links.

After formatting, each variant passed focused RopeFlatCacheTests/JsStringTests
(7 passed) and the full Release suite (2,277 passed, 4 skipped); builds had zero
warnings/errors. New tests cover snapshots, cached identity, append after flatten,
empty concat, slices, surrogate content and mixed-child iteration. Node v25.5.0
passes the five JS workload guards. OkojoBytecodeTool inspected append Add and
charCodeAt; no bytecode ABI changes. No Test262 run or execution mismatch.
