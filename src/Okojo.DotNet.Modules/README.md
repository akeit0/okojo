# Okojo.DotNet.Modules

`Okojo.DotNet.Modules` provides .NET ecosystem module-resolution primitives for Okojo embedders.

The first slice is intentionally small:

- model future module references for NuGet packages, project references, and local assembly files
- expose cache-location and cache-key helpers aligned with the .NET file-based app workflow and NuGet global packages
- provide a real module-loader wrapper for standard JS `import` specifiers such as `dll:` and `nuget:`

This package does **not** add JS syntax. The runtime surface is standard JavaScript `import` with resolver-owned specifiers
such as `import Uri from "dll:C:\\libs\\MyInterop.dll#MyInterop.Uri"` or
`import JsonConvert from "nuget:Newtonsoft.Json@13.0.3#Newtonsoft.Json.JsonConvert"`, not a parser extension on the
JavaScript side. The current implementation loads CLR assemblies through the existing reflection-backed CLR access path and
keeps NuGet/local-DLL resolution policy out of `Okojo` core while leaving room for future reflection and IL-emit backends.

When a specifier omits `#Clr.Path`, the import acts as an assembly loader for the existing global `clr` namespace. For
example, `import "dll:C:\\libs\\MyInterop.dll";` makes the assembly visible through `clr.MyInterop...`, and a default import
without a CLR path returns the root `clr` namespace object.
