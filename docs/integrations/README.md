# Integrations

Host profiles build on the engine; the engine must not absorb host policy.

- `node/` — Okojo.Node: runtime plan (master design), builtins slice, module
  system slice, package-exports slice, Ink debug workflow. Implementation
  status lives in each note, distinct from desired Node compatibility.
- `dotnet/` — .NET hosting: module resolution (`dll:`/`nuget:`), generated
  overload resolution, dynamic named/indexed host objects.

Boundary rule (from `../architecture/OKOJO_LIBRARY_SPLIT_PLAN.md`): the
engine owns ESM/jobs/seams and a FIFO Promise checkpoint; hosts own task
readiness, fairness, and platform APIs (CJS/require, `process`/`Buffer`,
timers, file loading).
