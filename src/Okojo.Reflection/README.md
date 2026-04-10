# Okojo.Reflection

`Okojo.Reflection` enables the reflection-backed CLR interop surface for Okojo.

Use this package only when the embedder wants CLR namespace/type access or reflection-based host binding behavior.

## Opt in

```csharp
using Okojo;
using Okojo.Reflection;

using var runtime = JsRuntime.CreateBuilder()
    .AllowClrAccess(typeof(MyHostType).Assembly)
    .Build();
```

`Okojo.Reflection` is intentionally separate from the core package so NativeAOT-friendly embeddings can stay on the
non-reflection path by default.
