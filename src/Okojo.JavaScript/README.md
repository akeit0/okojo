# Okojo.JavaScript

`Okojo.JavaScript` is the ECMAScript engine package for .NET.

It provides:

- script and module evaluation
- promises and async execution
- realms, agents, modules, values, objects, intrinsics, and execution

Embedding composition lives in `Okojo.JavaScript.Embedding`:

```csharp
using Okojo.Runtime;

using var runtime = JsRuntime.Create();
var realm = runtime.MainRealm;
var value = realm.Evaluate("1 + 2");

Console.WriteLine(value);
```

Add host/profile packages as needed:

- `Okojo.Hosting` for host schedulers and worker helpers
- `Okojo.WebPlatform` for `fetch`, timers, workers, and server/web globals
- `Okojo.Reflection` for opt-in CLR access
- `Okojo.WebAssembly` for backend-agnostic WebAssembly integration
