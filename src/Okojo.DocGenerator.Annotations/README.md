# Okojo.DocGenerator.Annotations

`Okojo.DocGenerator.Annotations` provides attributes that control declaration-file generation for Okojo documentation tooling.

Useful attributes include:

- `DocDeclarationAttribute`
- `DocIgnoreAttribute`

## Typical use

```csharp
using Okojo.DocGenerator.Annotations;

[DocDeclaration("console")]
public static partial class ConsoleGlobals
{
    [DocIgnore]
    public static string InternalOnly => "hidden";
}
```

Use `Okojo.DocGenerator.Cli` when you want to turn annotated projects into emitted declaration files.
