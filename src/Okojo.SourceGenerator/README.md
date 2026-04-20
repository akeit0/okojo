# Okojo.SourceGenerator

`Okojo.SourceGenerator` is the Roslyn source generator for Okojo export patterns.

It reads annotations from `Okojo.Annotations` and emits generated code for:

- global installer methods
- generated global property lists
- generated object export glue

Current export model:

- `[GenerateJsGlobals]` collects members annotated with `[JsMember]`, `[JsGlobalFunction]`, or `[JsGlobalProperty]`
- `[GenerateJsObject]` collects only members annotated with `[JsMember]`
- shared members can opt out per surface with `[JsIgnoreFromGlobals]` or `[JsIgnoreFromObject]`
- unnamed exports default to a JavaScript-friendly lower-first-character name

## Typical use

Add both `Okojo.Annotations` and `Okojo.SourceGenerator` to a project that defines Okojo globals or objects.

```xml
<ItemGroup>
  <PackageReference Include="Okojo.Annotations" Version="*" />
  <PackageReference Include="Okojo.SourceGenerator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Then annotate your types with attributes such as `GenerateJsGlobalsAttribute` or `GenerateJsObjectAttribute`, and mark the members you want exported with `[JsMember]`.
