# Okojo.WebPlatform

`Okojo.WebPlatform` adds portable web/server host APIs to Okojo runtimes.

It includes:

- `fetch`
- timers and delay scheduling hooks
- worker APIs
- web-style and server-style runtime setup helpers
- host-installed globals

## Typical use

```csharp
using Okojo;
using Okojo.Hosting;
using Okojo.WebPlatform;

using var runtime = JsRuntime.CreateBuilder()
    .UseThreadPoolHosting()
    .UseFetch()
    .UseWebWorkers()
    .Build();
```
