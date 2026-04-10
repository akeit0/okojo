# Okojo Module Sample

Practical ES module sample for import/export behavior:

- named exports
- default exports
- re-export (`export { ... } from`)
- default re-export alias (`export { default as x } from`)
- star re-export (`export * from`)
- namespace re-export (`export * as ns from`)
- quoted export names (`export { local as "quoted-name" }`)
- namespace import (`import * as ns`)
- live binding (`export let`)

## Files

- `src/catalog.js`
- `src/money.js`
- `src/cart.js`
- `src/index.js`
- `src/main.js`

## Run With Okojo

From repository root:

```powershell
dotnet run --project examples\OkojoModuleSampleRunner\OkojoModuleSampleRunner.csproj
```

The runner uses `engine.MainAgent.Modules.Evaluate(realm, modulePath)` (agent module API).

or

```powershell
dotnet run --project sandbox\OkojoRepl\OkojoRepl.csproj -- -e "var w=createWorker(); var ns=w.loadModule('examples/OkojoModuleSample/src/main.js'); ns.runDemo();"
```

Expected result shape:

```js
{
  shopName: "Okojo Shop",
  runCountBefore: 0,
  runCountAfter: 1,
  roundedSample: 12.34,
  catalogHasNotebook: true,
  summary: "...",
  totalText: "$...",
  totalValue: ...,
  runsViaDefault: 1
}
```

## Optional Node Check

```powershell
node examples\OkojoModuleSample\node-demo.mjs
```
