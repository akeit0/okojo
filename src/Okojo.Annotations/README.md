# Okojo.Annotations

`Okojo.Annotations` provides the attributes used to mark .NET types and members for Okojo-specific code generation.

Typical attributes include:

- `GenerateJsGlobalsAttribute`
- `GenerateJsObjectAttribute`
- `JsMemberAttribute`
- `JsGlobalFunctionAttribute`
- `JsGlobalPropertyAttribute`
- `JsIgnoreFromGlobalsAttribute`
- `JsIgnoreFromObjectAttribute`

## Typical use

```csharp
using Okojo.Annotations;

[GenerateJsGlobals]
public static partial class ConsoleGlobals
{
    [JsMember]
    public static string Hello() => "hello";
}

[GenerateJsObject]
public partial class GeneratedObjectSample
{
    [JsMember]
    public string Name { get; set; } = string.Empty;

    [JsMember]
    public bool DoSomething()
    {
        return true;
    }
}
```

Notes:

- `[GenerateJsObject]` is explicit opt-in: only `[JsMember]` members are exported
- `[GenerateJsGlobals]` can use `[JsMember]` for shared naming/defaults, or `[JsGlobalFunction]` / `[JsGlobalProperty]` when you need function/property-specific options
- `[JsIgnoreFromGlobals]` and `[JsIgnoreFromObject]` let one member participate in only one export surface
- default generated JavaScript names lowercase the first character unless you provide an explicit name

Use `Okojo.SourceGenerator` when you want the Roslyn generator that turns these annotations into generated installers and export glue.
