# Conformance gate for the redesign

Status: `proposed` — the gate that makes the redesign trustworthy.
Concerns `tools/Test262Runner` (cache in `Program.cs`, `Test262Runner.Infrastructure.cs`).

## Why the current cache cannot gate

The runner's passed-test cache stores paths and names the cache file from
the test root rather than the engine build or suite revision. It is a local
continuation aid, not a non-regression proof across compiler/VM changes.

## The gate

A fresh run of the selected baseline: no passed-result skipping, no new
exclusions, no loss of previously passing test variants. Pin the suite
revision and record build/configuration identity. Compare test identities
and execution modes, not just aggregate totals. Preserve negative-test phase
and error type — parse, module-resolution, and runtime failures are distinct
(Test262 INTERPRETING.md), never interchangeable.

## Focused regressions (optimization-sensitive behavior)

Conversion order and count, getters/proxies, prototype mutation, TDZ and
closures, arguments aliasing, generator cleanup, async ordering, module live
bindings, source-text behavior. Bytecode verification and operand-boundary
tests run independently of Test262 coverage.

## Expected churn is not rejection criteria

Regenerate bytecode snapshots. Rewrite API-shape tests. Change debugger data
structures. Those are consequences of the split, not arguments against it.

## Performance side of the gate

Extend existing probes to report compilation phases, first-use compilation,
allocated vs retained memory, bytecode size, register/context counts,
suspension payload, and cold vs warmed feedback. Trade-offs are explicit;
not every microbenchmark must improve.
