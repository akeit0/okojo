# Okojo.Hosting

`Okojo.Hosting` adds host-owned scheduling and worker helpers on top of the core Okojo runtime.

It includes:

- thread-pool hosting defaults
- host task queues and loop helpers
- worker runtime helpers
- manual and thread-affinity host loop building blocks

## Typical use

```csharp
using Okojo;
using Okojo.Hosting;

using var runtime = JsRuntime.CreateBuilder()
    .UseThreadPoolHosting()
    .Build();
```

Pair this package with `Okojo.WebPlatform` when the host also needs timers, `fetch`, or workers.
