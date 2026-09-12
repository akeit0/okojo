# Okojo Explicit Resource Management

## Scope for this iteration

- Parse and compile `using` and `await using` declarations as first-class syntax.
- Support disposal at lexical scope exit for:
  - blocks
  - function bodies
  - async function bodies
  - generators
  - async generators
  - loop scopes needed by the current Test262 surface
- Add `DisposableStack` and `AsyncDisposableStack`.
- Match disposal error chaining behavior, including `SuppressedError`, where the spec/Test262 requires it.

## Minimal JS repros

```js
{
  using r = { [Symbol.dispose]() { log.push("dispose"); } };
}
```

```js
{
  await using r = { async [Symbol.asyncDispose]() { log.push("asyncDispose"); } };
}
```

```js
{
  await using r = {
    get [Symbol.asyncDispose]() { return undefined; },
    [Symbol.dispose]() { log.push("dispose"); }
  };
}
```

```js
const stack = new DisposableStack();
stack.defer(() => log.push("defer"));
stack.dispose();
```

```js
const stack = new AsyncDisposableStack();
stack.defer(async () => log.push("defer"));
await stack.disposeAsync();
```

## Planned tests

- `tests\Okojo.Tests`
  - parser tests for `using` / `await using` statements and invalid forms
  - runtime/compiler tests for block disposal, loop disposal, async disposal fallback, reverse-order disposal, and suppressed error chaining
  - built-in tests for `DisposableStack` / `AsyncDisposableStack`
  - host interop tests ensuring host `[Symbol.dispose]` / `[Symbol.asyncDispose]` work with `using`
- `test262`
  - `test262\test\language\statements\using`
  - `test262\test\language\statements\await-using`
  - `test262\test\built-ins\DisposableStack`
  - `test262\test\built-ins\AsyncDisposableStack`

## Reference observations

### V8 / language-compiler-VM

- `using` and `await using` are lexical declaration forms with scope-exit disposal.
- `await using` uses `@@asyncDispose` first and falls back to `@@dispose` when async dispose is absent/undefined.
- Disposal order is LIFO.
- When both body evaluation and disposal throw, disposal errors are wrapped through `SuppressedError`.

### Node / built-ins-runtime APIs

- `DisposableStack` and `AsyncDisposableStack` are exposed as standard globals with stack-like disposal APIs.
- `AsyncDisposableStack.prototype.use()` accepts both async and sync disposables; `DisposableStack.prototype.use()` is sync-only.

## Optional QuickJS notes

- None recorded yet.

## Copy vs intentional difference

- Goal is standards-aligned behavior for the proposal as reflected by current Test262 coverage.
- No intentional divergence is planned for the core surface in this slice.

## Perf plan

- Keep scope-disposal bookkeeping off hot paths when no `using` declarations are present.
- Prefer per-scope lazy setup of disposal helpers/stacks instead of unconditional frame overhead.
- Reuse existing try/finally machinery where possible instead of introducing a second control-flow mechanism.
