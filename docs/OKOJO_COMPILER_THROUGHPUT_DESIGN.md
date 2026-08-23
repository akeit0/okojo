# Compiler Throughput Design - Direct Flat Replacement Plan

## Objective

Replace the production class-AST parse and compiler path with a compact,
single-owner frontend and a planned register-bytecode backend:

```text
source
  -> JsLexer
  -> FlatJavaScriptParser
  -> FlatAst
  -> binding discovery and reference collection
  -> scope/capture resolution
  -> register/context allocation
  -> bytecode emission
  -> JsScript / JsBytecodeFunction
```

The replacement target is not a one-pass parser that emits bytecode while it is
still recognizing grammar. ECMAScript declaration instantiation, hoisting,
parameter environments, captures, classes, modules, and abrupt completion all
need information that is not reliably available at the first token visit. The
long-term shape remains multi-pass, but every pass operates on dense IDs and
pooled tables rather than GC-allocated syntax objects.

Priority remains:

1. correctness
2. observability and tooling
3. measured optimization

Reference priority is intentionally asymmetric:

1. V8 for ECMAScript semantics, scope analysis, frame/register shape, bytecode
   generation, feedback operands, and lazy compilation
2. Oxc for parser allocation, compact AST layout, phase separation, and frontend
   benchmarking discipline
3. Roslyn and JavaScriptCore for optional tooling facades and shared-grammar
   builder patterns

Okojo's accumulator/register VM and many opcode shapes are already Ignition-like.
The lowest-risk long-term design is therefore to follow V8 unless Okojo's existing
ABI or a measured managed-runtime constraint gives a concrete reason to differ.

## Current Measurements

Historical `linq-js` baseline, 34 KB minified:

| phase | time | share |
|---|---:|---:|
| Parse | ~2.85 ms | ~34% |
| Compile | ~5.66 ms | ~66% |
| Total | ~8.5 ms | 100% |

The earlier class pipeline allocated approximately 230 KB of syntax objects per
compile and showed substantial `PollGC` and type-test cost. The current direct
flat parsing microbenchmark, using 80 declaration/update pairs after warm-up,
measures:

| path | allocated bytes |
|---|---:|
| direct lexer -> `FlatAst` | ~10.5 KB |
| class AST parse -> flat lowering | ~81.1 KB |

This is approximately an 87% parse/lowering allocation reduction for the covered
grammar. It is evidence for the representation, not yet a production migration
result: application syntax coverage and end-to-end compile time are still the
gates.

## Current Flat Architecture

The implemented path has these properties:

- 16-byte `AstNode` values in pooled contiguous arrays
- integer node handles with `-1` for an absent child
- post-order construction, so most children precede their parent
- dense side tables for child lists, object properties, nested functions, and
  formal parameters
- one disposable `FlatAst` owning the script and every nested function body
- separate binding collection, capture resolution, storage planning, and emit
  passes
- one register/accumulator emitter shared by scripts and functions
- class-AST lowering retained only as a temporary compatibility bridge
- unsupported direct grammar fails explicitly instead of silently parsing twice

Implemented execution coverage includes ordinary declarations and functions,
branches and ordinary loops, calls and construction, named/computed properties,
array/object data literals, binding and assignment destructuring, advanced
parameters, ordinary function expressions, closures, and `this`.

## Reference Architecture Insights

### V8: copy the compiler shape, not the C++ object layout

V8 parses to an AST, resolves variables through explicit compile-time scopes, and
then lets Ignition's `BytecodeGenerator` visit the AST. Its scope analysis decides
whether a name stays local or requires a heap context. Ignition uses an
accumulator, virtual registers, scoped temporary-register allocation, explicit
expression result modes, and feedback slots on property/global operations.

V8 also has a preparser that validates and records the information needed by an
outer compilation while skipping full AST construction for eligible function
bodies. Lazy functions are fully parsed and compiled when needed.

Okojo decisions:

- treat V8 as the default answer for new language/compiler/VM shape decisions
- record each deviation as an existing-ABI constraint, a simpler equivalent, or a
  measured Okojo-specific optimization
- copy the explicit discover -> resolve -> allocate -> emit structure
- keep function compilation as the natural ownership and laziness boundary
- keep accumulator/register result shape and scoped temporary release
- preserve feedback/cache operands in bytecode contracts where Okojo has them
- retain enough scope metadata for debugger-visible locals and context chains
- defer lazy function parsing until the eager flat compiler is complete and
  measured; laziness multiplies parser-state and source-lifetime complexity
- do not copy V8's pointer-heavy Zone AST; pooled index arrays are the managed
  equivalent that better fits Okojo

#### Concrete V8 source map

Use these V8 implementation files, in this order, when designing a compiler
slice. Paths are relative to a V8 checkout. The source mapping below was checked
against V8 revision `08dadaff028` (2026-03-27).

| concern | V8 source | Okojo application |
|---|---|---|
| Shared grammar | `src/parsing/parser-base.h`, `parser.h`, `preparser.h` | Keep one production core capable of driving the eager flat builder and a future syntax-only/preparse builder. Do not grow two independent grammars. |
| Cover grammar and early errors | `src/parsing/expression-scope.h` | Model expression/pattern/arrow ambiguity with scoped parser state and delayed diagnostics rather than reparsing or constructing temporary class nodes. |
| Function parse ownership | `src/parsing/parse-info.*`, `preparse-data.*` | Keep function source ranges, flags, and outer-scope requirements as the eventual lazy-compilation boundary. |
| Name resolution and storage | `src/ast/scopes.h`, `scopes.cc` | Resolve references recursively before allocating registers/context slots. Emit a compact runtime scope record only for scopes that need a context or observability metadata. |
| AST-to-bytecode lowering | `src/interpreter/bytecode-generator.h`, `bytecode-generator.cc` | Use explicit effect/value/test result modes plus scoped register, context, and control state. Avoid forcing every expression through an accumulator value when a branch or effect form is sufficient. |
| Bytecode assembly | `src/interpreter/bytecode-array-builder.*`, `bytecode-array-writer.*` | Centralize operand scaling, labels, source positions, constant operands, and final bytecode serialization. Feature emitters should express operations, not encode widths themselves. |
| Temporary registers | `src/interpreter/bytecode-register-allocator.h` | Preserve Okojo's current monotonic high-water allocator and marker-based bulk release; allocate contiguous ranges for calls and runtime operations. |
| Structured control flow | `src/interpreter/control-flow-builders.*`, `handler-table-builder.*` | Give loops, switches, catch, and finally dedicated builders. Route abrupt commands through a control-scope stack so contexts unwind correctly. |
| Opcode ABI | `src/interpreter/bytecodes.*`, `bytecode-operands.*` | Treat operand roles, scaling, accumulator use, and register ranges as reviewable ABI contracts. |

The highest-value near-term copy is V8's separation between expression result
mode and abrupt-control routing. Okojo already has register markers and an
Ignition-like accumulator, but `try`/`finally`, labels, optional chains, and
resumable functions will become fragile if each emitter hand-assembles its own
exit behavior.

For `finally`, follow the shape in `BytecodeGenerator::ControlScopeForTryFinally`:
intercept the outgoing command, preserve any accumulator result, record a compact
continuation token, enter `finally`, then dispatch the saved command afterward.
The exact V8 token representation is not an ABI requirement; the centralized
control contract and context unwinding are.

### Oxc: copy lifetime discipline and phase separation

Oxc uses a bump allocator and arena-aware collections for AST ownership. Its AST
is a strongly typed arena tree, not a flat integer-index array. Oxc enforces small
core enum sizes, including 16-byte statement and expression enums, and keeps
source spans on nodes. Parsing is followed by a distinct semantic phase for scope,
symbol, reference, and additional syntax analysis.

Okojo decisions:

- copy single-owner bulk lifetime and allocation accounting
- keep the 16-byte node-size contract under a regression test
- keep parsing and semantic/storage planning separate
- retain source offsets as compact values rather than allocating location objects
- distinguish binding identifiers, references, and property names semantically,
  even when they share a compact syntactic payload
- add allocation snapshots and application corpus benchmarks as routine frontend
  checks
- do not reproduce Rust lifetimes, arena pointers, or Oxc's general-purpose
  transform AST; Okojo's flat form is compiler-internal and bytecode-oriented

### Roslyn: useful boundary, wrong default representation

Roslyn's immutable green tree and lazy red facade are optimized for full-fidelity,
incremental IDE use. Sharing and on-demand parent/position wrappers are valuable
when a syntax tree must survive edits and serve many tools.

Okojo does not need that cost on the execution path. The reusable lesson is to
keep the hot compiler representation internal and expose richer diagnostic views
only on demand. If Okojo later needs an IDE-grade public syntax API, it should be a
separate product surface rather than changing `FlatAst` into a full-fidelity tree.

### JavaScriptCore: share grammar, vary the builder

JavaScriptCore's parser can drive different builders, including syntax-checking
and AST-building modes. This supports the same conclusion as V8's parser/preparser
split: grammar behavior should have one source of truth even if the produced
artifact differs.

Okojo should eventually share parser productions between eager flat building and
any future syntax-only/lazy mode. It should not maintain a production class parser
and an unrelated flat parser indefinitely.

## Landed Bytecode-Shape Lessons

The direct flat work has already established several reusable rules:

- evaluate a call receiver/callee first and place arguments in one contiguous
  register window
- prepare assignment member references once; never reevaluate a base or computed
  key during load/branch/store lowering
- create stable object-shape prefixes only for canonical non-index named keys;
  computed, indexed, and duplicate tails use keyed definitions
- materialize spread iterables at their source-order evaluation point rather than
  deferring user iteration until a later runtime helper
- step, default, and store destructuring elements in observable source order and
  close unfinished iterators on normal or abrupt completion
- reserve incoming argument registers as an ABI prefix, materialize rest before
  overlapping writes, establish parameter TDZ, and initialize each parameter in
  source order
- allocate per-iteration loop contexts only when a nested function captures the
  lexical head
- create function-expression closures at expression evaluation and initialize a
  named expression's self binding before parameter defaults

These are compiler contracts, not parser conveniences. New syntax should lower to
the same small set of prepared-reference, iterator, context, call, and abrupt-flow
operations.

## Target Pass Contracts

| pass | owns | must not do |
|---|---|---|
| Scan | token kind, raw span, literal decoding state | allocate AST objects |
| Parse | grammar, early errors, compact nodes and side-table entries | choose registers or context slots |
| Discover | scopes, declarations, references, function/class/module boundaries | emit bytecode |
| Resolve | bind references, mark captures, model parameter/body and class scopes | depend on source-order emitter accidents |
| Allocate | frame prefix, locals, temporaries, contexts, module/import storage | execute semantic slow paths |
| Emit | evaluation order, branches, handlers, opcodes, source positions | rediscover declarations |
| Runtime | dynamic coercion, iteration, property, call, module, async slow paths | repair compiler ordering |

Frame layout and opcode operands remain ABI contracts. Wide operand selection,
constant-pool indexing, context depth, handler ranges, and source positions must
go through centralized builders.

## Remaining Coverage Plan

### P0 - Semantic foundation for real programs

Complete these before broad grammar because nearly every application depends on
them:

- global and unresolvable name load/store/`typeof`/delete behavior
- script and function declaration instantiation, including `var` and function
  hoisting
- correct function, block, catch, class, module, and parameter environment
  boundaries
- replace the current parameter/body exclusion marker with a general environment
  model that remains correct through nested parameter functions
- `arguments` creation/mapping rules and arrow lexical capture
- anonymous function/class name inference
- immutable binding enforcement and strict/sloppy assignment behavior
- complete source-position, source-map, local-name, and handler metadata needed by
  disassembly, stack traces, and the debugger

Exit gate: ordinary real-world scripts can use host globals and declaration
hoisting without compiler-specific rewrites.

### P1 - Common synchronous grammar

- explicit effect/value/test expression emission modes
- one abrupt-command stack for return, throw/rethrow, break, and continue,
  including context unwinding and finally continuation dispatch
- `throw`, `try`/`catch`/`finally`, and completion routing
- `switch`
- `for-in` and `for-of`
- labeled statements and labeled `break`/`continue`
- template, regexp, and BigInt literals
- array/object spread
- object methods, getters, and setters
- ordinary arrow functions
- optional calls/chains and delete-chain behavior

Foundation status: effect/value/test intent now propagates through logical-not,
logical, conditional, and sequence expressions, and existing break, continue,
and return emission shares one control-scope dispatcher. Try/finally interception,
continuation tokens, labeled targets, and handler metadata remain part of P1.

Exit gate: the direct path compiles the synchronous non-class application corpus
and has differential execution coverage for every new control-flow form.

### P2 - Resumable functions

- generators and `yield`/`yield*`
- async functions and `await`
- async generators and `for-await-of`
- suspension metadata, register/context preservation, and abrupt resume paths

Exit gate: planned-compiler tests cover every resume mode and Test262 can target
the new compiler for the supported function families.

### P3 - Classes and advanced references

- class declarations and expressions
- base/derived constructors and `new.target`
- methods, accessors, fields, and static blocks
- `super` calls and named/computed super properties
- private names, brands, accessors, and `#x in object`
- computed-key and field-initializer ordering

Exit gate: class initialization order, derived-constructor rules, private-brand
checks, and observable function names match the production engine and V8.

### P4 - Modules

- module parse goal and early errors
- import/export entries in compact side tables
- module scope and live binding storage
- linking/evaluation integration
- dynamic import, `import.meta`, top-level await, and async dependency ordering

Exit gate: the production module linker consumes flat compiler metadata directly;
no class-AST module objects remain on the execution path.

### P5 - Production replacement and deletion

- run parser differential tests over the production corpus
- run planned-compiler execution against applicable Test262 coverage
- validate Okojo.Node and browser-host workloads
- switch the default compile entry points to the direct flat path
- retain the old path only behind an explicit diagnostic switch during a bounded
  stabilization window
- remove class-AST lowering and then remove the execution-only class parser once
  no supported consumer depends on it

There should be no automatic "try flat, catch, parse again" fallback. Double
parsing hides missing coverage, changes diagnostics, and destroys the allocation
win. Selection must be explicit until the direct path becomes the default.

## Validation Gates

### Correctness

- focused regression for each syntax/semantic slice
- full connected compiler suite after focused coverage
- normalized parser differential checks for structure, early errors, spans, and
  function metadata
- Okojo production-bytecode and V8 Ignition comparison for every new language or
  compiler feature
- differential execution against production Okojo where production behavior is
  correct; document intentional corrections where it is not
- planned-compiler Test262 mode before default replacement
- fuzzing for parser termination, malformed-input diagnostics, and direct/class
  behavioral disagreement

### Observability

- disassembly has function names, source ranges, context slots, handlers, and wide
  operands
- VM traces can map bytecode PCs back to source and visible locals
- stack traces and debugger scopes remain correct through contexts, catch blocks,
  classes, and suspension
- unsupported syntax reports one stable parse diagnostic, never an internal
  compiler exception

### Performance

Measure Release builds after warm-up and keep raw samples:

- scan, parse, discover, resolve/allocate, and emit time separately
- total cold and warm compile latency
- allocated bytes, Gen0 collections, peak pooled capacity, and retained capacity
- nodes and side-table bytes per source byte
- bytecode size, register count, context slots, and constant-pool size
- small scripts, minified libraries, many-small-functions, class-heavy code,
  module graphs, and application workloads

Do not optimize opcode count or reintroduce specialized fast paths from a single
microbenchmark. A migration requires a stable or improved correctness profile and
an end-to-end win across representative workloads.

## Post-Replacement Optimization

After eager production replacement:

1. lazy/preparse function bodies, using function source ranges and serialized
   scope summaries
2. reduce scanning cost on measured ASCII/minified hot paths
3. specialize side-table packing only where size profiles justify it
4. consider direct parser-to-discovery event fusion, while retaining `FlatAst` for
   bytecode emission and diagnostics
5. consider a separate optional syntax facade for tooling; do not burden runtime
   compilation with Roslyn-style full fidelity by default

## Primary References

- [V8 parsing and AST](https://chromium.googlesource.com/v8/v8/+/main/docs/parsing/parser-and-ast.md)
- [V8 scopes and ScopeInfo](https://chromium.googlesource.com/v8/v8/+/main/docs/runtime/scopes-and-scope-infos.md)
- [V8 Ignition bytecode generation](https://chromium.googlesource.com/v8/v8/+/refs/heads/main/docs/interpreter/interpreter-ignition.md)
- [V8 scanner optimization](https://v8.dev/blog/scanner)
- [V8 lazy parsing and preparser](https://v8.dev/blog/preparser)
- [Oxc parser architecture](https://oxc.rs/docs/learn/architecture/parser.html)
- [Oxc AST design](https://oxc.rs/docs/contribute/parser/ast)
- [Oxc allocator](https://docs.rs/oxc_allocator/latest/oxc_allocator/struct.Allocator.html)
- [Oxc semantic analyzer source](https://github.com/oxc-project/oxc/blob/main/crates/oxc_semantic/src/lib.rs)
- [Roslyn red/green tree design](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Design/Red-Green%20Trees.md)
- [JavaScriptCore parser builders](https://github.com/WebKit/WebKit/blob/main/Source/JavaScriptCore/parser/Parser.cpp)
