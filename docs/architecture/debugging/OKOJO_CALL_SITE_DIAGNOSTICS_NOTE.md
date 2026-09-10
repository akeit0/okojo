# Okojo call-site diagnostics

## Scope for this iteration

Improve non-callable and non-constructable errors from source-level call and `new`
expressions. `JsScript` records a compact program-counter-to-callee-name table so an
error from `x()` can report `x is not a function` instead of `Not a function`.
Compiler-generated calls remain unlabeled and retain their existing fallback messages.

## Minimal repros

```js
let x = 1;
x(); // TypeError: x is not a function
```

```js
let o = { x: 1 };
o.x(); // TypeError: o.x is not a function
```

```js
let C = 1;
new C(); // TypeError: C is not a constructor
```

## Planned tests

- `tests/Okojo.Tests/ErrorConstructorTests.cs`
  - direct identifier calls
  - named and computed member calls
  - nested and spread calls
  - construction errors
  - public `JsScript` call-site metadata lookup
- `tests/Okojo.Tests/StackTraceTests.cs`
  - the richer message is retained in `Error.stack`

## Reference observations

V8 routes `Runtime_ThrowCalledNonCallable` through
`ErrorUtils::NewCalledNonCallableError`. Its deferred `RenderCallSite` path computes the
source location, reparses the function, and uses `CallPrinter` to render expressions such
as `x`, `o.x`, `o[0]`, and `f(...)`. The final template is `% is not a function`.

Relevant local V8 sources:

- `src/runtime/runtime-internal.cc`
- `src/execution/messages.cc`
- `src/ast/prettyprinter.cc`
- `src/common/message-template.h`

Node/V8 observations for this iteration include:

```text
x()       -> x is not a function
o.x()     -> o.x is not a function
o["x"]() -> o.x is not a function
f()()     -> f(...) is not a function
```

## Copy versus intentional difference

Okojo copies V8's source-level callee rendering and message templates. It intentionally
does not reparse source after an exception: Okojo's arena AST is released after
compilation, so the compiler renders supported callee expressions once and interns them
in `JsScript.DebugNames`. The VM performs a binary-search lookup only on the exceptional
non-callable path. Unsupported or compiler-generated call shapes use the existing generic
fallback.

## Performance plan

- No additional branch or lookup is added to successful calls.
- Call-site names are rendered at compile time and share the existing debug-name string
  pool.
- Each labeled call adds one PC and one name-index entry; repeated text is interned.
- Wide bytecode uses the actual call opcode PC, and bytecode rewriting remaps the table
  with the other diagnostic PC tables.
