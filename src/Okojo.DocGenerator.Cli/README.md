# Okojo.DocGenerator.Cli

`Okojo.DocGenerator.Cli` is a `dotnet tool` that emits TypeScript declaration files from Okojo-annotated .NET projects.

It scans a target project for types marked with:

- `GenerateJsGlobalsAttribute`
- `GenerateJsObjectAttribute`
- `DocDeclarationAttribute`
- `DocIgnoreAttribute`

## Install

```powershell
dotnet tool install --global Okojo.DocGenerator.Cli
```

## Usage

```powershell
okojo-docgen --project .\src\MyProject\MyProject.csproj --out .\artifacts\types\globals.d.ts
```

Per-type output:

```powershell
okojo-docgen --project .\src\MyProject\MyProject.csproj --out .\artifacts\types --per-type
```
