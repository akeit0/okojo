# Okojo.JavaScript.Embedding

`Okojo.JavaScript.Embedding` provides embedding composition and host integration
for the `Okojo.JavaScript` ECMAScript engine.

```csharp
using Okojo.Runtime;

using var runtime = JsRuntime.Create();
Console.WriteLine(runtime.MainRealm.Evaluate("1 + 2"));
```

Add `Okojo.Hosting`, `Okojo.WebPlatform`, or another profile package when host
policy and platform APIs are needed.
