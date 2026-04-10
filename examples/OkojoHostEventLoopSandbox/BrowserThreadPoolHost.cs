using Okojo.Browser;
using Okojo.Hosting;
using Okojo.Runtime;

namespace OkojoHostEventLoopSandbox;

internal sealed class BrowserThreadPoolHost : IDisposable
{
    private readonly SandboxAssets assets;
    private readonly HttpClient httpClient;
    private readonly HostPump pump;

    private BrowserThreadPoolHost(SandboxAssets assets, JsRuntime runtime, HttpClient httpClient)
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

    public static BrowserThreadPoolHost Create(SandboxAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var moduleLoader = new DemoModuleLoader(assets);
        var httpClient = new HttpClient(new DemoFetchHandler(assets.FetchPayloads));
        var runtime = JsRuntime.CreateBuilder()
            .UseBrowserHost(browser =>
            {
                browser.ModuleSourceLoader = moduleLoader;
                browser.HttpClient = httpClient;
            })
            .Build();

        return new(assets, runtime, httpClient);
    }

    public void PumpUntil(Func<bool> completed, int timeoutMs = 2000)
    {
        pump.RunUntilOrThrow(completed, TimeSpan.FromMilliseconds(timeoutMs));
    }

    public void RunScript(string scriptPath)
    {
        _ = Runtime.MainRealm.Eval(assets.ReadScript(scriptPath));
    }

    public void RunScript(JsRealm realm, string scriptPath)
    {
        ArgumentNullException.ThrowIfNull(realm);
        _ = realm.Eval(assets.ReadScript(scriptPath));
    }
}
