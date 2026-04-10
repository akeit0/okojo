# Okojo CLI App Sandbox

This is a real packaged Node-style CLI app running on `Okojo.Node`.

It uses:

- `chalk`
- `cliui`
- `commander`
- `yargs`

The C# runner executes the same app with real `process.argv` in two scenarios:

- `greet --name okojo --times 2`
- `inspect alpha beta --upper --repeat 2`
- `report --config app.config.json`
- `explain --doc project.explain.json --width 68`

Install dependencies:

```powershell
cd sandbox\OkojoCliAppSandbox\app
pnpm install
```

Run the demo batch:

```powershell
dotnet run --project sandbox\OkojoCliAppSandbox\OkojoCliAppSandbox.csproj
```

Run a single CLI command through the Okojo host:

```powershell
dotnet run --project sandbox\OkojoCliAppSandbox\OkojoCliAppSandbox.csproj -- explain --doc project.explain.json --width 68
```

AI/escaped output mode:

```powershell
dotnet run --project sandbox\OkojoCliAppSandbox\OkojoCliAppSandbox.csproj -- --escape-output
```
