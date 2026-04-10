# Okojo Node Module System Note

## Scope

This note covers the first Node-style module-system determination slice in `Okojo.Node`.

Included in this iteration:

- entrypoint format detection for:
  - `.mjs`
  - `.cjs`
  - `.js` with nearest `package.json` `"type"`
- `require(...)` routing based on resolved module format
- sync ESM loading through `require(...)` for modules that complete immediately

Not included in this iteration:

- top-level-`await` support through `require(...)`
- `package.json` `"exports"`
- `.json` / `.node`
- Node built-in `node:` modules

## Minimal JS Repros

```js
// main.mjs
export default 42;
```

```js
// package.json
{ "type": "module" }

// main.js
export const answer = 42;
```

```js
// main.cjs
module.exports = require("./esm.js").value;
```

## Planned Tests

- `tests/Okojo.Node.Tests/NodeCommonJsTests.cs`
  - `.mjs` entry uses ESM
  - `.js` under `"type": "module"` uses ESM
  - `.cjs` forces CommonJS
  - `require(...)` of sync ESM returns the namespace object

## Reference Observations

Primary references:

- [Node.js Modules: CommonJS modules](https://nodejs.org/api/modules.html)
- [Node.js Modules: ECMAScript modules](https://nodejs.org/api/esm.html)
- [Node.js Modules: Packages](https://nodejs.org/api/packages.html)

Relevant sections:

- `Determining module system`
- `package.json and file extensions`

Observed Node behavior copied in this slice:

- `.mjs` is ESM
- `.cjs` is CommonJS
- `.js` under nearest package `"type": "module"` is ESM
- CommonJS `require(...)` can synchronously observe an ESM namespace object when the target completes synchronously

## Copy vs Intentional Difference

Copied:

- extension-based `.mjs` / `.cjs` split
- nearest-package `"type"` check for `.js`
- sync `require(...)` of sync ESM returning a namespace object

Intentional differences for now:

- async ESM via `require(...)` throws a runtime error instead of matching full Node error taxonomy
- no `ERR_REQUIRE_ASYNC_MODULE` / `ERR_REQUIRE_ESM` compatibility layer yet
- no package `"exports"` or conditional resolution yet

## Perf Plan

- keep format determination outside core `Okojo`
- resolve format with small string checks first, package.json lookup only for `.js`
- keep `require(...)` on the CommonJS cache path for CommonJS modules
- rely on Okojo module caching for ESM namespace reuse
