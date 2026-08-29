# Compiler Throughput Design - Direct Compiler Replacement Plan

## Objective

The production path uses a compact, single-owner frontend and a register-bytecode
backend:

```text
source
  -> JsLexer
  -> JavaScriptParser
  -> JsAst
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
| direct lexer -> `JsAst` | ~10.5 KB |
| class AST parse -> flat lowering | ~81.1 KB |

This is approximately an 87% parse/lowering allocation reduction for the covered
grammar. The flat representation is now the adopted production path; the
remaining work is coverage and measured end-to-end optimization.

### Pre-cutover production vs flat parse/compile benchmark (2026-08)

BenchmarkDotNet (`benchmarks/Okojo.Benchmarks/ParseCompileBenchmarks.cs`,
ShortRun, 6 warmup + 10 iterations, MemoryDiagnoser), five fixed corpora,
one shared realm per iteration set. `Production_*` records the pre-cutover
class-based pre-cutover path; `*` records `JavaScriptParser` +
`JsScriptCompiler.Compile(JsAst)`.

| scenario | prod parse | flat parse | ratio | prod compile | flat compile | ratio |
|---|---:|---:|---:|---:|---:|---:|
| Micro | 3.05 µs | 2.10 µs | 0.69× | 16.91 µs | 8.89 µs | 0.53× |
| Closures | 5.12 µs | 3.52 µs | 0.69× | 41.74 µs | 22.88 µs | 0.55× |
| Classes | 8.96 µs | 5.95 µs | 0.66× | 59.22 µs | 35.98 µs | 0.61× |
| Patterns | 13.13 µs | 6.37 µs | 0.49× | 47.42 µs | 25.54 µs | 0.54× |
| AsyncGen | 6.01 µs | 4.21 µs | 0.70× | 38.00 µs | 27.59 µs | 0.73× |

Allocations per compile operation:

| scenario | pre-cutover | flat | ratio |
|---|---:|---:|---:|
| Micro | 17.47 KB | 8.13 KB | 0.47× |
| Closures | 39.20 KB | 21.35 KB | 0.54× |
| Classes | 58.30 KB | 30.42 KB | 0.52× |
| Patterns | 54.51 KB | 16.81 KB | 0.31× |
| AsyncGen | 36.93 KB | 21.95 KB | 0.59× |

Takeaways:

1. Flat parsing wins on both time and memory (up to 2× faster, 5× less
   memory on pattern-heavy code).
2. Flat compile is **1.4–2.1× faster** than the pre-cutover compiler across all
   scenarios.
3. Flat compile allocates **1.7–3.2× less** than the pre-cutover compiler. The single
   largest win was disposing each function's `BytecodeBuilder` after
   `ToScript()`: without it, none of the ~17 rented collections were returned,
   so every nested-function compile paid full pool-rent cost (~12–14 KB per
   function; measured `mid` 15.6→4.2 KB). Secondary wins: `Mov`-based register
   copies in spread/heritage/name argument blocks, removal of dead
   `Ldar iteratorRegister` loads before register-argument runtime calls, a
   reusable child-capture dictionary, pooled scope binding lists, lazy
   parameter maps for zero-parameter functions, and list-instead-of-HashSet
   global duplicate detection.
4. Attribution methodology: stage-level deltas via
   `GC.GetTotalAllocatedBytes(precise:true)` around parse / collect / plan /
   emit (parse ≈2.3 KB, collect ≈0.7 KB, plan <0.1 KB — emission owns the rest),
   The reusable stage-allocation harness lives in `tools/CompilerAllocProbe`.

Reproduce:
`dotnet run --project benchmarks/Okojo.Benchmarks -c Release --no-build -- --filter "*ParseCompile*"`

### Output-preserving allocation pass (2026-08-30)

This pass targeted frontend work and temporary compiler diagnostics state. It did
not change opcode selection, operands, register counts, constants, or runtime
semantics.

- The lexer now allocates its BigInt literal side list only when a BigInt literal
  is actually scanned.
- Direct identifier call-site diagnostics reuse the identifier string already
  owned by `JsAst`. Composite call-site names use a 64-character stack-backed
  `PooledCharBuilder` instead of allocating a `StringBuilder` and its backing
  storage.
- `BytecodeBuilder.ToScript()` rents the temporary debug-name interning list and
  dictionary from the realm compile pool and uses a direct static intern helper.
  The immutable arrays retained by `JsScript` are unchanged.
- `CompilerAllocProbe` now measures phases independently, uses current-thread
  allocation accounting, creates a fresh compiler per production-shaped sample,
  forces collection between phase groups, and accepts bounded `--warmup` and
  `--samples` counts (defaulting to 100 and 200). Reusing one compiler in the
  previous probe accumulated compiler state and made long runs report growing
  per-operation allocation. Independently, every completed `JsScript` and its
  nested function scripts are registered recursively in the agent's strong
  script registries. Repeated same-realm compile benchmarks therefore retain
  their output and can grow into gigabytes; the reported compile allocation
  includes durable bytecode/debugger output and registry growth, not only
  transient compiler work. The bounded defaults prevent the probe from becoming
  an unbounded retention test, but a later measurement path should explicitly
  separate registered output size from transient allocation.

Median of three interleaved baseline/candidate process runs:

| corpus / phase | baseline allocation | candidate allocation | baseline time | candidate time |
|---|---:|---:|---:|---:|
| closures parse | 2.34 KB/op | 2.30 KB/op (-1.7%) | 17.38 us | 17.19 us (-1.1%) |
| closures compile, preparsed | 22.71 KB/op | 20.01 KB/op (-11.9%) | 111.41 us | 103.80 us (-6.8%) |
| linq-js parse | 42.86 KB/op | 42.83 KB/op (-0.1%) | 2767.06 us | 2726.78 us (-1.5%) |
| linq-js compile, preparsed | 2288.48 KB/op | 1957.90 KB/op (-14.4%) | 3999.72 us | 3844.41 us (-3.9%) |

The closure corpus used 500 warmups and 2,000 samples per process. The 34 KB
linq-js corpus used 100 warmups and 100 samples. Short timing deltas remain
machine-sensitive; the allocation deltas are the primary gate.

V8 keeps parser strings in parse-lifetime `AstRawString`/zone storage and renders
call expressions through `CallPrinter` only on the exceptional diagnostics path.
Okojo intentionally keeps its compile-time rendering because its flat AST is
disposed after compilation, but follows the same lifetime principle by reusing
AST strings and pooling or stack-backing temporary formatting state. A disassembly
comparison covered 34 general compiler cases plus seven call/template/BigInt cases;
every script count and normalized opcode sequence was identical.

## Current Compiler Architecture

The implemented path has these properties:

- 16-byte `AstNode` values in pooled contiguous arrays
- integer node handles with `-1` for an absent child
- post-order construction, so most children precede their parent
- dense side tables for child lists, object properties, nested functions, formal
  parameters, classes, and class elements
- one disposable `JsAst` owning the script and every nested function body
- separate binding collection, capture resolution, storage planning, and emit
  passes
- one register/accumulator emitter shared by scripts and functions
- AST lowering is the only production lowering path
- unsupported direct grammar fails explicitly instead of silently parsing twice

Implemented execution coverage includes ordinary declarations and functions,
branches, ordinary loops, and `switch`, calls and construction, named/computed
properties,
array/object data literals, binding and assignment destructuring, advanced
parameters, ordinary function expressions, closures, `this`, `throw`, and
`try`/`catch`/`finally` with optional or destructured catch bindings, synchronous
generators, ordinary async functions with `await`, async generators, and
`for-await-of`, plus base class declarations/expressions with explicit or implicit
constructors and public named/computed instance/static methods and accessors.
Class heritage, derived constructors, and implicit/explicit/spread `super()` are
also on the direct path, together with named/computed super-property loads, calls,
stores, compound assignments, updates, static/instance home objects, and lexical
use from nested arrows. Named/computed static and instance public fields also
execute directly with class-definition key capture and constructor-point values.
Source-ordered static blocks share the static initializer phase and execute with
the constructor receiver, strict block scope, class-name capture, and `super`.
Instance/static private fields now use fixed brand/slot operands, including nested
lexical access, calls, updates, optional access, and private-brand checks.

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
| Home object and `super` | `src/parsing/parser.cc`, `src/ast/scopes.cc`, `src/interpreter/bytecode-generator.cc`, `interpreter-generator.cc` | Mark home-object use during parsing/resolution, force only used home objects into contexts, preserve current `this` as receiver, and keep context-depth changes explicit when a method environment is inserted. |
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
separate product surface rather than changing `JsAst` into a full-fidelity tree.

### JavaScriptCore: share grammar, vary the builder

JavaScriptCore's parser can drive different builders, including syntax-checking
and AST-building modes. This supports the same conclusion as V8's parser/preparser
split: grammar behavior should have one source of truth even if the produced
artifact differs.

Okojo should share parser productions between eager flat building and any future
syntax-only/lazy mode. The production compiler now has one parser representation;
any alternate artifact should reuse its grammar instead of restoring a parallel
class-based parser.

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
- build spread-containing arrays from an empty literal plus a dynamic index;
  ordinary tail elements use an own-data-property opcode that bypasses prototype
  setters without applying object-property function-name inference
- preserve the stable object-shape prefix before the first spread, then copy and
  define the remaining properties in source order through existing runtime/keyed
  paths
- represent ordinary concise methods with one flat-function metadata bit; keep
  data methods in the shape prefix and lower accessors through the existing keyed
  accessor runtime until measurements justify a V8-style paired accessor table
- represent a class as one compact record plus a dense source-ordered element
  range; create the constructor, reuse the existing prototype/method/accessor
  runtime operations, then initialize the captured inner class binding
- evaluate heritage before constructor/public elements, retain the inner class
  binding as a hole through computed-key evaluation, and encode implicit derived
  forwarding as one function metadata bit using the existing super runtime ABI
- represent super-property use as demand-driven function metadata plus one
  synthetic context slot; attach instance/static home objects only to affected
  methods and copy the resolved super base through nested arrows
- treat an inserted method-environment context as part of the capture-depth ABI;
  every inherited binding propagated through that function must skip it, not just
  the synthetic super binding
- keep RegExp pattern/flags and canonical BigInt digits as arena string IDs;
  construct fresh RegExp objects through the existing runtime and load parsed
  BigInt constants through the typed constant-pool opcode
- step, default, and store destructuring elements in observable source order and
  close unfinished iterators on normal or abrupt completion
- preserve each `for-in`/`for-of` value while preparing a member assignment head;
  evaluate its base/key per iteration and reuse the ordinary prepared-member store
- emit `debugger;` as the existing single `Debugger` opcode and leave pause policy
  in the VM checkpoint layer
- reserve incoming argument registers as an ABI prefix, materialize rest before
  overlapping writes, establish parameter TDZ, and initialize each parameter in
  source order
- synthesize `arguments` only for an unshadowed ordinary-function reference,
  create it before parameter defaults, and move parameters to mapped context
  cells only for functions that observe it
- allocate per-iteration loop contexts only when a nested function captures the
  lexical head
- create function-expression closures at expression evaluation and initialize a
  named expression's self binding before parameter defaults
- pass inferred identifier and static-property names directly into nested function
  metadata; computed properties reuse the keyed-definition slow path, explicit
  names are preserved, and no class AST is reconstructed
- represent `finally` exits as a completion kind plus optional value, then replay
  return/break/continue after the finalizer through the same control-scope stack
- attach chained labels to existing breakable/iteration scopes; unmatched exits
  keep unwinding so crossed `for-of` iterators close, and per-finally completion
  kinds retain labeled destinations without adding a parallel jump subsystem
- wrap an optional chain once and mark only its `?.` links; all nullish branches
  share one end target, calls retain prepared receivers, and computed keys or
  arguments stay after the branch so short-circuiting skips their effects
- prepare a tagged-template callee/receiver before its cached site object and
  substitutions; keep cooked/raw quasi pairs dense and reuse `GetTemplateObject`
- keep generator suspend IDs and resume tables in emission state, not AST nodes;
  route return resumes through the existing abrupt-command stack and throw
  resumes through restored VM handlers
- compose generator and async function flags into the existing async-generator
  function kind; retain `0xFF` for yield and `0xFE` for await/explicit return,
  select async iterators before wrapped sync iterators for `yield*`, and keep the
  request queue plus resolve/reject work in the runtime
- represent `for-await-of` as the existing iteration node plus an async bit; share
  async/sync iterator selection with `yield*`, await step and close results, and
  route all abrupt exits through one implicit-finally completion dispatcher
- lower `switch` as a saved tag, source-ordered strict comparisons, one shared
  case-block scope, and consecutively bound clause bodies; retain the existing
  zero-based `SwitchOnSmi` specialization until corpus data justifies its guard and
  normalization sequence
- save lexical context in the VM exception-handler entry as part of the handler
  ABI; restoring only stack and PC is insufficient when an exception skips a
  captured block's `PopContext`
- lower unbound identifier read/store/`typeof` through the existing global-binding
  feedback ABI; the VM remains responsible for missing-read and strict/sloppy
  unresolvable-store behavior
- split `delete` by reference kind: property/global-object references use the
  existing keyed-delete runtime, locals and persistent lexicals return false,
  and evaluated non-references return true
- instantiate function declarations at script/function/block scope entry, lift
  `var` bindings to the containing variable environment, and merge compatible
  parameter/var/function declarations into one planned storage location
- classify program `var`/function bindings as persistent globals and program
  lexicals as script-context cells; emit the production `StaGlobalInit` and
  `StaGlobalFuncDecl` ABI instead of pinning script declarations in registers
- initialize lexical registers and context cells to the hole at scope entry, but
  use unchecked initialization stores for declaration writes and TDZ-checking
  stores only for later assignments

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

- unbound load/store/`typeof`/`delete` and persistent script declarations use the
  production global/environment ABI
- extend the landed global declaration validation to the complete early-error
  matrix and Annex-B block-function behavior; cross-script `var`, function,
  `let`, and `const` persistence and lexical/var conflicts are covered
- correct function, block, catch, class, module, and parameter environment
  boundaries
- replace the current parameter/body exclusion marker with a general environment
  model that remains correct through nested parameter functions
- arrow lexical capture of outer `arguments`; ordinary demand-driven mapped and
  unmapped arguments objects are landed
- ordinary anonymous function/class inference and named-class inner binding are
  landed
- remaining strict/sloppy assignment edge cases; local/captured `const` and
  named-function self assignment enforcement are landed
- complete source-position, source-map, local-name, and handler metadata needed by
  disassembly, stack traces, and the debugger

Exit gate: ordinary real-world scripts can use host globals and declaration
hoisting without compiler-specific rewrites.

### P1 - Common synchronous grammar

- explicit effect/value/test expression emission modes
- richer source and handler metadata for the landed abrupt-command stack

Foundation status: effect/value/test intent now propagates through logical-not,
logical, conditional, and sequence expressions, and existing break, continue,
and return emission shares one control-scope dispatcher. Direct `throw` and
`try`/`catch`/`finally` are now implemented. Finally scopes intercept exits,
preserve the return value when required, run the finalizer, and replay the saved
command; runtime throws continue through the VM handler path. The VM handler ABI
now preserves lexical context across exceptions and generator suspension.
Switch control, literal spread, ordinary object methods/accessors, RegExp, BigInt,
untagged template literals, and synchronous arrows with simple/default/rest/pattern
parameters are landed. Arrow heads reuse flat expression layouts as binding layouts
after one-pass cover-grammar validation, avoiding reparsing and duplicate pattern
nodes. `new.target` is also landed as a leaf node and direct frame load. Arrows
reuse ordinary closure bytecode plus the runtime `IsArrow` and bound-new-target
contracts; the collector skips arrow scopes when placing synthetic `arguments`, so
lexical captures resolve to the nearest enclosing ordinary function. Untagged
templates follow Ignition's alternating quasi/substitution order: substitutions
are converted with `ToString` at their source position, empty quasis are skipped,
and `Add` accumulates the result. The flat parser reuses one lexer and the dense
child pool, so this adds no nested parser owner or template side table. `for-in`
now emits the existing enumerate/next/step ABI directly, including captured
per-iteration lexical contexts. Synchronous `for-of` reuses the generic iterator
create/step/close runtime and routes continue, break, return, and throw through a
dedicated control scope. A measured fast-array specialization can follow without
changing the flat node or control contract. Chained labels and labeled
break/continue now share that control stack, retain destinations across finally,
and close crossed iterators. Optional property/call chains now copy V8's single
chain-end target, distinguish actual optional links from ordinary links, preserve
member receivers, and return true for short-circuited delete. Full early errors
and diagnostic handler metadata remain part of P1.
Iteration assignment heads now accept named/computed members and preserve the
current value while evaluating the reference, including iterator close when that
evaluation throws. Debugger statements now reuse the production opcode/hook ABI.
Tagged templates now reuse the realm-cached production site descriptor while
correcting callee-before-argument order to match V8.

Exit gate: the direct path compiles the synchronous non-class application corpus
and has differential execution coverage for every new control-flow form.

### P2 - Resumable functions

- measured live-range narrowing for the landed conservative register snapshot

Foundation status: synchronous `function*` declarations/expressions/object
methods, `yield`/`yield*`, eager advanced-parameter binding, suspend tables, sent
values, and next/return/throw resume modes are landed. Return resumes reuse the
abrupt-command stack so `finally` and iterator cleanup stay on the same path;
throw resumes reuse the VM's restored exception-handler continuation. Delegation
retains the active iterator in the existing continuation and forwards abrupt
resumes in the VM. Async declarations/expressions/object methods/arrows and unary
`await` are also landed. They share the same switch/suspend/resume emitter, use
the existing `0xFE` await operand, and leave promise resolve/reject driving in
`StartAsyncBytecodeFunction`, matching V8's resumable shape without duplicating
runtime promise state in flat nodes. Async-arrow cover heads defer contextual
await errors until `=>` disambiguates them from ordinary calls, so regexp and
division defaults parse once without speculative class nodes or a second lexer.
Async generator declarations/expressions/object methods are now the composition
of those two landed paths rather than a third compiler subsystem. They use the
existing `AsyncGenerator` runtime kind, await explicit return values with the
`0xFE` suspend marker, retain `0xFF` for ordinary yield, and select an async
iterator before the existing wrapped-sync fallback for delegation. This follows
V8's combined function-kind and suspend-table structure; Okojo intentionally
keeps async request queues and resolve/reject intrinsics in its runtime instead of
encoding them as extra bytecode. `for-await-of` is also landed using the same
async-first/wrapped-sync iterator selection as delegation. Each `next()` result
is awaited and checked;
abrupt exits converge on one completion dispatcher that awaits normal or
best-effort close before replaying return, throw, break, or outer continue. This
copies V8's implicit-finally shape while reusing Okojo's runtime close helpers and
keeps one close suspend site regardless of the number of abrupt statements.

Exit gate: planned-compiler tests cover every resume mode and Test262 can target
the new compiler for the supported function families.

### P3 - Classes and advanced references

- landed baseline: base/derived class declarations/expressions, explicit and
  implicit constructors, heritage/prototype setup, declaration and computed-key
  TDZ, inner class-name capture, public named/computed instance/static methods and
  accessors, implicit/explicit/spread `super()`, `new.target`, and derived
  `this`/return rules, plus anonymous class name inference for static inference
  sites
- landed super properties: named/computed loads and calls, assignment/compound/
  update targets, delete rejection, instance/static home objects, accessors, and
  lexical nested-arrow use
- landed static public fields: source-ordered named/computed keys, strict
  constructor-receiver initializer calls, missing initializers, `this`/`super`,
  captures, inner class-name initialization, static `prototype` early rejection,
  and direct flat execution. All keys are evaluated before the static
  initializer phase, matching V8 rather than interleaving key and value work.
- landed instance public fields: cached computed keys, base-constructor entry,
  implicit/explicit/spread derived post-`super()` scheduling, missing defaults,
  outer capture and constructor-parameter isolation, `this`/`super`, nested
  arrows, and direct flat execution
- landed static blocks: strict synthetic initializer functions, constructor
  receiver, `this`/`super`/inner-name access, block-local declarations and
  closures, source ordering with public static fields after the shared
  computed-key phase, and static-block-specific early errors. This deliberately
  reuses closure/environment and `CallProperty` bytecode instead of adding a
  static-block runtime representation or opcode.
- landed private fields: separate instance/static brands, fixed slots, initializer
  scheduling, lexical access through nested functions/classes, loads/calls/
  assignments/updates, optional access, `#x in object`, wrong-receiver errors,
  undeclared/duplicate/delete early errors, and direct flat execution. V8 uses
  private-name context slots plus keyed operations; Okojo deliberately reuses its
  direct private-field opcodes and function brand mappings.
- landed named field initializer inference: anonymous function/class values receive
  public or `#private` source names across instance/static initialization by reusing
  ordinary inferred-name closure compilation
- landed computed field initializer inference: cached normalized instance/static
  keys name anonymous functions, arrows, and classes, including numeric and symbol
  keys. Computed static keys are passed into the existing synthetic initializer so
  a nested class observes its name before static initialization, matching V8's
  special `VisitClassLiteral(..., key)` path without a new opcode or key evaluation
- landed private methods/accessors: closures are allocated once at class evaluation,
  instance descriptors are installed before every field initializer, and static
  descriptors precede static fields/blocks. Fixed brand/slot/value indices reuse
  `InitPrivateMethod`/`InitPrivateAccessor`; focused coverage includes identity,
  names, missing accessor halves, updates, `#x in`, lexical nesting, derived
  `super` home objects, early errors, and direct flat execution. This follows V8's
  class-evaluation shape and improves on production Okojo's per-instance accessor
  closure behavior.
- complete private-element, computed-key, field-initializer, and heritage ordering

Exit gate: class initialization order, derived-constructor rules, private-brand
checks, and observable function names match the production engine and V8.

### P4 - Modules

- landed import-descriptor foundation: an explicit strict module parse goal handles
  side-effect/default/named/namespace imports, string import names, and import
  attributes. Lazily allocated request/attribute/import tables follow V8's
  `SourceTextModuleDescriptor` split, import nodes only address dense ranges, and
  the binding collector creates a module root with read-only import bindings.
- landed export-descriptor foundation: one tagged, lazily pooled table represents
  local declaration/named/default exports and indirect namespace/star exports;
  export nodes retain existing flat declaration/expression payloads. Whole-module
  validation catches import/`var`/lexical/function/class conflicts, including
  nested `var`, duplicate explicit exports, missing local exports, and forward
  references after parsing, matching V8's descriptor-validation phase.
- source-free exports of imported bindings are canonicalized into indirect or
  namespace exports after validation; regular imports receive deterministic
  negative live-cell indices, local exports receive positive indices, and local
  aliases share one cell, matching V8's module-descriptor finalization contract
- planned module compilation now consumes finalized regular cells directly:
  import/export wrappers emit no syntax-tree objects, module loads/stores use the
  existing signed-cell VM opcodes, and child functions retain module-cell access.
  V8-special namespace imports use lexical/context storage and are initialized by
  one cold module-prologue runtime lookup that preserves import attributes.
- an internal experimental option now routes synchronous production module-graph
  evaluation through the flat compiler without creating a separate compiler
  assembly dependency. Runtime slot allocation uses the same deterministic signed
  cell order as the flat descriptor, including linked named, namespace, aliased,
  and anonymous-default exports. There is no fallback to `JsCompiler` in this mode.
- the opt-in module graph now parses once to a pooled `JsAst`; the linker copies its
  compact request/import/export descriptors into the persistent `ModuleLinkPlan`, the
  compiler consumes that same AST, and the module record releases it after compilation.
  Legacy class-based module parsing is absent from this path.
- flat module instantiation now compiles once into an execution artifact containing
  the script, initial context slots, and hoisted function templates. It installs named
  and default-exported function declarations into signed module cells or the shared
  top-level context before dependency evaluation; the script omits those stores, so
  evaluation preserves cyclic function identity instead of allocating a second closure.
- `import.meta` now uses one leaf arena tag and the existing zero-argument module-meta
  runtime helper in module bodies and captured functions, matching V8's inline
  `GetImportMetaObject` lowering without adding an opcode or persistent metadata.
- dynamic import now stores the evaluated specifier and optional attributes object in
  one contiguous temporary register block and calls the existing promise runtime. V8
  also uses a runtime boundary; Okojo derives the referrer from the active script instead
  of passing the closure and phase as extra operands.
- top-level await now flows from one parser-owned flag into the flat module execution
  plan. The planned body uses the existing async generator suspension bytecode and a
  tiny wrapper that returns its promise; the production module graph retains ownership
  of pending-dependency ordering. This follows V8's split between
  `BytecodeGenerator::GenerateAsyncFunctionBody` for a TLA body and
  `SourceTextModule` for async-parent scheduling, without adding an opcode or scheduler.
- remaining: extend flat compiler coverage and validate further workload performance

Exit gate: the production module linker consumes flat compiler metadata directly;
no legacy syntax-tree module objects remain on the execution path.

### P5 - Production replacement and deletion

- run parser differential tests over the production corpus
- landed canonical Test262Runner execution through the flat parser/compiler with one
  passed cache; no compiler-selection switch remains
- validated Okojo.Node, module, wrapper, and browser-host workloads
- switched all default compile entry points to the direct flat path
- removed the old execution compiler and its compatibility switch

There is no automatic "try flat, catch, parse again" fallback. Double parsing hides
missing coverage, changes diagnostics, and destroys the allocation win.

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
4. consider direct parser-to-discovery event fusion, while retaining `JsAst` for
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
