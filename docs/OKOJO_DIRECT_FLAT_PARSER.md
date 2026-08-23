# Direct Flat Parser - Experimental Slice

## Scope

Add a direct lexer-to-`FlatAst` path for the syntax already executable by the
experimental planned compiler. Keep the existing class-AST parser unchanged for
production consumers during this iteration.

`FlatAst`, `FlatFunctionInfo`, `FlatParameter`, and `FlatJavaScriptParser` live in
the parsing layer. The compiler consumes node IDs and parameter spans; a direct
parse result does not retain compiler planning objects. Operator token mapping is
shared with the production parser so precedence changes have one source of truth.

The direct path supports simple declarations/functions, blocks, branches,
ordinary loops, loop control, returns, and the current flat expression families.
Unsupported syntax fails explicitly; it does not silently restart through the
class parser.

This slice adds ordinary and spread calls plus named/computed member loads. It
excludes optional chaining, `super`, and private names.
The following slice adds array literals with elisions and dynamic elements;
array spread remains explicit unsupported syntax.
The object-literal slice uses a parsing-owned dense property table. This iteration
covers named, string, numeric, computed, and shorthand data properties. Methods,
accessors, spread, and legacy `__proto__` prototype mutation are excluded.
Member writes now share a prepared-reference lowering that evaluates the base and
computed key once. Simple, arithmetic/bitwise compound, prefix, and postfix forms
are covered. Logical member assignment shares the same prepared reference and
short-circuit branch lowering as identifier assignment.
Captured lexical heads in ordinary `for` loops now receive a fresh context per
iteration. Context replacement is capture-gated, so non-capturing loops retain
the register-only path.
Ordinary construction now uses flat callee/argument child spans and the existing
scaled `Construct` ABI. Spread construction uses the existing spread runtime ABI,
while `new.target` remains deferred.
The array-binding slice represents binding targets directly in the flat arena:
`VariableDeclaratorPattern` owns an initializer and an `ArrayBindingPattern` node
whose dense child span contains identifiers, elisions, defaults, nested arrays,
and a final rest element. It covers `var`/`let`/`const` declarations; assignment
patterns and formal-parameter patterns remain separate follow-up work.
An unshadowed `undefined` identifier now emits `LdaUndefined` directly after local
binding lookup, so lexical shadowing retains normal binding semantics.
The object-binding slice adds a distinct `ObjectBindingPattern` node backed by the
existing pooled `FlatObjectProperty` table. Properties retain static or computed
source keys plus binding targets; rest is an explicit property flag. It covers
empty, shorthand, aliased, computed, defaulted, nested, numeric, and rest bindings
for `var`/`let`/`const` declarations.

## Minimal Repros

```js
let total = 0;
for (let i = 0; i < 10; i++) total += i;
total;
```

```js
function outer(x) {
  function inner() { return x + 1; }
  return inner;
}
```

```js
function invoke(target, key) {
  target.method(1);
  return target[key];
}
```

```js
let values = [1, 2 + 3, , 4];
values.length + values[1];
```

```js
function make(value, key) {
  return { first: 1, [key]: value, second: value + 1, first: 4 };
}
```

```js
let first, second;
for (let i = 0; i < 2; i++) {
  function read() { return i; }
  if (i === 0) first = read;
  else second = read;
}
first() * 10 + second(); // 1
```

```js
function Box(value) { return { value }; }
new Box(42).value;
```

```js
function collect(a, b, c) { return a * 100 + b * 10 + c; }
let values = [1, 2];
collect(...values, 3); // 123
```

```js
let [first, , third = 3, ...rest] = [1, 2, undefined, 4, 5];
first * 100 + third * 10 + rest.length; // 132
```

```js
let { a: first, [key()]: second = 7, c, ...rest } = source;
first * 100 + second * 10 + c + rest.d;
```

## Planned Tests

- `tests/Okojo.Compiler.Tests/DirectFlatParserTests.cs`
  - direct arena layout
  - direct compile and execution
  - nested function capture
  - allocated-byte comparison against class parse plus lowering
  - direct/member calls and named/computed property loads
  - array length, holes, and dynamic element initialization
  - object property order, computed keys, shorthand, duplicates, and indices
  - named/computed member assignment, compound assignment, and update
  - logical member assignment short-circuiting and computed-key evaluation count
  - capture-gated per-iteration loop-head contexts across `continue` and `break`
  - construction evaluation order, no-parenthesis/nested precedence, and wide operands
  - direct/property spread calls, spread construction, and iterator evaluation order
  - array binding elisions, defaults, rest, nesting, iterator close, wide registers,
    and class bridging
  - unshadowed and lexically shadowed `undefined` reads
  - object binding key order, defaults, nesting, numeric keys, rest exclusions,
    nullish rejection, wide operands, and class bridging

## Reference Observations

V8 builds parser nodes in zone-owned memory and carries scope/function metadata
alongside the parse result. Okojo copies the single-owner lifetime and dense node
IDs using pooled managed arrays. It intentionally keeps the production class AST
available until flat syntax coverage is sufficient for migration.

For calls and property loads, V8 and production Okojo agree on the useful
register shape: evaluate the callee/receiver first, keep it in a register, place
arguments in a contiguous register range, and distinguish undefined-receiver
calls from property calls. Named loads carry a constant-pool key and feedback
slot; computed loads keep the key in the accumulator. The flat emitter copies
that shape while using Okojo's existing opcode ABI.

V8 uses an array boilerplate and patches dynamic elements. Okojo intentionally
creates a length-sized array and initializes only present elements in source
order; skipped indices remain holes without emitting hole stores.

For object literals, V8 and production Okojo create a boilerplate/shape for the
stable named prefix and emit keyed definitions after the first dynamic key. The
flat emitter copies that structure. Computed keys are normalized before their
values execute, preserving observable evaluation order. Numeric keys bypass shape
transitions, and duplicate named keys fall into the keyed tail.

V8 and production Okojo load the member once, branch on the loaded value, and
store only when the logical operator selects the right-hand side. The flat emitter
copies that branch shape while retaining its prepared base/key registers, so a
computed key is normalized once before the load and is reused by the conditional
store.

V8 and production Okojo evaluate the constructor before its arguments, place the
arguments in a contiguous register window, and issue `Construct`. The flat emitter
copies this bytecode shape and uses the shared scaled operand encoder. For nested
`new`, the flat parser intentionally follows V8 precedence: recursive constructor
operands consume member suffixes but leave call parentheses to the owning outer
`new`. This fixes the call-then-construct shape currently produced by the class
parser for `new new Factory()(42)`.

V8 materializes each spread iterable when that argument is evaluated, before any
later argument expression. Production Okojo currently defers iteration until the
spread-call runtime helper, which incorrectly orders `target(...iterable, later())`.
The flat emitter intentionally corrects this: it materializes each spread argument
immediately into a private array, marks it with a distinct runtime flag, and lets
the existing call/construct spread helpers copy that dense materialization without
invoking the user iterator again. Existing production spread flags and runtime IDs
remain ABI-compatible.

V8 initializes each array binding immediately after its iterator step, evaluates
defaults before requesting the next value, and closes a still-open iterator on
normal or abrupt completion. Production Okojo emits the same step/store ordering
through its destructuring iterator runtimes. The flat emitter reuses those runtime
operations but adds a declaration-local `PushTry` region instead of importing the
production compiler's general finally-routing machinery; declarations cannot
branch out of the pattern, so this smaller control-flow shape is complete for the
slice.

V8 emits the undefined constant directly for an unshadowed `undefined` read. The
flat emitter copies that shape only after planned local lookup, preserving code
such as `let undefined = 42` without allocating a synthetic global binding.

V8 and production Okojo require an object-coercible source before computed-key
effects, normalize each computed key once, load and initialize its target before
the next property, then copy rest properties with every prior static or normalized
key excluded. The flat emitter copies that order and reuses Okojo's
`RequireObjectCoercible`, keyed/named load, `NormalizePropertyKey`, and
`CopyDataPropertiesExcluding` ABI. Numeric static keys use keyed loads so they stay
off atom/shape-oriented paths.

For captured `for (let ...)` heads, V8 creates a new block context for each
iteration and moves the value through the update path. Production Okojo clones a
function context because its loop aliases share function-level cells. The flat
compiler instead replaces its dedicated loop-head context with a sibling context:
copy slots, pop the old context, create the new context, then update. This keeps
outer capture depths unchanged and retains old contexts only through closures.

## Performance Plan

- reuse `JsLexer` and its identifier/string tables
- emit post-order 16-byte nodes directly; never construct `JsExpression` or
  `JsStatement` objects on the direct path
- store nested functions and formal parameters in pooled dense side tables
- use pooled temporary child buffers and dispose the full parse result at once
- compare allocated bytes for direct parse versus class parse plus flat lowering
- allocate/copy loop contexts only when a nested function captures a loop-head binding
- reuse the call argument span/register allocator for construction
- materialize spread iterables once at their source-order evaluation point
- store array bindings between iterator steps and share the existing iterator-close runtimes
- retain normalized computed object-binding keys only until a following rest copy

Initial Release measurement for 80 declaration/update pairs after warm-up:

- direct lexer-to-flat parse: approximately 10.5 KB allocated
- class parse plus flat lowering: approximately 81.1 KB allocated
- direct path reduction: approximately 87%

## Deferred

- templates, classes, modules, destructuring assignments, and destructuring
  parameter forms
- object methods, accessors, and spread
- array spread
- optional chaining, `new.target`, and private/super members
- converging the remaining production grammar on flat node handles
- direct production `JsCompiler` migration
