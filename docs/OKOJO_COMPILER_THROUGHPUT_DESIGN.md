# Compiler Throughput Design - Research and Proposals

Research into how V8, Roslyn, and other production compilers optimize
their compilation pipelines, mapped to concrete Okojo proposals.

## Current Baseline (linq-js 34KB minified)

| phase | time | share |
|---|---|---|
| Parse (lexer + AST construction) | ~2.85ms | 17% |
| Compile (AST -> bytecode) | ~5.66ms | 83% |
| Total | ~8.5ms | |

Post-lock-removal trace hotspots:

| hotspot | exclusive | root cause |
|---|---|---|
| PollGC | ~30% | AST records, hashsets, temp lists |
| MarkCapturedNames... | ~2.7% | capture analysis walk |
| CastHelpers.IsInstanceOfClass | ~4.5% | `is` type dispatch |
| Monitor.Enter_Slowpath | ~2.4% | residual locks outside pool |

## 1. V8: Zone Allocation + Lazy Parsing

### Zone allocation

V8 allocates ALL AST nodes, scopes, and parser state in a Zone (bump
allocator). Memory comes from contiguous blocks; individual nodes are
never freed individually. The entire zone is discarded in O(1) when
compilation finishes.

Our AST uses C# record classes, each allocating individually on the GC
heap. For 34KB producing thousands of nodes plus hashsets/lists, this
creates Gen0 pressure. Trace shows PollGC at 30%.

### Lazy parsing + preparser

V8 pre-parses function bodies it encounters during top-level parsing:
validates syntax without building an AST. When the function is called,
it fully parses on demand. This avoids compiling code that never runs.

The preparser tracks variable declarations/references across scopes so
closure allocation decisions are correct. Preparse data is serialized
so inner functions are not re-preparsed when the outer function is
finally compiled.

## 2. Roslyn: Green/Red Trees + Pooled Nodes

### Green/red separation

Roslyn splits syntax into two layers:
- Green tree: immutable, persistent, no positions/parents, built bottom-up.
  Tracks only relative widths. Heavily shared across edits and files.
- Red tree: lazy facade providing positions and parents, built on demand.

Green nodes with <=3 children are cached in a 65K-entry intern table
(55% hit rate on the Roslyn codebase itself). Common trivia patterns
(single space, newline, indentation) are pre-cached singletons.

### List optimizations

- Empty list = null (zero allocation)
- Singleton = parent points directly at child (no list node)
- Small lists have specialized implementations

### Impact

Tokens, trivia, and lists constitute 75%+ of tree elements but incur
ZERO red allocations. Combined with green sharing, the syntax layer
allocates very little under normal usage.

## 3. Mapped to Okojo: Current Pain Points

Our compile pipeline: Lexer -> Parser (AST records) -> JsCompiler (bytecode).

Pain points mapped to research findings:

| symptom | root cause | V8/Roslyn solution |
|---|---|---|
| PollGC 30% | per-node GC allocation for AST records | Zone/arena allocation (V8) |
| MarkCapturedNames ~7% | repeated identifier walks per nested function | Precompute in parser pass (V8 preparser variable tracking) |
| CastHelpers.IsInstanceOfClass 4.5% | `is JsIdentifierExpression x` type dispatch | Specialized node types / visitor pattern with enum dispatch |
| Monitor.Enter residual 2.4% | InstallIntrinsics + realm init locks | Pre-warm outside measurement |
| Array.Copy 22.7% inclusive | collection growth during compile | Pooled builders (Roslyn list optimizations) |

## 4. Proposals

### C1: AST Arena Allocator

Introduce an arena allocator for AST nodes. Nodes are allocated
contiguously; no individual GC tracking. The arena is discarded when
compilation completes.

Requirements:
- ArenaAllocator class (bump pointer over pooled blocks)
- AST nodes hold no unmanaged resources (already true)
- No finalizers on nodes (already true)
- Nodes may hold references to strings (those outlive the arena via
  interning, or are also arena-allocated)

Impact: eliminates PollGC pressure from AST construction.
Risk: medium - requires changing how AST nodes are created but not
how they are consumed.

### C2: Single-Pass Compile (Merge Capture Analysis)

Currently: parse -> compile (which internally does capture analysis
via PrecomputeDirectChildCaptures then AssignCurrentContextSlots).

V8 merges this into the parser/preparser. The parser tracks variable
declarations and references as it goes, so by the time compilation
starts, capture information is already known.

Proposal: move capture marking into the parser pass. When the parser
sees an identifier reference inside a nested function scope, it marks
the outer binding immediately. This eliminates the separate
PrecomputeDirectChildCaptures walk entirely.

Impact: removes MarkCapturedNamesReferencedByNestedFunction (~7%)
and MarkDirectCapturesFromNestedFunction overhead.
Risk: high - requires restructuring parser/compiler boundary.

### C3: Type Dispatch Optimization

The compiler uses pattern matching (`is JsIdentifierExpression id`)
throughout VisitExpression/VisitStatement. Each check is a
CastHelpers.IsInstanceOfClass call.

Options:
a) Add a NodeType enum to each AST node; switch on enum instead of
   type testing. Enum comparisons are direct integer compares.
b) Use a visitor pattern with virtual dispatch instead of
   type-testing chains (JIT can devirtualize sealed classes).
c) Make AST node classes sealed so the JIT can devirtualize
   `is` checks into simple type handles.

Option (c) is zero-risk and can be done incrementally.
Option (a) is the highest impact but requires touching every node.

### C4: Collection Pool Warm-Up

The pool still allocates on first use per type per realm. For a fresh
realm compiling linq-js, the first RentList/RentDictionary call allocates
new collections. Pre-warming common types at realm creation would avoid
this cold-start cost.

### C5: Lazy Function Compilation

V8 pre-parses function bodies without building ASTs, deferring full
compilation until the function is actually called. This is the single
biggest throughput optimization in V8's pipeline.

For Okojo: when the compiler encounters a function declaration/expression,
it could emit a CreateClosure placeholder and store the source range +
scope metadata. The function body is compiled lazily on first invocation.

This is a major architectural change requiring:
- Source range tracking in bytecode functions
- A lazy compilation entry point
- Scope/variable metadata serialization (like V8 PreparseData)

## 5. Priority and Dependencies

| ID | proposal | impact | effort | dependency |
|---|---|---|---|---|
| C3c | Seal AST classes | low risk, incremental | small | none |
| C1 | AST arena allocator | high (kills 30% GC) | medium | C3c recommended first |
| C4 | Pool warm-up | low-medium | trivial | none |
| C2 | Single-pass capture analysis | medium-high | large | C1 (arena helps) |
| C5 | Lazy function compilation | very high (architectural) | very large | C2 |

Recommended order: C3c -> C1 -> C4 -> C2 -> C5
