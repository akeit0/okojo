# Okojo Node Package Exports Note

## Scope

This note covers the first practical `package.json` `"exports"` slice in `Okojo.Node`.

Included in this iteration:

- root string exports:
  - `"exports": "./index.js"`
- root conditional exports:
  - `"exports": { "node": "...", "require": "...", "default": "..." }`
- subpath exports:
  - `"exports": { ".": "./main.js", "./subpath": "./lib/subpath.js" }`
- nested conditional exports under `"."` or subpath entries
- condition matching in declaration order for active CommonJS conditions

Not included in this iteration:

- pattern exports such as `"./*"`
- array fallback exports
- `"imports"`
- full ESM-side `"import"` condition routing
- package self-resolution beyond ordinary bare-package lookup

## Minimal JS Repros

```js
require("pkg");
require("pkg/subpath");
```

```json
{
  "exports": "./entry.js"
}
```

```json
{
  "exports": {
    ".": {
      "node": "./node.js",
      "require": "./require.js",
      "default": "./default.js"
    },
    "./subpath": "./lib/subpath.js"
  }
}
```

## Planned Tests

- `tests/Okojo.Node.Tests/NodeCommonJsTests.cs`
  - root string exports
  - subpath exports
  - conditional exports for `require`
  - blocked subpath does not fall back to filesystem probing

## Reference Observations

Primary references:

- [Node.js Modules: Packages](https://nodejs.org/api/packages.html)
  - `Package entry points`
  - `Subpath exports`
  - `Conditional exports`
- [Node.js CommonJS modules](https://nodejs.org/api/modules.html)

Observed Node behavior copied in this slice:

- if `"exports"` is present, it takes precedence over `"main"` and raw file probing
- subpaths must be explicitly exported
- conditional exports are matched in declaration order against active conditions
- for CommonJS loading, `node` and `require` are relevant active conditions, with `default` as fallback when declared

## Copy vs Intentional Difference

Copied:

- root and direct subpath export resolution
- conditional object matching by property order
- `"exports"` blocking fallback-to-filesystem behavior

Intentional differences for now:

- no wildcard/pattern exports
- no array target fallback
- no invalid-target error taxonomy parity
- no full ESM import-condition path yet

## Perf Plan

- keep package exports parsing in `Okojo.Node`
- parse only the target package `package.json`
- use direct exact-key lookup for subpaths
- do not add Node package policy into core `Okojo`
