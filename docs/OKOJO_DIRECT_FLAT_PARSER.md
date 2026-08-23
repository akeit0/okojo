# Direct Flat Parser - Experimental Slice

## Scope

Add a direct lexer-to-`FlatAst` path for the syntax already executable by the
experimental planned compiler. Keep the existing class-AST parser unchanged for
production consumers during this iteration.

The direct path supports simple declarations/functions, blocks, branches,
ordinary loops, loop control, returns, and the current flat expression families.
Unsupported syntax fails explicitly; it does not silently restart through the
class parser.

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

## Planned Tests

- `tests/Okojo.Compiler.Tests/DirectFlatParserTests.cs`
  - direct arena layout
  - direct compile and execution
  - nested function capture
  - allocated-byte comparison against class parse plus lowering

## Reference Observations

V8 builds parser nodes in zone-owned memory and carries scope/function metadata
alongside the parse result. Okojo copies the single-owner lifetime and dense node
IDs using pooled managed arrays. It intentionally keeps the production class AST
available until flat syntax coverage is sufficient for migration.

## Performance Plan

- reuse `JsLexer` and its identifier/string tables
- emit post-order 16-byte nodes directly; never construct `JsExpression` or
  `JsStatement` objects on the direct path
- use pooled temporary child buffers and dispose the full parse result at once
- compare allocated bytes for direct parse versus class parse plus flat lowering

Initial Release measurement for 80 declaration/update pairs after warm-up:

- direct lexer-to-flat parse: approximately 10.5 KB allocated
- class parse plus flat lowering: approximately 81.1 KB allocated
- direct path reduction: approximately 87%

## Deferred

- calls and member access
- arrays, objects, templates, classes, modules, destructuring, and advanced
  parameter forms
- unifying the complete production grammar on flat node handles
- direct production `JsCompiler` migration
