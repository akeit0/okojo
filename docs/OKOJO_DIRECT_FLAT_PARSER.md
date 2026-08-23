# Direct Flat Parser - Coverage and Replacement Plan

## Purpose

`FlatJavaScriptParser` is the intended production parser for executable
ECMAScript. It consumes `JsLexer` tokens and constructs `FlatAst` directly,
without allocating the public/class syntax objects used by `JavaScriptParser`.

The target production flow is:

```text
JsLexer -> FlatJavaScriptParser -> FlatAst
        -> binding collector -> storage planner -> bytecode emitter
```

The class parser and `FlatAstLowerer` remain a temporary compatibility and
differential-testing bridge. The end state removes them from normal execution,
not merely adds another permanent compiler choice.

The broader architecture, reference research, and throughput gates are in
[`OKOJO_COMPILER_THROUGHPUT_DESIGN.md`](OKOJO_COMPILER_THROUGHPUT_DESIGN.md).

V8 is the primary behavioral and compiler reference. Okojo's VM is already an
Ignition-like accumulator/register machine, so new scope, frame, call, control-flow,
and bytecode decisions should copy V8 by default. Oxc is a secondary reference for
how to build the frontend cheaply; it does not override V8 semantics or bytecode
shape.

## Ownership and Representation

The direct path follows these contracts:

- `FlatAst` owns all nodes and side tables for one script or module compile
- the script and nested function bodies share that owner
- `AstNode` remains 16 bytes and is referenced by integer ID
- `-1` means absent child; no nullable node objects are allocated
- nodes are normally appended post-order
- variable-length children live in dense pooled spans
- object properties and formal parameters use typed pooled side tables
- source locations are integer offsets into the retained source text
- the parser records syntax and early-error facts, not register allocation
- semantic passes resolve names, captures, storage, and runtime environments
- disposal returns pooled arrays in one operation

`FlatAst` is an internal execution artifact. It is not intended to become a
full-fidelity public syntax API with parents, trivia objects, and mutation helpers.

## Current Coverage

| area | implemented direct path | remaining |
|---|---|---|
| Parse goal | scripts | modules, standalone function goal |
| Declarations | `var`/`let`/`const`, ordinary function declarations, function/block declaration prologues, function-scoped `var`, persistent script globals/lexicals, initial global conflict validation | classes, imports/exports, complete declaration early errors, Annex B |
| Blocks/control | block, `if`, `while`, `do`, ordinary `for`, `switch`, unlabeled `break`/`continue`, `return`, `throw`, `try`/`catch`/`finally`, empty/expression statement | `for-in/of`, labels, `debugger` |
| Primitive expressions | number, string, boolean, null, identifier, `this`, grouping | regexp, BigInt, templates, `super`, `new.target`, `import.meta` |
| Operators | precedence table, assignment, arithmetic/logical/bitwise/comparison, conditionals, sequence, updates, property/identifier/value `delete` | optional-chain operators and delete-chain behavior, remaining edge-specific early errors |
| References | locals, lexical contexts, globals/unresolvable load/store/`typeof`/`delete`, named/computed properties | imports, private and super references |
| Calls/construction | direct/member calls, spread calls, ordinary/spread `new`, wide operands | optional calls, dynamic import, super call |
| Arrays/objects | holes, data properties, computed/shorthand/index keys, stable shape prefix | array/object spread emission, methods, getters/setters, legacy `__proto__` intentionally excluded |
| Bindings | identifier and nested array/object declarations, defaults, rest, computed keys, optional/identifier/destructured catch bindings | class, module bindings and remaining early errors |
| Assignments | identifier/member targets, compound/logical/update, array/object destructuring | private/super targets, optional-chain restrictions |
| Functions | ordinary declarations/expressions, closures, simple/default/rest/pattern parameters, named self, ordinary anonymous-function name inference, `this`, demand-driven mapped/unmapped `arguments` | arrows, async, generators, class-name inference, lazy bodies |
| Classes | none | declaration/expression, constructors, methods, fields, static blocks, private names, super |
| Modules | none | parse goal, entries, linking metadata, live bindings, top-level await |

The direct parser rejects unsupported grammar. It does not catch an error and
restart through `JavaScriptParser`; that would allocate both representations,
change diagnostics, and conceal coverage gaps.

## Current Semantic Gaps

Syntax coverage alone is not enough to replace production compilation. The
planned compiler still needs:

- the remaining global declaration early-error matrix; unbound references,
  persistent script declarations, and identifier delete use the VM/global
  environment ABI
- Annex-B block functions and conflicts across nested lexical/variable
  environments; ordinary local declaration hoisting and script-level conflicts
  are landed
- a general parameter/body environment model; the current exclusion marker fixes
  ordinary cases but is not the final nested-environment representation
- class name inference; ordinary anonymous-function inference is landed
- remaining strict/sloppy assignment edge cases outside ordinary lexical bindings
- complete source-position, handler, local-name, and debugger scope metadata

These are P0 because real programs depend on them even when their syntax is
otherwise already supported.

## Implemented Compiler Insights

### Calls and construction

Evaluate the callee or receiver first, retain it in a register, evaluate arguments
left-to-right into a contiguous range, and select the receiver-aware call opcode.
Construction uses the same dense argument/register machinery and scaled operand
encoding.

Spread iterables are materialized when their argument is evaluated. This matches
V8 ordering and intentionally corrects the production Okojo path that can defer
user iteration until the spread runtime helper.

### Properties and assignments

Object literals prebuild a transition shape only for the stable canonical named
prefix. Computed keys, numeric indices, and duplicate names enter the keyed tail;
numeric indices never become shape transitions.

Member assignment prepares the base and normalized computed key once. Compound,
logical, prefix, postfix, and destructuring stores reuse that prepared reference,
so observable base/key expressions are never duplicated.

### Destructuring

Array bindings and assignments step the iterator, apply defaults, and store each
target before requesting the next value. An unfinished iterator closes on normal
or abrupt completion. Object patterns check coercibility before computed-key
effects, normalize computed keys once, and retain only keys needed by a later rest
copy.

The flat emitter uses existing iterator/property runtime operations but avoids the
class compiler's target-thunk packaging.

### Parameters

Incoming formal arguments occupy the frame prefix. Advanced-parameter prologues:

1. materialize rest before local writes can overlap extra actual arguments
2. snapshot incoming formal registers
3. establish TDZ for every parameter binding
4. process each outer default and pattern in source order
5. release the snapshot immediately

This follows V8's observable ordering and fixes a production Okojo discrepancy in
which all outer defaults run before all patterns.

### Closures and loops

Binding discovery and storage planning decide whether a local stays in a register
or moves into a context. Captured lexical `for` heads receive a new sibling context
per iteration; non-captured heads remain register-only.

Function expressions create their closure at expression evaluation. Named
expressions initialize a function-local self binding before parameter defaults.
`this` is a zero-payload node that emits Okojo's existing `LdaThis` frame load.

### Function-name inference slice

This iteration covers anonymous ordinary function expressions in identifier
declarations and assignments, ordinary parameter/default binding positions, and
object-literal data properties. Minimal repros are `let f = function () {}`,
`f = function () {}`, `function read(value = function () {}) {}`, destructuring
defaults, `{ method: function () {} }`, and `{ [key]: function () {} }` including
symbol keys. The connected regression target is
`DirectFlatParserTests.CompileString_InfersAnonymousFunctionNames`.

V8's parser assigns identifier and static-property names before bytecode emission
(`SetFunctionNameFromIdentifierRef` and `SetFunctionNameFromPropertyName`). For a
computed object key, Ignition retains the evaluated key and emits
`Runtime::kSetFunctionName` only when the value needs a name. The flat compiler
copies the semantic split: static names are passed directly into nested function
metadata without mutating `FlatAst`; computed keys reuse Okojo's existing fused
`DefineOwnKeyedProperty` name assignment. Member assignments remain unnamed,
matching V8 and Node. The shared keyed-property helper now preserves explicit
function names instead of replacing them with the property key.

The hot static path adds no runtime operation, AST object, or new string: it reuses
the pooled identifier/property string. Computed properties add no function-specific
runtime call beyond their existing keyed definition. Explicit function-expression
names remain authoritative and continue to create the only function-local self
binding.

### Exceptions and finally

`ThrowStatement`, `TryStatement`, and `CatchClause` use ordinary fixed flat nodes;
catch bindings reuse the existing binding-pattern representation and receive an
explicit catch scope in discovery/storage planning.

The emitter copies the structure of V8
`BytecodeGenerator::ControlScopeForTryFinally`: an intercepted exit stores a
small completion kind and, for return, its accumulator value. The finalizer runs
with that control scope removed, so an abrupt finalizer overrides the pending
completion. Normal completion or the saved return/break/continue is then replayed
through the enclosing control stack. Runtime exceptions use Okojo's existing
`PushTry`/`PopTry`/`Throw` ABI instead of copying V8's handler-table and
`ReThrow` opcodes literally.

Handler entries now also save the current `JsContext`. The VM restores context,
stack, and PC together at a handler target, and suspended generators retain the
same saved handler contexts. This fixes exceptions that bypass bytecode
`PopContext` from captured lexical blocks.

### Switch slice

This iteration adds direct parsing, binding discovery, and planned emission for
ordinary `switch`, including a default clause in any position, fallthrough,
unlabeled `break`, returns through `finally`, and one lexical environment shared by
all clauses. Minimal repros and the production-bytecode input live in
`artifacts/okojobytecodetool/cases/flat_ast_switch.js`; focused regressions target
selection/fallthrough plus cross-clause TDZ and capture behavior.

V8's `BytecodeGenerator::VisitSwitchStatement` evaluates and saves the tag before
entering the case-block scope, emits comparisons in source order (with the default
as the no-match destination), and binds clause bodies consecutively for natural
fallthrough. It may replace a profitable dense Smi range with
`SwitchOnSmiNoFeedback`. Okojo copies the semantic/control shape first using its
existing strict-compare and jump ABI. Okojo already has a zero-based `SwitchOnSmi`
opcode, but profitable general integer switches need type/integer/range guards and
a case-value-base normalization sequence. Enabling that specialization is deferred
until corpus measurements justify the added bytecode. No case objects are
allocated: `SwitchStatement` and `SwitchCase` use fixed flat nodes plus the existing
dense child table.

### Global references

An identifier unresolved by local scope planning or an outer flat-function
capture emits the existing `LdaGlobal`, `StaGlobal`, or `TypeOfGlobal` family.
Names and global-binding feedback slots are deduplicated by `BytecodeBuilder`,
and one helper selects narrow or wide operands. This matches production Okojo
bytecode and V8's global feedback shape while leaving ReferenceError and
strict/sloppy store decisions in the VM.

Minimal repros are `hostValue += 2`, `typeof missingValue`, a missing ordinary
read, and strict versus sloppy assignment to an unresolvable name. Regression
targets are
`DirectFlatParserTests.CompileString_LoadsStoresUpdatesAndTypesGlobalBindings`
and `CompileString_AppliesSloppyAndStrictUnresolvableStoreRules`. No new runtime
operation or compiler binding object was added. Identifier delete remains separate
because it requires Reference/Environment Record semantics, not another load
branch.

### Declaration instantiation

The discover pass now assigns every `var` binding to its nearest function or
program scope and merges compatible parameter/var/function declarations by
scope and name. Initializer references still retain their source lexical scope,
so `{ let local = 40; var lifted = local + 2; }` resolves `local` correctly while
storing `lifted` in the function environment.

Emission has a scope-entry declaration prologue. It initializes each canonical
`var` binding to `undefined`, creates function-declaration closures in source
order so the last compatible declaration wins, and treats declaration statements
as runtime no-ops. Block functions use the same prologue when their block scope
is entered. A `var` declaration without an initializer performs no source-position
store, so it cannot reset a parameter or earlier assignment.

This copies V8's declaration-instantiation timing and the observed Ignition
shape—`CreateClosure` precedes the first body call—while retaining Okojo's dense
binding plan and existing closure opcodes. Regression targets are
`CompileString_HoistsFunctionDeclarationsAtScopeEntry`,
`CompileString_HoistsVarWithoutResettingParametersAtDeclarationSite`, and
`Collect_LiftsAndMergesCompatibleVarBindings`. The pass adds one temporary
semantic dictionary for declaration merging; bytecode emission allocates no
hoisting objects.

Program storage now follows V8's Global Environment split instead of treating a
script as a function frame. Root `var` and function declarations plan as
`GlobalBinding` and emit the existing `StaGlobalInit`/`StaGlobalFuncDecl` family.
Root `let` and `const` bindings occupy script-context slots and publish
`TopLevelLexicalAtoms`, slots, and const flags through `JsScript`; later scripts
therefore resolve them through the same global-binding inline cache used by the
production compiler. Local function declarations still use registers or captured
context cells.

Scope entry initializes ordinary lexical bindings to the hole. Declaration
initialization then uses an unchecked register write, while subsequent lexical
assignment uses `StaLexicalLocal` and preserves its TDZ check. Function-name self
and parameter bindings keep their separate prologue ordering. This distinction is
required by Okojo's opcode ABI and mirrors Ignition's separation between creating
uninitialized bindings and initializing them.

This iteration covers cross-script persistence, global lexical/var conflicts,
restricted global properties, duplicate root lexicals, const reassignment, and
top-level/local TDZ. Minimal repros are:

```js
var count = 1; let step = 2; const limit = 3;
function add() { return count + step + limit; }
```

followed by `count += 40; step += 1; add();`, and `typeof value; let value = 1;`.
Regression targets are
`CompileString_PersistsGlobalDeclarationsAcrossScripts`,
`CompileString_RejectsGlobalDeclarationConflicts`, and
`CompileString_EnforcesLexicalTdzBeforeDeclaration`.

The implementation copies V8's semantic split and production Okojo's exact
global opcodes; it intentionally reuses Okojo's existing context metadata and VM
global IC instead of adding a V8-style runtime declaration opcode. Compile-time
cost is one declaration-name set plus lexical metadata arrays only when root
lexicals exist. Script `var`/functions no longer consume pinned registers.
Complete nested early errors, Annex-B block-function rules, class declarations,
modules, and direct-eval-specific behavior remain outside this slice; direct eval
is intentionally unsupported by project policy.

### Immutable binding assignment slice

Iteration scope: reject assignment/update/destructuring stores to planned local or
captured `const` bindings, and apply strict/sloppy named-function self-assignment
rules. Minimal repros are `const value = 1; value = 2`, a closure assigning an
outer `const`, and `(function named() { "use strict"; named = 1; })()`.

Regression targets are
`CompileString_RejectsAssignmentToLocalAndCapturedConstBindings` and
`CompileString_AppliesNamedFunctionSelfAssignmentRules`. Production Okojo and V8
Ignition both evaluate the right-hand side and then call
`ThrowConstAssignError`; no store occurs. Okojo reuses that runtime ID and
carries immutability in existing planned/capture records. The intentional ABI
difference remains Okojo's existing runtime-call encoding and debug-name table.

The hot mutable-store path gains one planned-flag branch and no runtime allocation.
No new opcode or binding hierarchy is needed. Module imports/exports, class-name
bindings, and `using`/`await using` assignment errors remain with their syntax
slices.

### Arguments binding slice

Iteration scope: synthesize `arguments` only for an unshadowed ordinary-function
reference, expose it to parameter defaults, and preserve simple sloppy parameter
aliasing. Repros cover `arguments.length`, `arguments[0] = value`, a default
reading `arguments[0]`, and parameter/lexical/`var` shadowing. Regression targets
are direct reads, mapped/unmapped writes, defaults, and shadowing in
`DirectFlatParserTests`.

Production Okojo uses `CreateMappedArguments`; its VM selects mapped versus
unmapped behavior from the function's strict/simple flags. V8 likewise creates
the object in the function prologue only when the scope records an arguments
binding. Okojo copies that shape and reuses `ArgumentsMappedSlots`; parameters
move to context cells only in functions that observe `arguments`. No runtime type
or opcode is added. The object allocation occurs only when JavaScript can observe
it.

### Delete expression slice

Iteration scope: lower property, local identifier, persistent global lexical,
global object binding, unresolvable identifier, and non-reference `delete`, with
strict unqualified-identifier early errors. Repros include `delete object[key]`,
`delete local`, cross-script `delete lexical`, deletable host globals, and
`delete sideEffect()`.

Regression targets cover result booleans, evaluation order, configurable versus
non-configurable properties, and strict failures. V8 emits a property delete for
property references, `false` for resolved lexical/local references, a lookup
delete for globals/unresolvables, and `true` after evaluating non-references.
Okojo copies that reference split while reusing `DeleteKeyedProperty` and its
strict variant. No new opcode or runtime allocation is required.

## Reference Lessons Applied

### V8

- parser AST and scope records feed a separate Ignition bytecode visitor
- compile-time scopes resolve declarations/references and choose local versus
  context allocation
- register-allocation scopes release temporary ranges structurally
- expression result modes distinguish effect, value, and branch contexts
- lazy parsing works at function boundaries through preparser metadata

Okojo copies the pass boundaries and bytecode evaluation shapes. It intentionally
uses pooled node IDs rather than V8's Zone-allocated pointer tree.

For each remaining feature, the implementation note must identify the relevant V8
scope and Ignition bytecode shape first, then state whether Okojo copies it or keeps
an intentional ABI-level difference.

Use a local V8 checkout as source, not only the overview documents. For each
feature, trace this sequence:

1. grammar production and parser state in `src/parsing/parser-base.h`
2. cover-grammar or delayed-error handling in `src/parsing/expression-scope.h`
3. declaration/reference resolution and allocation in `src/ast/scopes.*`
4. evaluation order and result mode in
   `src/interpreter/bytecode-generator.cc`
5. control/handler shape in `src/interpreter/control-flow-builders.*` and
   `handler-table-builder.*`
6. final opcode and operand contract in `src/interpreter/bytecodes.*` and
   `bytecode-array-builder.*`

Copy the semantic layering, not C++ mechanics. `ParserBase<Parser>` and
`ParserBase<PreParser>` demonstrate one grammar with different products;
`ExpressionScope` demonstrates scoped ambiguity/error state; `Scope` resolves
before allocating; and `BytecodeGenerator` carries explicit effect/value/test,
register, context, and abrupt-control scopes. Okojo should represent the same
facts with dense IDs and pooled tables.

V8's `ControlScopeForTryFinally` reference shape is now landed for unlabeled
return, break, and continue. Okojo intentionally leaves runtime throw routing to
its VM handler stack; labeled destination tokens are added when labels land.

### Oxc

- one arena owns parser products and arena-aware collections
- core statement/expression enums are held to 16-byte size tests
- parsing and semantic analysis are separate phases
- nodes retain compact spans and semantic IDs are attached/resolved later
- allocation and conformance checks are normal parser development tools

Oxc itself uses a typed arena tree, not a flat integer-index AST. Okojo copies its
lifetime discipline, compactness, and phase separation while keeping a more
bytecode-specific flat representation.

### Roslyn and JavaScriptCore

Roslyn shows that a rich public syntax facade can be separated from the compact
compiler representation. JavaScriptCore shows that one grammar can drive distinct
builders, such as syntax checking and AST construction. Together they argue for a
future shared Okojo grammar core, not two permanently divergent parsers.

## Remaining Coverage Plan

### Stage F0 - Binding and declaration correctness

Implement before adding large syntax families:

- unbound load/store/update/`typeof`/`delete` and persistent script
  `var`/function/lexical declarations are landed
- complete declaration early errors and Annex-B behavior; initial root
  lexical/var/restricted-property conflicts, ordinary function/block hoisting,
  and function-scoped `var` are landed
- function, block, catch, class, module, and parameter environment records
- ordinary function-name inference, demand-driven mapped/unmapped `arguments`,
  local/captured `const`, and strict/sloppy named-function self assignment are
  landed; class-name inference remains with class coverage
- source/handler/local-name metadata

Focused corpus:

```js
read();
function read() { return hostGlobal + value; }
var value = 1;
```

```js
let outer = 42;
function read(value = function nested(next = outer) { return next; }) {
  var outer = 1;
  return value();
}
```

### Stage F1 - Synchronous application grammar

- extend effect/value/test modes to the remaining expressions
- extend abrupt-completion routing to labels and iterator cleanup; switch breaks
  are landed
- `for-in`/`for-of` and labels
- `debugger`
- regexp, BigInt, and template literals
- array/object spread
- object methods and accessors
- ordinary arrows with lexical `this`, `arguments`, and `new.target`
- optional chaining/calls

New side tables should be purpose-specific and dense: handler/catch records and
template spans. Switch clauses already use fixed nodes and the dense child table.
Avoid generic object payloads.

First foundation slice landed:

- `ExpressionResult` carries effect, value, or single-target test intent without
  allocating result-scope objects
- logical `&&`/`||`, logical-not, conditional, and sequence expressions propagate
  test intent directly to jumps
- pure literals in sequence-effect positions emit no accumulator load
- break, continue, and return route through one control-scope dispatcher; loop
  context unwinding is no longer a separate statement-only path
- `throw`, `try`/`catch`/`finally`, optional catch bindings, and catch binding
  patterns parse directly into the arena
- finally continuation kinds replay return/break/continue after cleanup and
  compose through nested finalizers
- exception handlers restore saved lexical context as well as stack and PC,
  including after generator suspension

Minimal repro:

```js
function choose(a, b, c) {
  if ((123, a && (b || !c))) return 1;
  while (a ? b : c) {
    if (b) break;
    c = 0;
  }
  return 0;
}
```

Regression target:
`DirectFlatParserTests.CompileString_EmitsLogicalConditionsInTestMode`.

This copies V8's `ExpressionResultScope` and `ControlScopeForTryFinally`
responsibilities with value records and no per-scope object allocation. Okojo
uses compact integer completion kinds and its existing `PushTry` bytecode rather
than V8's switch opcode and handler-table encoding. The immediate throughput
checks remain fewer materialized booleans, no redundant literal loads, and no
class-AST allocation; end-to-end impact remains benchmark-gated.

Try/finally slice note:

- iteration scope: direct throw, try/catch/finally, optional and destructured
  catch bindings, nested finalizers, and return/break/continue replay
- repros: `try { throw { value: 4 } } catch ({ value }) { ... } finally { cleanup() }` and
  `try { return value } finally { cleanup() }`
- regression targets:
  `DirectFlatParserTests.CompileString_ReplaysAbruptCompletionsAfterFinally`,
  `CompileString_RestoresHandlerContextAndAllowsFinallyOverride`, and
  `CompileString_ExecutesOptionalAndDestructuredCatchBindings`
- V8 observation: deferred commands save a token/result before entering finally;
  Okojo copies that control shape
- intentional difference: Okojo runtime throws remain accumulator values routed
  by `PushTry`; no new rethrow opcode or general labeled route map was added
- allocation risk: fixed flat nodes and compiler value records only; no handler,
  result-scope, or continuation objects are allocated during emission
- deferred: catch/body lexical-conflict early errors join the general direct
  parser declaration early-error pass; labeled completion destinations join
  labels rather than introducing unused route scaffolding now

### Stage F2 - Resumable functions

- generators, `yield`, and `yield*`
- async functions and `await`
- async generators and `for-await-of`
- suspension/resume tables and preserved register/context ranges

The parser should record suspension points and function flags; the emitter and VM
remain responsible for resume-state layout.

### Stage F3 - Classes

- compact class record and dense class-element table
- class declaration/expression scopes and inner name binding
- base/derived constructors and `new.target`
- methods/accessors, fields, computed keys, static blocks
- `super` call/property and private names/brands

Computed keys, static initialization, and instance fields must not be collapsed
into parser-time execution order. Store source order explicitly and let the class
emitter schedule spec phases.

### Stage F4 - Modules

- module parse goal and module early errors
- compact import/export entry tables
- module scopes, live binding references, and storage
- linker-facing metadata without class-AST wrappers
- dynamic import, `import.meta`, top-level await, and async dependency order

Module records outlive a single bytecode function, so their ownership boundary
must be explicit rather than hidden in temporary parser tables.

### Stage F5 - Replacement

1. complete normalized parser differential coverage
2. run applicable Test262 through the planned compiler
3. run Okojo.Node and browser-host application workloads
4. make direct flat compilation the default
5. keep the old path only behind an explicit diagnostic switch for a bounded
   stabilization period
6. remove `FlatAstLowerer` and execution-only class parser/compiler code

## Parser Implementation Strategy

- migrate productions in semantic slices, not token-by-token feature stubs
- add the feature's node/side-table representation, early errors, binding visit,
  emitter, bytecode reference, and focused execution test together
- keep parser state explicit for strict mode, function kind, loop/label targets,
  class context, module goal, and await/yield permissions
- avoid dictionaries and `List<T>` in ordinary productions; use stack buffers,
  pooled growth, and typed side tables
- do not intern every source substring; reuse lexer identifiers and decode/copy
  only values that must survive the token
- preserve exact evaluation order in the emitter, not by relying on node creation
  order
- keep slow semantic operations behind runtime IDs and keep common register paths
  branch-light

## Validation Plan

### Parser

- normalized direct/class AST comparison for supported grammar
- exact early-error and source-position regressions
- malformed-source fuzzing for termination and stable diagnostics
- allocation snapshots for representative syntax families

### Compiler and VM

- inspect production Okojo bytecode before each feature
- inspect V8 Ignition for language/compiler behavior
- compare execution order and abrupt completion with observable repros
- test narrow/wide operand boundaries, register pressure, context depth, and
  handler ranges
- run the connected compiler suite focused-first, then once in full

### Migration

- planned-compiler Test262 mode, with failures separated into parser, binding,
  emitter, VM contract, and unsupported categories
- real minified libraries, many-small-function bundles, class-heavy applications,
  and module graphs
- debugger/disassembly/stack-trace verification, not only returned values

## Performance Plan

Keep the current allocation comparison, then measure the complete pipeline:

- lexer, parser, discovery, resolve/allocate, emit, and total time
- cold and warmed runs
- allocated bytes and Gen0 collections
- peak and retained pooled-array capacity
- nodes/side-table bytes per source byte
- bytecode bytes, constants, registers, and context slots
- direct path versus class parse + lower + planned emit
- direct path versus the production compiler

Do not use automatic fallback in performance measurements. Unsupported inputs
must be excluded or reported as coverage failures, not reparsed invisibly.

## Production Replacement Gates

- [ ] F0 binding/declaration semantics complete
- [ ] common synchronous application corpus compiles directly
- [ ] classes compile directly
- [ ] modules link/evaluate from flat metadata
- [ ] planned-compiler Test262 gate established and green for supported coverage
- [ ] source diagnostics, disassembly, stack traces, and debugger scopes verified
- [ ] end-to-end Release benchmarks beat or match production across representative
      workloads without material bytecode/register/context regressions
- [ ] default embedding and host entry points use direct flat compilation
- [ ] no automatic class-parser fallback remains
- [ ] old execution parser/lowerer/compiler path removed

## Intentionally Unsupported Legacy Semantics

Replacement does not require restoring deprecated behavior already excluded by
project policy: direct-eval-specific semantics, `with`, legacy `__proto__`
mutation, legacy accessor APIs, `Function.prototype.arguments/caller`, or
`arguments.callee`.

## Primary References

- [V8 parsing and AST](https://chromium.googlesource.com/v8/v8/+/main/docs/parsing/parser-and-ast.md)
- [V8 scopes and ScopeInfo](https://chromium.googlesource.com/v8/v8/+/main/docs/runtime/scopes-and-scope-infos.md)
- [V8 Ignition](https://chromium.googlesource.com/v8/v8/+/refs/heads/main/docs/interpreter/interpreter-ignition.md)
- [V8 lazy parsing](https://v8.dev/blog/preparser)
- [Oxc parser architecture](https://oxc.rs/docs/learn/architecture/parser.html)
- [Oxc AST design](https://oxc.rs/docs/contribute/parser/ast)
- [Oxc allocator](https://docs.rs/oxc_allocator/latest/oxc_allocator/struct.Allocator.html)
- [Roslyn red/green tree design](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Design/Red-Green%20Trees.md)
- [JavaScriptCore parser](https://github.com/WebKit/WebKit/blob/main/Source/JavaScriptCore/parser/Parser.cpp)
