# Compiler Throughput Design - Research and Proposals

Research into V8, Roslyn, Rust-based parsers (Oxc), and flat AST designs,
mapped to concrete Okojo improvement proposals.

## Current Baseline (linq-js 34KB minified)

| phase | time | share |
|---|---|---|
| Parse | ~2.85ms | 17% |
| Compile (AST -> bytecode) | ~5.66ms | 83% |
| Total | ~8.5ms | |

Post-lock-removal trace hotspots: PollGC 30%, MarkCapturedNames 7%,
CastHelpers.IsInstanceOfClass 4.5%, residual Monitor.Enter 2.4%.

## 1. V8 Parser Pipeline

Zone allocation (bump allocator) for all AST/scopes/parser state.
Lazy parsing + preparser: function bodies skipped until called.
PreparseData serialization avoids re-preparsing nested functions.
CRTP shared ParserBase eliminates parser/preparser code divergence.

## 2. Roslyn Green/Red Trees

Green tree: immutable, persistent, no positions, built bottom-up.
Red tree: lazy facade providing positions/parents on demand.
Green node caching: <=3 children interned in 65K-entry table (55% hit).
Trivia pooling: common whitespace patterns are singleton instances.
List optimizations: empty=null, singleton=direct pointer, small=specialized.

Impact: tokens+trivia+lists (75% of tree) incur zero red allocations.

## 3. Oxc (Rust) - Fastest JS Parser

Memory arena (bumpalo): O(1) alloc, O(1) dealloc, cache-friendly linear layout.
CompactString: strings <=24 bytes inlined, no heap allocation.
Minimal heap: ONLY arena + CompactString, nothing else heap-allocated.
Separation of concerns: scope binding in semantic analyzer, not parser.
SIMD whitespace skipping in lexer.
Result: 3x faster than swc, 5x faster than Biome, 100% test262.

## 4. Super-Flat AST Pattern

Progressive optimization from jhwlr.io/super-flat-ast:
1. Traditional tree: Box<Expr> + Vec<Stmt> = many small allocations
2. Flat AST: nodes in contiguous array, referenced by integer index
3. + Bump allocation: arena-backed, amortizes allocation cost
4. Super-flat: + pointer compression + string interning
Result: 3x less memory than tree, faster at every scale.

## 5. Mapping to Okojo

Our pipeline: Lexer -> Parser (AST records on GC heap) -> JsCompiler (bytecode).

Key differences from fast implementations:
- AST nodes are individually GC-allocated records (~230KB per compile)
- No arena; GC collects them normally causing Gen0 pressure (PollGC 30%)
- Capture analysis is a separate walk AFTER parsing (V8 does it in preparser)
- No string interning for identifiers beyond the lexer table
- Visitor methods use type-testing chains (`is JsXExpression`)

## 6. Proposals (C-series)

### C1: Arena Allocator - DEFERRED
True arena impractical in safe C#. See prior assessment.

### C3c: Seal AST Classes - DONE
All classes already sealed.

### C4: Pool Warm-Up - DONE
Pre-warmed common collection types at realm creation.

### C6: AST Node Count Reduction
Reduce total node count emitted by parser:
- Skip single-statement Block wrappers when no lexical declarations exist
- Flatten single-element Sequence expressions to their inner expression
- Eliminate redundant ExpressionStatement wrappers around expression-only
  return bodies

Each eliminated node = one fewer heap allocation = one fewer GC object.

### C7: Identifier Interning
DONE. CreateIdentifierExpression now interns by (Name, NameId) pair.
Minified files reference same short identifiers hundreds of times;
sharing one immutable instance eliminates redundant allocations.
Measured: Gen0 collections 23 -> 22 per 100 compiles (~4%).

### C8: Capture Analysis Merge into Parser
Currently PrecomputeDirectChildCaptures walks the entire AST BEFORE
compilation starts, then MarkCapturedNamesReferencedByNestedFunction
runs DURING compilation. Two full traversals.

V8/Oxc approach: track captures during PARSING. When the parser enters
a function scope, it pushes scope info. When it sees an identifier
reference, it checks if the binding lives in an outer function scope
and marks it captured immediately.

Moving this into the parser eliminates one full AST traversal AND
removes the need for the separate PrecomputeDirectChildCaptures pass.

Effort: large (requires parser to track scope stack + binding table).
Impact: eliminates ~7% of compile time plus reduces temp collections.

### C9: NodeType Enum Dispatch
Add `public abstract JsNodeType Type { get; }` to JsNode base class.
Implementations return enum constants. VisitExpression/VisitStatement
switch on enum instead of type-testing chains.

Enum comparisons are direct integer compares; `is` checks call
CastHelpers.IsInstanceOfClass (~4.5% of compile time).

Effort: medium (touch every node class + visitor switch).
Impact: eliminates CastHelpers overhead from hot paths.

## 7. Priority

| ID | what | impact | effort |
|---|---|---|---|
| C7 | Identifier interning | ~4% GC reduction | DONE |
| C6 | Node count reduction | proportional to nodes saved | medium |
| C9 | Enum dispatch | ~4.5% compile time | medium |
| C8 | Capture merge into parser | ~7% + reduced collections | large |

## 8. 2026 State of the Art - Flat Array ASTs

### Key finding from production parsers (Yuku/Zig, Oxc/Rust)

The industry has converged on replacing pointer-based AST trees with
flat arrays of struct nodes referenced by integer index. This eliminates
ALL of the following simultaneously:
- Per-node heap allocation (one bulk array instead of N objects)
- Cache misses from pointer chasing (linear memory layout)
- GC pressure (no individual objects to track)
- Serialization cost (flat arrays are already wire format)

Yuku (Zig): ~1 node per 2 source bytes; 50KB file = ~25K nodes in a
handful of contiguous arrays. 3-10x faster than alternatives.

### Data-oriented design principles

1. Indices not pointers: NodeIndex = u32, half the size of a pointer,
   position-independent, reserved value encodes "no child".
2. Structure-of-arrays layout: separate arrays for each field type
   improve cache utilization during traversals that access one field.
3. Scratch buffers: reusable per-parser buffers for building child lists;
   flushed to side tables when block is complete; reset for next use.
4. Arena owns everything: single teardown when compilation finishes.
5. Compile-time layout validation: size asserts fail the build if any
   node type exceeds its budget.

### C# adaptation path

True arena allocation is impractical for managed classes. But a
struct-based AST stored in pooled arrays IS feasible:

// Instead of: class JsBinaryExpression : JsExpression { ... }
// Use:
struct AstNode {
    AstKind Kind;        // enum byte
    int Child0;          // index or -1
    int Child1;
    int ExtraOffset;     // into side table for lists/strings
    // ... packed fields
}

// All nodes in one contiguous array:
AstNode[] _nodes = new AstNode[estimatedCount];
int _nodeCount = 0;

// Children referenced by index:
int MakeBinary(int op, int left, int right) {
    var idx = _nodeCount++;
    _nodes[idx] = new AstNode(AstKind.Binary, left, right, op);
    return idx;
}

Consumers walk via indices instead of references. No GC tracking,
no per-node allocation, linear memory = cache-friendly traversal.

This requires rewriting the parser output format and every compiler
consumer. It is the single largest remaining optimization but also
the one with the highest ceiling: it addresses PollGC (30%), improves
cache locality for ALL subsequent passes, and reduces memory footprint
proportionally.
