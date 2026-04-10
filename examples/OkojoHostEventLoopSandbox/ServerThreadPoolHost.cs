using Okojo;
using Okojo.Hosting;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.WebPlatform;

namespace OkojoHostEventLoopSandbox;

internal sealed class ServerThreadPoolHost : IDisposable
{
    private readonly SandboxAssets assets;
    private readonly HttpClient httpClient;
    private readonly HostPump pump;

    private ServerThreadPoolHost(SandboxAssets assets, JsRuntime runtime, HttpClient httpClient)
    {
        this.assets = assets;
        this.Runtime = runtime;
        this.httpClient = httpClient;
        pump = runtime.CreateHostPump();
    }

    public JsRuntime Runtime { get; }

    public JsRealm MainRealm => Runtime.MainRealm;

    public void Dispose()
    {
        Runtime.Dispose();
        httpClient.Dispose();
    }

    public static ServerThreadPoolHost Create(SandboxAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var moduleLoader = new DemoModuleLoader(assets);
        var httpClient = new HttpClient(new DemoFetchHandler(assets.FetchPayloads));
        var runtime = JsRuntime.CreateBuilder()
            .UseServerHost(server =>
            {
                server.ModuleSourceLoader = moduleLoader;
                server.HttpClient = httpClient;
                server.RealmSetups.Add(static realm =>
                {
                    if (realm.Global.TryGetValue("host", out _))
                        return;

                    var hostObject = new JsPlainObject(realm);
                    hostObject.DefineDataProperty("name", JsValue.FromString("server"), JsShapePropertyFlags.Open);
                    hostObject.DefineDataProperty("platform", JsValue.FromString(".NET"), JsShapePropertyFlags.Open);
                    realm.Global["host"] = JsValue.FromObject(hostObject);
                });
            })
            .Build();

        return new(assets, runtime, httpClient);
    }

    public void PumpUntil(Func<bool> completed, int timeoutMs = 2000)
    {
        pump.RunUntilOrThrow(completed, TimeSpan.FromMilliseconds(timeoutMs));
    }

    public JsModuleLoadResult LoadModule(string specifier)
    {
        return Runtime.LoadModule(specifier);
    }
}
