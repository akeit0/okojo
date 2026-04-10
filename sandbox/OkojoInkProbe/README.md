# Okojo Ink Probe

This sandbox tries real installed Node packages through `Okojo.Node` instead of using only hand-written fixture modules.

Current probe apps:

- `app`:
  real Ink entry, expected to stop at the current wasm boundary (`yoga-layout`)
- `app-react`:
  pure JS React package probe
- `app-cli`:
  pure JS CLI package probe using `chalk`, `commander`, and `yargs`

It is intentionally a probe:

- real `package.json`
- real installed `node_modules`
- very small Ink entry
- output focused on the first real missing module or API

Setup:

```powershell
cd sandbox\OkojoInkProbe\app
pnpm install
```

Run:

```powershell
dotnet run --project sandbox\OkojoInkProbe\OkojoInkProbe.csproj
```
