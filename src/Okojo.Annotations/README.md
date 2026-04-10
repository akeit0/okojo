# Okojo.Annotations

`Okojo.Annotations` provides the attributes used to mark .NET types and members for Okojo-specific code generation.

Typical attributes include:

- `GenerateJsGlobalsAttribute`
- `GenerateJsObjectAttribute`
- `JsGlobalFunctionAttribute`
- `JsGlobalPropertyAttribute`

## Typical use

```csharp
using Okojo.Annotations;

[GenerateJsGlobals]
public static partial class ConsoleGlobals
{
    [JsGlobalFunction]
    public static string hello() => "hello";
}

[GenerateJsObject]
public partial class GeneratedObjectSample
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }

    public bool DoSomething()
    {
        return true;
    }
}
```

Use `Okojo.SourceGenerator` when you want the Roslyn generator that turns these annotations into generated installers and export glue.
