# E023: atom hash cost and grouped lookup calls

Baseline a092c58; standalone experiment only. No engine implementation changed.
This continues E022 across the same broad size/corpus/lookup cases. It adds three
attribute-only candidates: inline Mask, inline Hash, and both. The hash algorithm,
capacity, index layout, equality and collision behavior remain unchanged.

All four builds passed the scoped differential checks after formatting and built
with zero warnings/errors. Six rotating rounds compare two Dictionary controls,
fresh grouped base and three candidates: 36 processes, 54 cases each. Six further
processes measure four hash paths at five UTF-16 lengths. Total: 42 processes and
2,064 compact case medians. All measured lookup/hash batches allocate zero bytes.

Environment: Windows x64 i7-13700F, .NET 10.0.12, SDK
11.0.100-preview.5.26302.115, Release, unpinned CPU, ReadyToRun disabled, tiered
PGO enabled and explicit prewarm. Timed runs disable disassembly.

## Findings

| 462 short names, ns/op | Dictionary | Group base | Mask | Hash | Both |
|---|---:|---:|---:|---:|---:|
| Canonical-reference hit | 3.38 | 6.32 | 6.36 | 6.53 | 6.24 |
| Equal-content string hit | 6.02 | 8.41 | 8.66 | 8.69 | 8.47 |
| Span hit | 7.58 | 8.35 | 8.46 | 8.59 | 8.37 |

Inlining attributes do not close the general lookup gap. FullOpts Mask inlining
removes two helper calls but expands Locate 423 -> 590 B. Separate Tier1 captures
show the base already contains zero Mask calls in Locate and both Find overloads.
Base/mask Tier1 sizes match: Locate 1,043 B, string Find 1,544 B, span Find 1,366 B.
This is not a claim that every instruction or profile is identical.

Hash IL is 18 bytes/zero locals and is already inlined in captured FullOpts Find;
the explicit attribute changes no captured Find size. Small timing changes are
insufficient evidence of a general benefit.

| Hash-only ns/hash | Public span | Dictionary diagnostic span path |
|---|---:|---:|
| 4 UTF-16 characters | 2.69 | 1.98 |
| 32 characters | 14.10 | 4.32 |
| 128 characters | 62.23 | 17.09 |
| 512 characters | 261.89 | 82.68 |

The hash probe reads the actual initial Dictionary comparer once via reflection
outside timing. That private runtime API is diagnostic-only, never used by an
index candidate. The timed paths include different dispatch/guard costs, so these
figures cannot assign an exact portion of a full lookup or JSON regression to
hash arithmetic. They do identify a substantial policy/call-path cost. Public
string and public span hash timings are similar; switching entrances alone does
not fix it.

Decision: reject the attributes as a general optimization and keep production
unchanged. Before another table integration, evaluate hash policy together with
collision-pressure fallback and general string/span/growth behavior. No cheap
unprotected hash, private comparer dependency or JSON-only acceptance criterion.
E022's prototype integration gaps remain; no engine suite was needed for this
standalone change.
