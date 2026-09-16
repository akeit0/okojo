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

Additional same-agent realms created with `runtime.CreateRealm()` share object references
without copying. When a browser document is replaced, `runtime.ReleaseRealm(childRealm)`
removes the runtime's registry ownership. Saved functions/objects and pending jobs remain
valid; the host must detach its document services separately. The main realm remains owned
until runtime disposal. Each realm has an independent module map; set
`options.ModuleSourceLoader` in `runtime.CreateRealm(options => ...)` to override its loader.
Module URLs and `import.meta.url` are unchanged. Cached modules do not independently root a
released realm through the agent, while retained namespace/function references remain valid.
`agent.Modules` diagnostics and invalidation default to the main realm; use their explicit
realm overloads to inspect or invalidate a child document's map.

Browser hosts can configure `JsRealmOptions.GlobalThisObject` while keeping realm-local global
bindings. The host integration `JsWindowProxy` supports same-agent retargeting and detachment;
create it in a long-lived anchor realm. See
[host global-this](../../docs/architecture/OKOJO_HOST_GLOBAL_THIS.md) for the contract and example.
