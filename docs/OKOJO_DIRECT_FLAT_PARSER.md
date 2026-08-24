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
- object properties, formal parameters, classes, and class elements use typed
  pooled side tables
- source locations are integer offsets into the retained source text
- the parser records syntax and early-error facts, not register allocation
- semantic passes resolve names, captures, storage, and runtime environments
- disposal returns pooled arrays in one operation

`FlatAst` is an internal execution artifact. It is not intended to become a
full-fidelity public syntax API with parents, trivia objects, and mutation helpers.

## Current Coverage

| area | implemented direct path | remaining |
|---|---|---|
| Parse goal | scripts, strict module goal with import/export descriptors, single-parse experimental linked module execution | default-path adoption, standalone function goal |
| Declarations | `var`/`let`/`const`, ordinary function and base-class declarations, function/block declaration prologues, function-scoped `var`, persistent script globals/lexicals, initial global conflict validation, side-effect/default/named/namespace imports, local/default/indirect/namespace/star exports | complete declaration early errors, Annex B |
| Blocks/control | block, `if`, `while`, `do`, ordinary `for`, `for-in`, synchronous `for-of`, `for-await-of`, `switch`, chained labels, labeled/unlabeled `break`/`continue`, `return`, `throw`, `try`/`catch`/`finally`, `debugger`, empty/expression statement | remaining declaration/control early errors |
| Primitive expressions | number, BigInt, string, boolean, null, regexp, tagged/untagged template, identifier, `this`, `new.target`, `import.meta`, contextual `super` roots, grouping | — |
| Operators | precedence table, assignment, arithmetic/logical/bitwise/comparison, conditionals, sequence, updates, optional chains, property/identifier/value/optional-chain `delete` | remaining edge-specific early errors |
| References | locals, lexical contexts, globals/unresolvable load/store/`typeof`/`delete`, named/computed ordinary and `super` properties, private field/method/accessor loads, calls, stores, updates, `#x in value`, planned regular import/local-export module-cell loads/stores through nested functions, namespace-import prologue initialization, and opt-in production module-graph execution from flat linker metadata | default-path adoption |
| Calls/construction | direct/member/optional calls, spread calls, ordinary/spread `new`, implicit/explicit/spread `super()`, super-property calls, dynamic import with optional attributes, wide operands | — |
| Arrays/objects | holes, array/object spread, data properties, ordinary/generator/async concise methods, getters/setters, computed/shorthand/index keys, stable data shape prefix, demand-driven super home objects | legacy `__proto__` intentionally excluded |
| Bindings | identifier and nested array/object declarations, defaults, rest, computed keys, optional/identifier/destructured catch bindings, class declaration and inner-name bindings, read-only import bindings in a module root scope, module-wide import/`var`/lexical/function/class conflict and local-export validation, deterministic signed module cells, runtime cell-order integration, exported-`var` instantiation metadata | remaining early errors |
| Assignments | identifier/ordinary/super/private-field member targets, compound/logical/update, array/object destructuring, core optional-chain target restrictions | remaining early errors |
| Functions | ordinary declarations/expressions, closures, synchronous and async generators with `yield`/`yield*`, async declarations/expressions/object methods with `await`, synchronous and async arrows with simple/default/rest/pattern parameters and lexical `this`/`arguments`/`new.target`, ordinary simple/default/rest/pattern parameters, named self, ordinary anonymous-function/class name inference, demand-driven mapped/unmapped `arguments` | lazy bodies |
| Classes | base/derived declarations and expressions, explicit/implicit constructors, heritage/prototype setup, derived `this`/return rules, public/private instance/static methods and accessors, named/computed public fields, instance/static private fields and brands, source-ordered static blocks, named/computed super loads/calls/stores/updates, strict bodies, declaration TDZ/const storage, inner class-name capture, anonymous name inference including named/computed fields, private methods, and private accessors | full ordering differential and Test262 gate |
| Modules | strict parse goal, side-effect/default/named/namespace imports, string import names, import attributes, local/declaration/default/indirect/namespace/star exports, compact request/import/export tables, module binding validation, imported-export canonicalization, signed live-cell assignment, single-parse opt-in linked evaluation and re-export linking, named/default-function instantiation before dependency evaluation, `import.meta` in module and closure contexts, dynamic import promise execution, top-level await and async dependency ordering | default-path adoption |

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

Prepared-reference refinement: compound, update, call, and iteration-head
references emit `RequireObjectCoercible` on the prepared base before
`NormalizePropertyKey`, matching production Okojo and V8. A null/undefined base
therefore reports its `TypeError` before a converting key runs its
`toString`, while a throwing key expression still evaluates before the coercion
check. Regression target:
`CompileString_ChecksMemberBaseCoercibleBeforeCompoundKeyNormalization`.

Update-expression refinement: identifier and member updates apply `ToNumeric`
immediately after the load, before capturing the postfix old value, so
`x++` on a boxed number or valueOf-bearing object yields the converted primitive,
matching production and V8. Regression target:
`CompileString_AppliesToNumericBeforeCapturingUpdateOldValue`.

Sloppy-identifier refinement: `let` (sloppy only) and `of` are accepted as
binding identifiers and references, so `for (var let of …)`, `using of`,
and similar Annex-B-style heads parse like the production parser.
Regression targets include the planned-mode `head-var-bound-names-let` and
`using-for-statement` test262 cases.

### Literal spread slice

This iteration emits array and object literal spread from the existing flat
`SpreadElement`/object-property records. Repros live in
`artifacts/okojobytecodetool/cases/flat_ast_literal_spread.js`; focused tests cover
left-to-right iterator/getter effects, array holes after a dynamic prefix, symbol
copying, overwrite order, and anonymous array-element function names.

V8 builds an empty array when any element is spread, tracks the next dynamic index,
and expands each iterator at its source position before later element effects.
Okojo copies that ordering with its existing `AppendArraySpread` runtime. Dynamic
non-spread elements still require CreateDataProperty semantics: they must bypass
prototype setters and must not infer an object-property name for anonymous
functions. The planned path therefore uses a dedicated no-name own-property opcode
rather than overloading assignment or Okojo's object-literal keyed define.

Object spread evaluates the source then invokes the existing
`CopyDataProperties` slow path against the already-created target. Stable named
properties before the first spread keep the precomputed shape prefix; subsequent
properties remain keyed so spread overwrite order is preserved. No temporary
property lists or class expressions are built.

### Object method/accessor slice

Scope for this iteration is ordinary concise methods plus ordinary getters and
setters with named, indexed, or computed keys. Generator/async methods and
`super` subsequently landed through their corresponding function/class slices. The reference
case is `artifacts/okojobytecodetool/cases/flat_ast_object_methods.js`; focused
tests target receiver `this`, computed-key evaluation order, accessor merging and
names, closure capture, and method non-constructibility.

V8's `VisitObjectLiteral` separates the static boilerplate from the dynamic tail,
creates concise-method closures in an object-literal home-object context, uses
own-property definition for methods, and batches static getter/setter pairs before
runtime accessor definition. Production Okojo follows the same observable shape
with `InitializeNamedProperty`, `DefineOwnKeyedProperty`, method-environment
closures, and `DefineObjectAccessor`.

The flat path copies the split and reuses Okojo's existing accessor runtime. A
single method bit in dense function metadata is sufficient for ordinary methods;
no method AST subclass is needed. Methods that reference `super` now add a
demand-driven home-object context; ordinary methods still avoid it. Accessors end the precomputed data
shape prefix and use the keyed runtime path, preserving duplicate and computed-key
order without adding an accessor-plan object. If profiling shows accessor-heavy
literals matter, the later optimization is a dense pair table matching V8's
batched static accessor pass.

### RegExp and BigInt literal slice

This iteration covers RegExp literals and BigInt literals in both the direct
parser and class-AST bridge. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_regexp_bigint.js`; focused tests cover
character classes/escaped delimiters and flags, fresh RegExp allocation per
evaluation, values beyond Number precision, and BigInt arithmetic.

V8 emits `CreateRegExpLiteral` with constant pattern/flags metadata and loads
BigInt values from the constant pool. Production Okojo intentionally differs at
the ABI boundary: it invokes the existing `CreateRegExpLiteral` runtime with two
contiguous string arguments, while BigInt already uses `LdaTypedConst`. The flat
path copies those production shapes. Both parsers use one delimiter/flag scanner.
RegExp nodes store two arena string IDs and BigInt nodes store one canonical
decimal string ID; neither needs a new side table or per-node object. Regex
construction remains runtime work because each literal evaluation must return a
fresh mutable RegExp object.

### Untagged template literal slice

This iteration covers untagged template literals, including nested templates,
escape cooking, sequence/object/regexp substitutions, and left-to-right
substitution/`ToString` effects. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_template.js`; focused tests also cover
braces inside strings and comments plus invalid untagged escapes. Tagged template
site identity/raw arrays remain a separate slice because they require cached site
metadata rather than ordinary string concatenation.

V8 stores alternating string parts and substitutions, skips empty parts, converts
each non-string substitution at its source position, and accumulates with ordinary
`Add`. Production Okojo uses the same observable `ToString`/`Add` order. The flat
path stores alternating quasi/expression node IDs in the existing child pool
and emit the V8 shape without a template object or new side table.

The lexer continues to return one template token, but a shared template scanner
locates nested substitution boundaries robustly. The direct parser temporarily
repositions the same lexer into each substitution, parses directly into the same
arena, then restores the token end. This preserves lexer-owned identifier IDs and
avoids substring parsers, nested AST owners, or class-node fallback.

### Ordinary arrow function slice

The direct path supports ordinary synchronous arrows with identifier/parenthesized
simple, default, rest, or pattern parameters, expression or block bodies, inferred
names, and lexical `this`/`arguments` behavior. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_arrow.js`; focused tests cover call
receiver replacement, nested capture, constructor rejection, nested pattern
initialization, function length, and rest-placement early errors. Lexical
`new.target` is described below.

V8 gives arrows ordinary closure bytecode but marks their function kind so closure
creation captures lexical receiver state and name resolution crosses the arrow
scope for `arguments`. Okojo already has the same runtime `IsArrow` contract. The
flat path therefore reuses `FlatFunctionInfo`, the function side table, capture
planning, and the existing function emitter; it adds one arrow flag rather than a
parallel compiler. The binding collector marks arrow scopes so synthetic
`arguments` binds at the nearest enclosing non-arrow function.

#### Arrow cover-grammar completion

Parenthesized rest and nested array/object pattern heads are complete. V8 parses
the parenthesized head once as cover grammar, then walks its
comma expression left-to-right to declare parameters; only the tail may be a
spread/rest parameter. The flat parser uses the same one-pass shape: collect
top-level parenthesized assignments/spread into the existing node pool, validate
them as bindings only when followed by `=>`, and otherwise return the ordinary
grouped/sequence expression. Existing array/object expression layouts are already
compatible with the binding walker and parameter prologue, so no duplicate
pattern tree or parser rollback is required.

The reference case extends
`artifacts/okojobytecodetool/cases/flat_ast_arrow.js` with nested defaults, array
rest, and a top-level rest parameter. Focused tests cover evaluation order,
function length, duplicate names, rest placement, and invalid parenthesized
targets. Okojo copies V8's cover-grammar validation and intentionally keeps its
existing flat parameter ABI.

#### Lexical `new.target` slice

The direct path supports `new.target` in ordinary functions and arrows while keeping it
a syntax error at script scope. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_arrow.js`; focused tests cover direct
call versus construction, an escaping arrow, member continuation, and script-level
early errors.

V8 accepts the meta-property only when the receiver scope is a function. An
ordinary function reads its incoming new-target register; an arrow captures that
value through the nearest enclosing ordinary function scope. Okojo already has
the equivalent `LdaNewTarget` frame opcode and arrow `BoundNewTargetValue` runtime
contract. The flat path therefore needs one leaf node and direct opcode emission,
not a synthetic binding or capture-table entry. Function metadata records actual
use for observability, while the hot execution path stays a single frame load.

### `for-in` enumeration slice

The direct path supports synchronous `for-in` with single identifier or nested
pattern declarations, identifier/member assignment targets, `break`/`continue`,
and captured lexical loop heads.
The reference case is `artifacts/okojobytecodetool/cases/flat_ast_for_in.js`;
focused tests cover inherited enumerable keys, nullish inputs, assignment targets,
abrupt loop control, and per-iteration closure capture.

V8 lowers `for-in` to receiver conversion, enumeration preparation, next-key,
undefined filtering, and step operations. Okojo already exposes the compact
`ForInEnumerate`/`ForInNext`/`ForInStep` ABI with runtime fallbacks for wide
registers. The planned compiler will emit that existing sequence directly and
reuse its loop control/context rotation machinery. The flat node stores one dense
three-child range (`left`, `right`, `body`) plus an in/of flag so `for-of` can reuse
the parser and storage-planning shape without changing the arena ABI.

### `for-of` iterator-close slice

The direct path supports synchronous `for-of` on the shared flat loop node. The
reference case is `artifacts/okojobytecodetool/cases/flat_ast_for_of.js`; focused
tests cover arrays and custom iterators, declaration patterns, per-iteration
capture, normal exhaustion, `continue`, `break`, `return`, and thrown bodies.

V8 marks the iterator done before stepping, clears done only after a value is
obtained, and routes abrupt body completion through iterator finalization. Okojo
already has generic create/step/close runtime helpers used by destructuring. The
planned compiler reuses them and adds one for-of control scope: `continue` jumps
without closing, `break` and `return` perform normal close, and the VM exception
handler performs best-effort close before rethrowing the original exception. This
keeps iterator machinery off the common non-iterator control path and avoids a
second runtime implementation.

#### Iteration member-target slice

Assignment heads such as `for (target.value of values)` and
`for (target[key()] in object)` reuse the prepared-member store path. V8 preserves
the current iteration value, evaluates the assignment base/key at the assignment
point, then stores that saved value. Okojo copies this ordering with one temporary
value register; no loop-specific reference representation is added. Focused tests
cover named/computed targets, per-iteration base/key effects, and abrupt computed
key errors closing a `for-of` iterator.

#### Iteration pattern-target slice

Bare destructuring assignment heads such as `for ([a, b] of pairs)`,
`for ({ x } of items)`, and `for await ([v] of sources)` are accepted as cover
grammar. The flat parser validates the array/object expression head with the same
one-pass `IsDestructuringAssignmentTarget` walk used for destructuring assignments
and still rejects patterns after `in`, since `for-in` accepts only identifier or
member references. The binding collector visits the head through the ordinary
expression path, so every target is a reference, never a new binding. Emission
stores the current iteration value once into a temporary and replays the shared
assignment-mode pattern walkers, so defaults, nested patterns, rest elements,
member targets, and per-iteration effects reuse exactly the landed destructuring
machinery. The class-AST bridge needs no change because the production parser also
keeps these heads as expressions. Regression targets are
`CompileString_ExecutesForOfWithDestructuringAssignmentHeads`,
`CompileString_ExecutesForAwaitOfWithDestructuringAssignmentHead`, and
`ParseScript_RejectsInvalidDestructuringIterationHeads`.

### Labeled control-flow slice

This iteration adds chained labels plus labeled `break`/`continue`. The reference
case is `artifacts/okojobytecodetool/cases/flat_ast_labels.js`; focused tests cover
nested labels, unknown/duplicate labels, continue-to-non-iteration early errors,
and labeled exits across nested `for-of` loops.

V8 resolves labels to breakable/iteration targets during parsing, then routes the
resolved command through its execution-control stack. The flat parser keeps only
the compact label string on statement/jump nodes and validates active targets with
a lazily allocated label stack. The compiler attaches chained labels to the
existing control scope. Unmatched labeled exits continue unwinding, so leaving an
inner `for-of` still performs IteratorClose before reaching an outer label. When
an exit crosses `finally`, the completion kind retains its label route and replays
through the same control stack after finalization; no feature-specific jump path
bypasses context or iterator cleanup.

### Optional-chain slice

This iteration covers optional named/computed property access, direct/member
optional calls, mixed optional/non-optional chain links, spread calls, and
`delete` short-circuit behavior. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_optional_chain.js`; focused tests
target receiver preservation, skipped key/argument effects, nullish versus
ordinary `undefined` links, and assignment/update early errors.

V8 wraps the complete chain once, marks only the links introduced by `?.`, and
routes those nullish checks to one chain-end label. Okojo copies that shape:
one fixed flat wrapper node, an optional bit on member links, and a distinct call
kind for optional call links. This avoids the production class AST's ambiguous
"inside a chain" flag and prevents `object?.missing.value` from incorrectly
short-circuiting after a non-optional link. Calls retain their prepared receiver,
and computed keys/arguments remain after the nullish branch so skipped effects
are not materialized.

### Debugger statement slice

`debugger;` is a fixed zero-child node that emits the existing `Debugger` opcode.
V8 and production Okojo both lower it to that single operation; the VM's existing
checkpoint policy decides whether it pauses, so the planned compiler adds no hook
or runtime abstraction. Focused coverage verifies the opcode and no-hook execution.

### Tagged-template slice

This iteration covers direct/member tags, cached template-object identity, cooked
and raw strings, substitutions, invalid cooked escapes, and receiver/evaluation
order. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_tagged_template.js`.

V8 lowers a tagged template to an ordinary call whose first argument is a
per-site `GetTemplateObject` constant, then evaluates substitutions left-to-right.
The tag callee/receiver is prepared before those arguments. Okojo copies that
order and reuses its existing `JsTemplateSiteDescriptor` plus `GetTemplateObject`
runtime, which already caches one frozen template array per realm. The flat node
stores dense cooked/raw quasi pairs interleaved with substitution node IDs; `-1`
represents an undefined cooked quasi after an invalid tagged escape. No generic
template object or parser-owned runtime cache is added.

### Synchronous-generator slice

This iteration covers `function*`, ordinary `yield`, `yield*`, and
next/return/throw resume modes. The minimal reference cases are
`artifacts/okojobytecodetool/cases/flat_ast_generator.js` and
`artifacts/okojobytecodetool/cases/flat_ast_yield_delegate.js`; focused execution
coverage also places suspension under `try`/`finally` and iterator cleanup so
abrupt resumes reuse the landed completion dispatcher.

The follow-up object-method slice accepts named/computed `*method()` forms and
feeds them through the same flat function metadata and closure emitter. Its
reference case is
`artifacts/okojobytecodetool/cases/flat_ast_generator_method.js`; no object
property flag or generator-specific definition opcode is needed.

V8 emits one entry `SwitchOnGeneratorState`, saves the live register range at
each `SuspendGenerator`, resumes at the suspend ID, then dispatches the resume
mode. Okojo copies that control shape through its existing generator bytecode and
VM runtime IDs. Flat function metadata gains only the generator kind bit, and a
yield node stores its optional operand plus one delegation bit; no continuation
AST or new runtime object is introduced. Delegation reuses the VM's active
iterator continuation and return/throw forwarding. The implementation
conservatively snapshots the complete planned register file. Narrower liveness is
benchmark-gated after correctness.

### Async-function slice

This iteration starts async coverage with `async function` declarations and
expressions plus unary `await`. The minimal reference case is
`artifacts/okojobytecodetool/cases/flat_ast_async_await.js`; focused coverage
includes fulfilled values, rejected awaits caught in the function, synchronous
throws becoming rejections, captured locals, and nested non-async function
boundaries. The follow-up object-method slice accepts named/computed
`async method()` forms through the same function metadata and definition path;
its reference case is
`artifacts/okojobytecodetool/cases/flat_ast_async_method.js`. The async-arrow
follow-up reuses the synchronous arrow head conversion and lexical capture path;
its reference case is
`artifacts/okojobytecodetool/cases/flat_ast_async_arrow.js`. Ambiguous
`async(...)` input is parsed once as a cover head: await diagnostics are deferred
until `=>` confirms an async arrow, while an ordinary call discards them. This
handles regexp, division, nested function, and parenthesized defaults without a
second lexer or AST pass.

V8 uses the generator state switch and suspend/resume machinery underneath async
functions, wrapping body completion in promise resolve/reject handling. Okojo
already centralizes that promise driver in `StartAsyncBytecodeFunction`, so the
flat emitter reuses the same switch table and marks await suspension with the
existing `0xFE` ABI operand. Function metadata gains the async kind bit and await
remains a fixed one-child node. No promise or continuation nodes enter the AST.

### Async-generator slice

This iteration composes the landed generator and async paths for `async
function*` declarations/expressions, named/computed `async *method()` object
methods, `await`, `yield`, and `yield*`. The minimal reference case is
`artifacts/okojobytecodetool/cases/flat_ast_async_generator.js`; focused coverage
executes direct-flat parsing and class-AST lowering, advanced parameters,
fulfilled awaits, yielded promises, awaited explicit returns, `try`/`finally`,
next/return/throw resume modes, sync-iterator delegation, and native
async-iterator delegation.

V8 represents async generators as one function kind but lowers their requests
through async-generator-specific await/yield/resolve/reject intrinsics. Okojo
copies the combined-kind and resumable-control shape while intentionally keeping
its existing ABI: the runtime owns the async-generator request queue and
resolve/reject machinery, the compiler emits `0xFE` suspend markers for `await`
and explicit return values, and ordinary `yield` keeps the `0xFF` marker. The
entry switch and resume-mode dispatcher remain shared with generators and async
functions, so no async-generator AST node, continuation type, or promise state is
added.

For async `yield*`, the emitter first requests `Symbol.asyncIterator`, falls back
to `Symbol.iterator` through the existing sync-to-async wrapper, awaits each
`next()` result, validates it as an object, and yields its `value`. The runtime's
active delegate still forwards external next/return/throw requests. This matches
V8's observable ordering while retaining Okojo's smaller VM/runtime split.
The landed `for-await-of` slice reuses this iterator-selection and await machinery
rather than introducing another async-iteration subsystem.

### `for-await-of` slice

This iteration accepts `for await (... of ...)` in async functions and async
generators. The reference case is
`artifacts/okojobytecodetool/cases/flat_ast_for_await_of.js`; focused tests cover
native async iterators, promised values through the wrapped-sync fallback,
declaration and member targets, per-iteration captures, labeled continue, break,
return, throw, async-generator external return, and class-AST lowering.

V8 obtains `Symbol.asyncIterator` first, wraps `Symbol.iterator` when needed,
awaits every `next()` result, validates the iterator result object, and models
abrupt close as an implicit `finally` that awaits `return()`. Okojo copies that
ordering with the same helper used by async `yield*`. One extra flag on the
existing flat for-in/of node distinguishes async-of; the binding collector,
iteration assignment path, and per-iteration context rotation remain shared.

The emitter routes break/return/outer-continue and caught throws to one compact
completion dispatcher. That dispatcher selects normal or best-effort async close,
uses one shared await suspend target, validates normal close results, then replays
the saved command through the existing outer control stack. This matches V8's
single implicit-finally shape and avoids multiplying suspend tables and close
bytecode at every abrupt statement. Rejected best-effort close preserves the
original throw through the existing runtime suppression promise.

### Destructuring

Array bindings and assignments step the iterator, apply defaults, and store each
target before requesting the next value. An unfinished iterator closes on normal
or abrupt completion. Object patterns check coercibility before computed-key
effects, normalize computed keys once, and retain only keys needed by a later rest
copy.

The flat emitter uses existing iterator/property runtime operations but avoids the
class compiler's target-thunk packaging.

Iterator-close refinement: a `next()` step that itself throws marks the iterator
as already closing, so the shared catch handler rethrows without calling
`return()`. Abrupt completions from defaults, targets, or stores still close
best-effort before rethrowing. The emitter tracks this with one in-step flag
register around each step call instead of per-element handler tables; V8 copies
the same semantic split between its step and target scopes. Regression targets:
`CompileString_SkipsIteratorCloseWhenDestructureStepThrows` plus the test262
`dstr/*iter-abpt` and `*thrw-close-skip` families.

### Parameters

Incoming formal arguments occupy the frame prefix. Advanced-parameter prologues:

1. materialize rest before local writes can overlap extra actual arguments
2. snapshot incoming formal registers
3. establish TDZ for every parameter binding
4. process each outer default and pattern in source order
5. release the snapshot immediately

This follows V8's observable ordering and fixes a production Okojo discrepancy in
which all outer defaults run before all patterns.

Parameter/body environment refinement: closures created while emitting parameter
initializers now exclude function-scope non-parameter bindings from their capture
set, matching the reference-resolution guard that already applied to direct
default-expression reads. A parameter-default closure therefore observes the
outer binding for `var x` declared later in the body, while body closures observe
the body binding. Regression target:
`CompileString_SeparatesParameterClosureCaptureFromBodyVar`.

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

Logical assignment operators (`&&=`, `||=`, `??=`) now perform NamedEvaluation
for identifier targets by reusing `EmitExpressionWithInferredName`, matching
spec section 13.15.2 step 5; member targets intentionally stay unnamed. Regression
target: `CompileString_InfersNamesThroughLogicalAssignmentOperators`.

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

Context-slot stores now preserve the same split: assignment to a captured or
script-scope `let`/class binding composes a hole-checked load before the plain
context store, so forward references through destructuring targets and nested
closures throw the required `ReferenceError` instead of silently writing.
Initialization stores (declarations, catch/loop-head bindings) stay unchecked,
and `var`/parameter/function kinds never check. The guard is one temporary
register plus a checked load, matching V8's conditional
`ThrowReferenceErrorIfHole` placement without adding an opcode. Regression
target: `CompileString_EnforcesTdzOnContextSlotLexicalStores`.

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

Shadowing refinement: when a function has parameter expressions, body-level
lexical or function declarations named `arguments` no longer suppress synthesis,
matching spec section 9.2.12 step 20 (which applies only when
hasParameterExpressions is false). The parameter-scope default therefore still
observes the real `arguments` object while the body binding shadows it locally.
The class-AST bridge passes the same flag from its parameter plan. Regression
targets are the planned-mode test262 `arguments-with-arguments-*` family.

### Using declarations slice

This iteration ports explicit resource management onto the direct path by copying
production's runtime seam. The flat parser recognizes contextual `using` and
`await using` heads with one-token and two-token lexer peeks, parses them as the
existing `VariableDeclaration` node carrying `JsVariableDeclarationKind.Using`
kinds, requires initializers, restricts bindings to identifiers, rejects
script-top-level declarations, and allows `for (using x of y)` heads while still
rejecting `using` in `for-in`. Collection treats using kinds as per-loop lexical
bindings; planning reuses lexical register/context storage.

Emission adds one explicit-resource scope wrapper built on the existing finally
control-scope machinery: create a disposable resource stack, run the wrapped body
with the scope pushed, and dispose through `DisposeDisposableResourceStack` (with
an await suspension for async scopes) before replaying return/throw/break/continue.
Declarations evaluate their initializer, store, then call
`AddDisposableResource`. Wrapping mirrors production granularity: blocks and body
statement lists containing using wrap as a whole, bare declarations without an
ambient scope get a mini scope, C-style `for` wraps the loop, and for-of/for-await-of
heads add each iteration value per iteration. Module top-level using skips stack
scopes entirely and uses the module-scoped `AddCurrentModule*` runtime, matching
`JsCompiler.ModuleExecution`. Regression targets:
`CompileString_ExecutesUsingDeclarationsWithLifoDisposal`,
`CompileString_ExecutesForOfUsingHeadsWithPerIterationDisposal`, and
`ParseScript_RejectsInvalidUsingDeclarations`.

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

V8's `ControlScopeForTryFinally` reference shape is now landed for return and
labeled/unlabeled break and continue. Okojo intentionally leaves runtime throw
routing to its VM handler stack. Labeled destinations use compact per-finally
completion kinds rather than V8's exact token encoding, while retaining the same
intercept-finalize-replay contract.

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

New side tables should be purpose-specific and dense: handler/catch records and
tagged-template site descriptors. Untagged templates and switch clauses use fixed
nodes plus the dense child table. Avoid generic object payloads.

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
- chained labels and labeled break/continue reuse the same control dispatcher;
  destination identity survives finally and exits close crossed `for-of` iterators
- optional chains use one wrapper/chain-end target and mark only actual `?.`
  links; optional calls preserve member receivers and `delete` short-circuits to true
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
  by `PushTry`; labeled exits use per-finally completion kinds rather than V8's
  exact continuation-token encoding
- allocation risk: fixed flat nodes and compiler value records only; no handler,
  result-scope, or continuation objects are allocated during emission
- deferred: catch/body lexical-conflict early errors join the general direct
  parser declaration early-error pass

### Stage F2 - Resumable functions

- ordinary async declarations/expressions/object methods/arrows and `await` are
  landed
- async generator declarations/expressions/object methods, awaited returns, and
  sync/async `yield*` delegation are landed
- `for-await-of` with awaited step/close and abrupt-command replay is landed
- narrow the landed conservative register snapshot only with measured liveness data

The parser records generator/async kind bits plus fixed yield/await nodes; one
emitter owns suspend IDs/tables and the VM owns continuation and async-promise
state. This keeps suspension layout and promise plumbing out of syntax metadata.

### Stage F3 - Classes

- compact class record and dense class-element table
- class declaration/expression scopes and inner name binding
- base/derived constructors and `new.target`
- methods/accessors, fields, computed keys, static blocks
- `super` call/property and private names/brands

Computed keys, static initialization, and instance fields must not be collapsed
into parser-time execution order. Store source order explicitly and let the class
emitter schedule spec phases.

Baseline class slice landed:

- iteration scope: base class declarations/expressions with explicit or implicit
  constructors plus public named/computed instance/static methods and accessors
- minimal repro:
  `class C { constructor(v) { this.v = v } get value() { return this.v }
  static make(v) { return new C(v) } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_baseline.js`
- focused regressions cover strict method bodies, constructor call rejection,
  declaration TDZ, inner class-name visibility, computed-key ordering, method
  names/non-constructibility, static versus prototype placement, and class-AST
  bridge execution
- V8 observation: class scope and inner name resolution precede emission; the
  constructor closure is created first, its prototype is established, and public
  elements are defined in source order with computed keys evaluated at class
  definition time
- Okojo implementation: reuse `ClassGetPrototypeAndSetConstructor`, `DefineClassMethod`,
  `DefineClassAccessor`, planned closures, and existing lexical storage; add one
  pooled class-element table rather than class-specific node objects
- first-slice boundary was heritage/derived `super`, fields, static blocks, and
  private names; only private elements remain from that list
- performance plan: keep source-order element records dense, compile each method
  once into the shared function table, allocate constructor/prototype registers
  in one temporary scope, and avoid dictionaries until private-name resolution
  requires them

Heritage and derived-constructor slice landed:

- iteration scope: `extends` evaluation, prototype/constructor heritage, implicit
  argument forwarding, explicit `super()` with ordinary/spread arguments, derived
  `this` TDZ, duplicate/missing-super errors, and derived return rules
- minimal repro:
  `class D extends B { constructor(v) { super(v); this.ready = true } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_heritage.js`
- focused regressions cover heritage evaluation before constructor initialization,
  implicit forwarding, explicit/spread calls, `new.target`, `this` before super,
  missing/duplicate super, object/undefined/primitive constructor returns, and the
  class-AST bridge
- V8 observation: `BuildClassLiteral` evaluates heritage before creating the
  constructor and computed members, and keeps the inner class binding in TDZ
  through public-element definition; derived frames keep hole-valued `this` until
  `super()` succeeds
- Okojo implementation: reuse `SetClassHeritage`, `CallSuperConstructor`,
  `CallSuperConstructorWithSpread`, and `CallSuperConstructorForwardAll`; carry
  only derived/implicit-forward bits in flat function metadata
- intentional difference: Okojo keeps its existing runtime super-construction ABI
  instead of copying V8's `FindNonDefaultConstructorOrConstruct` bytecode sequence;
  observable evaluation, forwarding, and error rules match V8
- intentional boundary: named/computed `super` properties and super method calls
  follow with method-environment capture; fields/private initialization remains a
  later source-ordered class phase
- performance plan: retain the heritage value in one temporary register, use the
  existing contiguous argument ABI, and add no new runtime helper or class object

Anonymous class-name inference slice landed:

- iteration scope: infer anonymous class names in identifier declarations and
  assignments, parameter/destructuring defaults, and static object-literal data
  properties; explicit class names remain authoritative
- minimal repro: `let C = class {}; ({ value: class {} }).value.name`
- focused regressions cover declaration/assignment/default/property inference, explicit
  names, computed property names, class-AST bridge execution, and constructor
  metadata
- V8 observation: inferred-name contexts pass a name register into class-literal
  lowering; the constructor receives the name without creating a named-class inner
  lexical binding
- Okojo implementation: extend the existing function inferred-name dispatcher to class
  expressions and pass the pooled name directly into constructor compilation
- intentional difference: computed object keys continue through Okojo's existing
  keyed-definition naming slow path instead of a dedicated class opcode
- performance plan: no AST mutation, runtime operation, string copy, or new side
  table on static identifier/property paths

Super-property and home-object slice landed:

- iteration scope: named/computed `super` loads, method calls with derived receiver,
  assignment/compound/update, delete early/runtime errors, class constructors,
  instance/static methods and accessors, plus lexical use from nested arrows
- minimal repro:
  `class D extends B { read(k) { return super[k]() } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_super_property.js`
- focused regressions cover getter/setter receiver identity, computed-key ordering,
  call receiver preservation, static versus instance home objects, nested-arrow
  capture, assignment/update results, strict set failure, and class-AST bridge
- V8 observation: `parser.cc` creates a `SuperPropertyReference` to the instance or
  static home-object variable; scope analysis forces that variable into a context
  only when needed. Ignition loads `homeObject.[[Prototype]]`, uses the current
  `this` as receiver, emits `GetNamedPropertyFromSuper`/`StoreToSuper`, and preserves
  that receiver for the following call.
- Okojo implementation: reuse `SetFunctionMethodEnvironment`,
  `LoadKeyedFromSuper`, and `SuperSet`; allocate one synthetic super-base context
  slot only in direct-flat functions whose body references a super property.
  Nested arrows copy that slot lexically. The inserted method-environment context
  is included in external-capture depth so ordinary captures remain correct.
- intentional difference: retain Okojo's keyed runtime helpers rather than adding
  Ignition's named/keyed super bytecodes until profiling justifies dedicated ops
- performance shape: no environment for methods without super, static keys stay in
  the constant pool, computed keys evaluate once, and load/store/update reuse one
  prepared reference. The class-AST bridge conservatively marks legacy class
  methods because that parser does not retain per-method super-use metadata.

Static public-field slice landed:

- iteration scope: named/computed static public fields, missing initializers,
  source-order key/value effects, and `this`/`super` in initializers
- minimal repro: `class C extends B { static [key()] = super.make(this) }`
- reference case: `artifacts/okojobytecodetool/cases/flat_ast_class_static_fields.js`
- focused tests cover method/field ordering, computed keys once, receiver/home object,
  inherited static access, inner class-name availability, undefined defaults,
  static `prototype` early rejection, and the class-AST bridge
- V8 observation: `BuildClassLiteral` evaluates each static computed key at class
  definition, then invokes a synthetic initializer with the constructor as `this`
  before defining the next static element
- Okojo implementation: reuse the dense class-element record, planned nested-function
  compilation, `CallProperty`, and `DefineClassField`; do not add a field AST or
  opcode. Each static initializer is a synthetic strict method body invoked with
  the constructor receiver, so normal capture and demand-driven super planning
  remain shared. Class definition first evaluates every computed key and defines
  methods/accessors, then initializes the inner class binding, then runs static
  field initializers in source order.
- subsequent slices added instance-field constructor scheduling and static-block
  early-error context. Anonymous field-initializer function/class naming still
  belongs in the shared initializer-result path.
- performance plan: retain one constructor register, evaluate each key once into a
  temporary, and attach a method environment only when the initializer uses `super`

Instance public-field slice landed:

- iteration scope: named/computed public instance fields, missing initializers,
  base-constructor entry, derived post-`super()` scheduling, outer captures,
  `this`/`super`, nested arrows, and constructor-parameter isolation
- minimal repro:
  `class D extends B { [key()] = super.read(); constructor() { super() } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_instance_fields.js`
- focused tests cover computed keys once per class, fields once per instance, base/derived
  ordering, implicit/explicit/spread `super()`, missing defaults, outer-vs-parameter
  shadowing, nested-arrow super, undefined `new.target`, forbidden lexical
  `arguments`, and class-AST bridge
- V8 observation: computed names are captured during class definition; one instance
  members initializer runs with the new receiver at base-constructor entry or
  immediately after derived `super()` returns
- Okojo implementation: reuse `SetFunctionInstanceFieldKey`,
  `LoadCurrentFunctionInstanceFieldKey`, and `DefineClassField`; emit initializer
  expressions inline in constructor bytecode while excluding constructor
  parameters/body locals from their lexical lookup
- intentional difference: inline the initializer sequence instead of storing and
  calling V8's synthetic initializer function; observable scope and ordering stay
  equivalent and no per-instance closure is allocated
- performance plan: one cached key per computed field, no runtime key reevaluation,
  one contiguous three-register define window, and no initializer objects

Class static-block slice landed:

- iteration scope: source-ordered static blocks with `this`, `super`, class-name
  access, local `var`/lexical declarations, nested functions, and abrupt errors
- minimal repro: `class D extends B { static { this.x = super.x + 1 } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_static_blocks.js`
- focused tests cover ordering with computed keys/static fields, constructor
  receiver, inherited static access, block-local scope, nested closures,
  undefined `new.target`, and early errors for `return`, `arguments`, `await`,
  `yield`, and outer-loop control
- V8 observation: the class static initializer function executes blocks and static
  fields together after all keys and class-name initialization
- Okojo implementation: reuse a synthetic strict method body plus `CallProperty`
  with the constructor receiver; no new AST table, opcode, or runtime helper
- performance plan: compile each block once, allocate a method environment only for
  `super`, and retain the existing pooled class-element order

Private field slice landed:

- iteration scope: instance/static private fields, direct/compound/update access,
  private calls, `#x in value`, nested lexical use, initialization order, and
  undeclared/duplicate/delete early errors
- minimal repro: `class C { #x = 1; read() { return this.#x } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_private_fields.js`
- focused tests cover instance/static brand separation, wrong-receiver errors,
  initializer order, nested functions/classes, updates, calls, brand checks, and
  class-AST bridge execution
- V8 observation: private names are resolved through a class private environment;
  instance and static fields use distinct brands and fixed compile-time slots
- Okojo implementation: add one private bit to flat member/element records and reuse existing
  `InitPrivateField`/`GetPrivateField`/`SetPrivateField` plus `HasPrivateField`
- intentional difference: V8 lowers private access through private-name context
  slots and keyed operations; Okojo embeds its existing fixed brand/slot operands,
  avoiding a runtime private-name lookup while retaining lexical brand mapping
- performance plan: preallocate one brand per instance/static class side, embed
  brand/slot operands in bytecode, and allocate no runtime name lookup table

Named field-initializer inference slice landed:

- iteration scope: anonymous function/class values in named instance/static,
  public/private field initializers on both direct and class-AST paths
- minimal repro: `class C { #f = function () {}; name() { return this.#f.name } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_field_names.js`
- focused tests cover all eight public/private, instance/static function/class
  combinations plus the class-AST bridge
- V8/Node observation: the inferred name is the source field name, including the
  leading `#` for private fields
- Okojo implementation: reuse normal inferred-name closure compilation; static
  synthetic initializer metadata carries one optional pooled name index
- performance plan: one integer metadata field per flat function, no AST rewrite,
  runtime naming helper, or additional closure

Private method/accessor slice landed:

- iteration scope: instance/static private methods plus paired/single private
  getters/setters, calls, assignment, updates, brand checks, lexical nesting, and
  receiver errors
- minimal repro:
  `class C { #m() { return this } get #x() { return 1 } call() { return this.#m() } }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_private_methods.js`
- focused tests cover method identity/non-constructibility/names, accessor reads,
  writes and updates, missing-half errors, instance/static brands, `#x in`, nested
  access, duplicate and `#constructor` early errors, derived `super` home objects,
  initialization before fields, and class-AST bridge execution
- V8 observation: private method/accessor closures are created once during class
  evaluation; instance initialization installs the brand/descriptors after base
  entry or derived `super()`, while static descriptors are installed before static
  fields and blocks
- Okojo implementation: the private binding records field/method/accessor kind and
  staticness; emission reuses `InitPrivateMethod`/`InitPrivateAccessor` and
  transports instance closures through the constructor's existing private-method
  value storage
- intentional production-path improvement: accessors are created once per class,
  and all instance method/accessor descriptors are installed before any field
  initializer, including fields that reference a later private declaration
- performance plan: create each private function closure once per class, use fixed
  brand/slot/value indices, and allocate no per-instance method/accessor closures

Computed field-initializer naming slice landed:

- iteration scope: anonymous function/arrow/class values in computed public
  instance/static fields, including numeric and symbol keys
- minimal repro:
  `let k = 'value'; class C { [k] = function () {} } new C().value.name`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_class_computed_field_names.js`
- focused tests cover direct and class-AST paths, instance/static fields,
  numeric/symbol names, explicit-name preservation, one-time property-key
  coercion, and class static initialization observing the inferred name
- V8 observation: computed keys are normalized and cached during class evaluation;
  Ignition's keyed field definition carries `kSetFunctionName`, while a class with
  static initialization receives the key before its static initializer executes
- Okojo implementation: cached normalized keys feed the existing
  property-key-aware `SetFunctionName`; computed static-field keys travel as the
  existing synthetic initializer's hidden argument, so nested class static
  initialization observes the name at V8's point
- performance plan: no new object or side table; one hidden argument only for a
  computed static initializer and one runtime naming call only for anonymous values

Module import-descriptor slice landed:

- iteration scope: module parse goal plus side-effect/default/named/namespace
  imports, string export names, import attributes, and import binding collection
- minimal repro: `import value, { read as local } from 'pkg' with { type: 'json' }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_module_imports.js`
- focused tests cover compact request/attribute/entry metadata, module strictness,
  module root scope, import binding kind/read-only planning, malformed clauses,
  string-name alias requirements, and duplicate attribute errors
- V8 observation: `SourceTextModuleDescriptor` keeps module requests separate from
  import/export entries and assigns binding/cell information after parsing; import
  declarations do not become executable statements
- Okojo implementation: lazily allocated pooled `FlatModuleRequest`,
  `FlatImportEntry`, and `FlatImportAttribute` tables are addressed by thin import
  nodes; the binding pass uses an explicit module root and read-only import kind,
  while linker execution stays on the production path until flat metadata
  consumption lands
- performance plan: module-only pooled tables, no class-AST import objects, and no
  impact on the script parser/compiler hot path

Module export-descriptor slice landed:

- iteration scope: local declaration/named/default exports, indirect named,
  namespace, and star exports with shared module requests/attributes
- minimal repro: `export const value = 1; export { value as renamed }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_module_exports.js`
- focused tests cover dense export entries, wrapped executable declarations/default
  expressions, destructured declaration names, duplicate export-name errors,
  re-export request metadata, and binding collection through export wrappers
- V8 observation: local exports and special indirect/star exports are separate
  descriptor entries; validation canonicalizes exports of imported bindings and
  assigns live-cell indices after parsing
- Okojo implementation: one tagged, lazily pooled export-entry table plus thin
  export nodes retains wrapped flat declarations/expressions for the future module
  execution plan without allocating `JsExport*` objects. A post-parse module pass
  validates duplicate bindings, nested `var` conflicts, forward local exports,
  missing locals, and duplicate explicit export names across the entire module.
- performance plan: lazily rent export storage only for modules containing exports;
  source-free exports add no module request and executable payloads reuse existing
  flat nodes

Module descriptor-finalization slice landed:

- iteration scope: canonicalize source-free exports of named/default/namespace
  imports and assign stable signed live-cell indices to regular imports/exports
- minimal repro: `import { x as local } from 'pkg'; export { local as value }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_module_cells.js`
- focused tests cover named/default/namespace canonicalization, request/import-name
  preservation, zero cells for indirect/star/namespace entries, negative regular
  import cells, positive local export cells, alias sharing, and deterministic order
- V8 observation: `MakeIndirectExportsExplicit` rewrites local exports that target
  imports before `AssignCellIndices`; regular imports receive `-1,-2,...`, local
  exports receive `+1,+2,...`, and aliases of one local share a cell
- Okojo implementation: the post-parse finalizer rewrites the owned flat tables
  after whole-module validation and before binding/storage planning, retaining
  V8's signed-index invariant for the future linker/compiler seam
- performance plan: module-only temporary dictionaries/sorted name lists; no AST
  objects, no script-path work, and no persistent map after indices are written

Planned module-cell bytecode slice landed:

- iteration scope: feed finalized regular import/export cells into planned storage,
  unwrap flat import/export statements during emission, and preserve module-cell
  access in nested compiled functions
- minimal repro:
  `import { x } from 'pkg'; export let y = x; export function read() { return y; }`
- reference case:
  `artifacts/okojobytecodetool/cases/flat_ast_module_cells.js`
- focused tests cover direct `LdaModuleVariable`/`StaModuleVariable` operands,
  deterministic local-export cells, export wrapper execution, hoisted exported
  functions, and module-cell capture instead of accidental global lookup
- V8 observation: regular imports/exports use signed module cells in Ignition;
  namespace imports remain descriptor-special and are initialized explicitly by
  the module prologue through `GetModuleNamespace`
- Okojo implementation: one module-only name-to-cell planning map classifies root
  bindings as `ModuleBinding`; the existing signed-cell VM opcodes are emitted
  directly and the capture descriptor carries module identity/depth into child
  functions. Namespace bindings use lexical/context storage and a module-prologue
  runtime call that resolves the already-linked namespace with its import type.
- performance plan: no class-AST lowering and no mirrored export stores; flat
  wrapper nodes disappear during emission and each live binding uses one VM cell
  access

Namespace-import prologue slice landed:

- iteration scope: initialize V8-special namespace imports before module body
  execution without allocating a negative regular-import cell
- minimal repro: `import * as ns from './dependency'; export default ns`
- focused tests execute planned bytecode against linked module bindings, including
  import-attribute keying, and verify that namespace bindings use lexical storage
- V8 observation: `VisitModuleNamespaceImports` calls `GetModuleNamespace` with the
  module request and initializes the module-scope variable before body emission
- Okojo implementation: append-only runtime ID `GetCurrentModuleNamespace` resolves
  the specifier relative to the active module record, reads the namespace already
  installed by linking, and initializes the planned lexical/context binding. Child
  contexts inherit the module record, so no frame-layout or opcode change is needed.
- intentional difference: Okojo passes specifier plus import type through the cold
  prologue helper rather than serializing V8's module-request index; linker adoption
  can replace this with a resolved request table if measurement warrants it
- performance plan: one cold runtime call per namespace import; regular named/default
  imports remain direct signed-cell loads

Production module execution opt-in slice landed:

- iteration scope: execute the planned module compiler inside the existing production
  module graph while keeping the default compiler untouched and avoiding a core to
  experimental assembly reference
- minimal graph: one dependency exports mutated and constant live bindings; the entry
  imports them in non-cell order, imports the namespace, and exposes named plus
  anonymous-default exports
- focused test: `CompileModule_ExecutesThroughLinkedModuleGraph` proves linked named,
  namespace, local-export, and default-export cells through `JsRealm.LoadModule`
- V8 observation: `SourceTextModuleDescriptor` finalization assigns regular imports
  negative cells and local exports positive cells by stable local name, while namespace
  imports are initialized separately by the bytecode-generator module prologue
- Okojo implementation: an internal agent compiler delegate selects the experimental
  path; the existing linker allocates cells in the finalized flat order, and every
  planned module emits a root function context so active runtime module bindings are
  attached even when it has no captured lexical slots
- Oxc insight: parser-owned compact module tables should transfer into the persistent
  module record as data, not be rebuilt as class nodes. The current delegate is only an
  adoption seam; it is not a second public compiler framework.
- single-parse ownership: the module record owns the pooled `FlatAst`; linking transfers
  request/import/export and exported-`var` instantiation data into `ModuleLinkPlan`, the
  compiler consumes the same AST, and the record returns its pools immediately afterward
- flat requests resolve directly into final link bindings; no temporary class-AST import
  or export wrapper objects are created. A cycle regression verifies exported `var` is
  visible as `undefined` before evaluation rather than remaining in TDZ.
- focused graph coverage also includes indirect, namespace, and star re-exports; the
  record is asserted to contain no class `JsProgram` and no retained flat AST after emit
- hoisted-function instantiation now compiles the flat AST once into a script, TDZ-aware
  initial context slots, and function templates. Named declarations are cloned into
  signed export cells or the shared top-level context before dependencies evaluate;
  body evaluation reuses the script and skips those stores, preserving closure identity
  across cycles. The focused cycle imports a function before its declaring module runs,
  observes the same object, and calls it successfully.
- V8 observation: module declarations are instantiated before evaluation and function
  bindings are already callable through a cycle. Okojo copies that lifecycle while
  retaining its existing signed-cell opcode ABI; no new opcode or class-AST object is
  required.
- default named and anonymous function wrappers now feed the same template artifact;
  focused coverage preserves the named local self-binding, infers anonymous name
  `"default"`, and observes the anonymous closure through an import cycle. Deferral is
  restricted to root scope so block functions retain normal runtime initialization.
- `import.meta` is a leaf flat node accepted only by the module parse goal and emitted as
  the existing zero-argument runtime call. The linked regression checks resolved `url`
  and object identity from a captured function. V8 likewise lowers it to inline
  `GetImportMetaObject`; Okojo intentionally reuses its host-populated module object.
- dynamic import uses one two-child flat node for specifier/options and the existing
  promise runtime. Parsing covers an optional second argument and trailing comma; linked
  execution resolves a planned dependency and updates a live export from `.then`.
  Like V8, evaluation order is explicit before the runtime call. Okojo intentionally
  derives the referrer from `JsScript.SourcePath`; import phases remain outside this slice.
- nested-function scripts inherit the compile artifact's source text and path, matching
  the production compiler, so dynamic `import()` inside arrows/methods resolves relative
  specifiers against the real referrer and stack traces keep file locations. Planned
  function scripts previously cleared `SourceCode`, which made every nested-function
  import resolve against the process working directory. Regression target:
  `CompileFunction_InheritsSourcePathForDynamicImportReferrer`.
- top-level `await` sets one parser-owned `HasTopLevelAwait` bit and the linker transfers
  it directly into `ModuleExecutionPlan`. The planned module body emits the existing
  async generator prologue/resume table, while a tiny ordinary script wrapper creates
  and calls that async closure so evaluation returns its promise.
- `for await (...)` heads are recognized in module top-level scope the same way as
  unary `await`, so `for await (binding of [await 1])` parses and records the TLA bit
  even though `asyncFunctionDepth` is zero.
- V8 observation: `bytecode-generator.cc` handles
  `kModuleWithTopLevelAwait` through `GenerateAsyncFunctionBody`, while
  `source-text-module.cc` owns pending dependency counts and async-parent release.
  Okojo copies that body/graph boundary: the existing production graph waits on the
  returned promise and schedules parents, so no module-only opcode or parallel
  scheduler is introduced.
- focused graph coverage proves a parent observes an imported mutation performed after
  its dependency resumes from `await Promise.resolve()`. `JsRealm.Import` waits for
  completion; the lower-level `LoadModule` API intentionally remains a nonblocking
  completion handle.
- next slice: default-path adoption and replacement-gate measurement
- performance plan: benchmark parse/link/plan/emit separately and compare this true
  single-parse path against class parse plus production compilation. Instantiation emits
  once and evaluation only executes the retained script; it does not recompile or replace
  hoisted closures.

### Stage F4 - Modules

- module parse goal and initial module early errors are landed
- compact import/export entry tables are landed
- module scopes, live binding references, storage, and opt-in linked execution are landed
- linker-facing persistent metadata without class-AST wrappers is landed in opt-in mode
- named and default function declarations are instantiated from the flat execution artifact
- `import.meta` is landed through the existing module runtime binding
- dynamic import is landed through the existing promise runtime
- top-level await and async dependency order are landed through the existing module graph

Module records outlive a single bytecode function, so their ownership boundary
must be explicit rather than hidden in temporary parser tables.

### Stage F5 - Replacement

1. complete normalized parser differential coverage
2. expand the explicit `Test262Runner --planned-compiler` gate across applicable
   coverage; its direct script and flat-module worker paths and separate passed cache
   are landed, with initial addition and module-code probes green
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
- [x] modules link/evaluate from flat metadata
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
