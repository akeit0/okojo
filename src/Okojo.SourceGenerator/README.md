# Okojo.SourceGenerator

`Okojo.SourceGenerator` is the Roslyn source generator for Okojo export patterns.

It reads annotations from `Okojo.Annotations` and emits generated code for:

- global installer methods
- generated global property lists
- generated object export glue

## Typical use

Add both `Okojo.Annotations` and `Okojo.SourceGenerator` to a project that defines Okojo globals or objects.

```xml
<ItemGroup>
  <PackageReference Include="Okojo.Annotations" Version="*" />
  <PackageReference Include="Okojo.SourceGenerator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Then annotate your types with attributes such as `GenerateJsGlobalsAttribute` or `GenerateJsObjectAttribute`.
