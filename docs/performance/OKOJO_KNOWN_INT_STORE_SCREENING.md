# E015: known integer heap-store screening (2026-09-12)

Outcome: screened out before a production change. No CopyInt32To helper added,
no A/B speedup claimed, and no engine source or tests changed.

Base revision: `29ada756eeeb3614f81eb305eb22ebe8e25004ac`.
The proposed direct integer-to-VM-register site was not found in the searched
VM paths: integer results go into the local accumulator, and Star already
uses CopyValueTo. A concrete related heap-store site exists in RegExp
replacement callback arguments:

```csharp
replaceArgs[captureCount + 1] = JsValue.FromInt32(matchIndex);
```

This is a fresh callback-argument array, not a VM register or an old-reference
overwrite. Inspecting it tests whether known integer construction leaves
unnecessary reference-copy work for a specialized helper to remove.

## Finding

FromInt32 exposes Obj=null to the JIT. In captured native code, the offset
store is already a payload store followed by a zero object-slot store, with
no assignment-helper call between them. The following inputValue string
store has its own CORINFO_HELP_ASSIGN_REF; that call must not be attributed
to the integer store.

The host lambda is generated as
`Intrinsics+<>c__DisplayClass1038_0.<InstallRegExpConstructorBuiltins>b__42`.
Its IL still contains FromInt32 plus stelem: 1,850 bytes, 68 locals. Native
size is 6,889 bytes with tiering off. A process with tiering and PGO enabled
emitted Tier0-FullOpts (7,185 bytes), not observed Tier1; that code also has
the two scalar writes. This is inspection evidence, not candidate equivalence
or a performance result.

This narrows [E014](OKOJO_REGISTER_STORE_EXPERIMENT.md): arbitrary JsValue
heap assignment retained a reference-assignment helper there, whereas a
known-null object field is already optimized here. A broad rule about all
heap assignments would be incorrect.

## Validation and provenance

SDK 11.0.100-preview.5.26302.115, .NET 10.0.12, Windows x64 10.0.26200,
i7-13700F, Release, ReadyToRun=0, no affinity pinning. FullOpts capture used
250 warmup/25 calls; tiered capture used 1,000 warmup/25 calls. Capture
timings are not treated as benchmarks.

The input replaces `/([a-z])(\d)/g` in `a1 b2 c3` with a callback. Guards
check offset types, input string, output `1a 2b 3c`, and offset sum 9. Node
v25.5.0 and Okojo passed. Node is the built-in reference; no intentional
semantic difference or execution mismatch was observed. Okojo bytecode was
inspected; no mismatch-specific VM trace was needed.

Raw identifiers: `20260912-e015-known-int` and bytecode snapshot
`20260912-145843/e015-replace.disasm.txt`. No source change required a full
test suite. Probe and inspection utility builds had no warnings/errors.
Integer-boundary and old-reference tests were not added because the candidate
was stopped at the promised baseline-codegen gate.
