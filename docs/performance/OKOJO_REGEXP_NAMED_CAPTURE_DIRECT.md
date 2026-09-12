# E017: direct named-capture objects for functional replacement

Accepted against `7ff17f7ec6ecaa2460cbab655ab8add73178601e` (E016 included).

Intrinsic functional RegExp replacement previously built a per-match C#
named-value dictionary, then enumerated it to create the callback's JavaScript
groups object. It now skips that dictionary and populates a fresh groups object
from the existing capture-string array and compiled name/index metadata.

## Boundaries

- `ExecMatchResult` and `RegExpEngine.Exec` default to materializing named values.
  Only intrinsic functional replacement opts out.
- Custom exec preserves the returned groups object's identity.
- Normal exec and string-template replacement retain their previous data path.
- `/d` group-index arrays and named-index dictionaries remain unchanged.
- Every callback gets a distinct null-prototype groups object with the same
  property order and writable/enumerable/configurable data properties.
- Null capture strings mean unmatched; empty strings mean successful empty
  captures. Duplicate names resolve to the first participating capture, matching
  the old range-based selection. Compiled name keys are unique.
- This applies to any functional callback kind, including non-global replacement.
  E016's narrower argument-buffer reuse eligibility is unchanged.
- All modified types and methods are internal engine implementation, not stable
  embedding API additions. No compiler, bytecode or frame ABI changes.

## Evidence

Windows x64, i7-13700F, .NET 10.0.12, SDK 11.0.100-preview.5.26302.115, Release.
ReadyToRun disabled, default GC, no affinity pinning. FullOpts screening:
three alternating A/B pairs per case, 1,000 warmups and 200 samples. PGO gate:
seven alternating pairs, 10,000 warmups and 2,000 samples. Six cases, 120 timing
processes total; builds and native captures were separate.

| Complete 64-match case | Bytes before -> after | PGO median ns before -> after | Change |
|---|---|---|---|
| Optional named capture | 38,504 -> 21,096 | 25,300 -> 22,600 | -10.67% |
| Duplicate named capture with /d | 58,584 -> 41,176 | 26,600 -> 24,500 | -7.89% |

Both save 17,408 B/call; every paired named-case timing improves in both protocols.
FullOpts timing improvements are 11.46% and 9.12%. PGO controls: ordinary captures
-1.20%, non-global -1.89%, custom exec +0.66%, exec/template -0.16%, all with
unchanged allocation. FullOpts exec/template is +1.63%; its PGO paired range is
-10.43% to +13.21%, so exact neutrality is not established.

BuildMatchResult IL grows 403 -> 423 bytes with 21 locals unchanged. FullOpts
native code grows 1,123 -> 1,133 bytes, with an explicit branch around the value
dictionary allocation. The new groups builder uses an array loop instead of
interface enumeration; 79 IL bytes/5 locals versus 96/3 for the dictionary
builder. Extra locals do not predict execution cost. Allocation savings cover
both the dictionary conversion and enumeration change; no component breakdown
or application-wide speedup is claimed.

## Correctness and reference

OkojoBytecodeTool inspected the named workload callback (`groups.tail` property
read and capture comparison); snapshot `20260912-160000/e017-named.disasm.txt`.
Node v25.5.0 agrees with all six new semantic test bodies and workload guards.
No execution mismatch required a VM trace.

`RegExpNamedCaptureReplacementTests` checks object identity, descriptors, null
prototype, optional/empty/duplicate captures, nested replacement and mutation,
named `/d` exec indices, templates, custom exec identity and bound callbacks.
After formatting, focused E016+E017 tests: 11 passed. Full Release suite:
2,268 passed, 4 skipped, 0 failed. Builds have zero warnings/errors. No Test262
run is claimed.

The independent OkojoOptNote Git collection retains E017's exact production
patch, runnable workloads/IL inspector, native excerpts, binary hashes, and all
timing rounds. This repository note is self-contained and has no local external
document dependency.

Possible next experiment: avoid `/d` index materialization specifically where
intrinsic replacement cannot expose it. That requires a separate consumer audit,
correctness boundary and measurement; it is not part of E017.
