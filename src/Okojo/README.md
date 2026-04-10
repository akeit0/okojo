# Okojo

`Okojo` is the core JavaScript engine package for .NET.

It provides:

- script and module evaluation
- promises and async execution
- the main embedding API (`JsRuntime`, `JsRealm`, agents, modules)

## Quick start

```csharp
using Okojo;

using var runtime = JsRuntime.Create();
var realm = runtime.MainRealm;
var value = realm.Evaluate("1 + 2");

Console.WriteLine(value);
```

Add companion packages as needed:

- `Okojo.Hosting` for host schedulers and worker helpers
- `Okojo.WebPlatform` for `fetch`, timers, workers, and server/web globals
- `Okojo.Reflection` for opt-in CLR access
- `Okojo.WebAssembly` for backend-agnostic WebAssembly integration
