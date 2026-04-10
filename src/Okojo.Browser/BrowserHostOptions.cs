using Okojo.Runtime;

using Okojo.WebPlatform;

namespace Okojo.Browser;

public sealed class BrowserHostOptions
{
    public bool UseThreadPoolHosting { get; set; } = true;
    public IModuleSourceLoader? ModuleSourceLoader { get; set; }
    public HttpClient? HttpClient { get; set; }
    public bool InstallWebWorkers { get; set; } = true;
    public Action<WebWorkerOptions>? ConfigureWebWorkers { get; set; }
}
