# Okojo .NET Module Resolution Note

## Scope for this iteration

- create a separate optional `src\Okojo.DotNet.Modules` package
- add cache primitives for the file-based app build cache and NuGet global-packages cache
- keep the future runtime surface on normal JavaScript `import` / `export` syntax with specifier schemes rather than adding new JS grammar
- add a first real JS-to-.NET module path for `dll:` and `nuget:` imports through a generated source-module wrapper and a hidden host bridge

## Minimal repros

```csharp
var localDll = DotNetModuleReference.AssemblyFile(@".\packages\MyInterop.dll");
var package = DotNetModuleReference.Package("Newtonsoft.Json", "13.0.3");
var project = DotNetModuleReference.Project("../SharedLib/SharedLib.csproj");
```

```javascript
import Uri from "dll:./packages/MyInterop.dll#MyInterop.Uri";
import JsonConvert from "nuget:Newtonsoft.Json@13.0.3#Newtonsoft.Json.JsonConvert";
import "dll:./packages/MyInterop.dll";
const Uri2 = clr.MyInterop.Uri;
```

## Planned tests

- keep cache-key fingerprints stable for the same module-reference set and sdk input
- honor `NUGET_PACKAGES` override and default temp-path runfile cache layout
- cover end-to-end `dll:` and `nuget:` imports through the public module API

## Reference observations

- .NET 10 file-based apps use `#:package`, `#:project`, `#:property`, and `#:sdk`
- build outputs are cached under `<temp>\dotnet\runfile\<app>-<fingerprint>\...`
- NuGet package payloads still come from the normal global packages cache
- JavaScript module syntax already gives the right embedder surface; the extensibility point should be specifier resolution, not parser changes
- the current first real runtime path uses `#...` when the module should resolve directly to a CLR namespace/type, and bare `dll:` / `nuget:` imports act as assembly loaders for the existing global `clr` namespace
- NuGet resolution currently targets the global packages cache `lib\<tfm>\*.dll` layout

## Copy vs intentional difference

- copy: use the .NET cache layout shape and package-cache expectations
- intentional difference: reference .NET file-based-app syntax as input vocabulary and cache guidance, but keep the actual Okojo runtime model on standard JS `import` specifiers and explicit `DotNetModuleReference` values instead of a `.cs` directive parser

## Perf plan

- keep module-reference and cache helpers allocation-light
- use content-based hashing for cache keys; defer actual restore/build orchestration to a later slice
