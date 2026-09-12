# One binding analysis plus lazy nested compilation

Status: `proposed` — highest compiler priority.
Replaces: repeated per-view binding work in `CompilerBindingCollector.cs`,
`JsFunctionCompiler.Compile.cs`, and `JsCompilerBase.Scope.cs`; eager
compilation of every nested unit (see
`../performance/reports/OKOJO_FRONTEND_ALLOCATION_PROFILE_20260830.md`).

Related active input: `../performance/OKOJO_VM_DISPATCH_REDUCTION_PROPOSALS.md`.

## A. Resolve bindings once, retain the result

Collection traverses nested bodies, nested compilation re-collects, emission
re-searches scopes and binding lists, and capture preparation re-sweeps
active bindings — overlapping views of one semantic fact. Build a single
indexed product instead:

```text
ReferenceId → BindingId
BindingId   → declaring scope, flags, capture requirements
ScopeId     → parent, function owner, environment requirements
FunctionId  → parameters, captures, nested functions, source range
```

Storage planning assigns registers, context slots, and module cells from it;
emission consumes resolved accesses. Two structural consequences:

- **Explicit environment structure.** Parameter, body, class, private-name,
  and module scopes become direct relationships, replacing emission-time
  exclusions ("ignore these body bindings while emitting parameter
  initializers") with correct scope resolution.
- **Per-child capture descriptions.** Each function receives exactly the
  bindings it captures instead of a rebuilt name-keyed view of all outer
  bindings.

This is an enabler, not a claim that collection dominates CPU — the
allocation profile says it does not on the measured corpora. Keep the good
frontend work (expression contexts, shared source, collection reuse, lazy
prototype feedback); gains come from fewer units and less repeated semantics,
not more pooling.

## B. Defer nested-function bytecode compilation

Strongest compiler-performance bet: 462 compiled units for the linq-js
corpus, each paying binding work, emission, materialization, and
registration. Implementation order:

1. Parse and validate the complete source (early errors stay early).
2. Retain compact function descriptors plus binding/capture summaries.
3. Compile the entry function and deliberately selected eager functions.
4. Compile remaining bodies on first invocation, then reuse.

Constraints, both hard:

- Defer **compilation**, never observable semantics: closure identity, name,
  length, environment, source text, hoisting, and initialization behave as
  today. Invalid syntax in a never-called nested function still fails at
  parse time — validation and scope facts separate from body compilation
  (the usable lesson of V8's lazy parsing; its parser architecture need not
  be copied).
- Do not retain borrowed pooled AST storage indefinitely: either keep a
  compact owned representation for deferred bodies or keep source ranges,
  parse context, and summaries and reparse on demand. Benchmark both.

Measure startup, first invocation, steady state, retained memory, and the
"every function eventually runs" case separately — otherwise the first call
hides the deferred work.

## Acceptance gate

Entry compile emits identical bytes for unchanged sources; deferred bodies
compile at most once and reuse after; early-error suite passes without
invoking anything; startup/first-call/steady-state numbers reported
separately with retained-memory figures.
