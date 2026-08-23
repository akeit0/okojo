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

This slice adds ordinary calls plus named/computed member loads. It excludes
spread, optional chaining, `super`, private names, construction, and member writes.
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

Initial Release measurement for 80 declaration/update pairs after warm-up:

- direct lexer-to-flat parse: approximately 10.5 KB allocated
- class parse plus flat lowering: approximately 81.1 KB allocated
- direct path reduction: approximately 87%

## Deferred

- templates, classes, modules, destructuring, and advanced
  parameter forms
- object methods, accessors, and spread
- array spread
- spread calls, optional chaining, construction, and private/super members
- converging the remaining production grammar on flat node handles
- direct production `JsCompiler` migration
