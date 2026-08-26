# Okojo.JavaScript.Embedding

`Okojo.JavaScript.Embedding` provides runtime composition and host integration
contracts for the `Okojo.JavaScript` ECMAScript engine. It does not select a
thread, task scheduler, event loop, worker host, or message serializer.

```csharp
using Okojo.JavaScript.Embedding;

using var runtime = JsRuntime.Create();
Console.WriteLine(runtime.MainRealm.Evaluate("1 + 2"));
```

Add `Okojo.Hosting`, `Okojo.WebPlatform`, or another profile package when host
policy and platform APIs are needed.
