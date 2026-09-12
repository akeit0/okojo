# RegExp named index-pair identity

Node requires `m.indices.groups.x === m.indices[n]` for the corresponding
participating named capture. Okojo previously allocated the named pair twice.
The runtime now selects the participating capture index and reuses that pair.
Duplicate names and unmatched captures follow the compiled capture mapping.

Found while preparing E018 on c97b40d. Frozen baseline reproduced the identity
failure. Three regression cases also cover nested exec, replacement templates,
custom exec indices getters and empty Unicode matches. Node v25.5.0 agrees.
After formatting: focused 3 passed, full Release suite 2,271 passed, 4 skipped.
This correctness prerequisite is committed separately from E018 measurements.
