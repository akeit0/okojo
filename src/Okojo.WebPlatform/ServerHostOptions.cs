using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.WebPlatform;

public sealed class ServerHostOptions
{
    public bool UseThreadPoolHosting { get; set; } = true;
    public IModuleSourceLoader? ModuleSourceLoader { get; set; }
    public HttpClient? HttpClient { get; set; }
    public IList<IRealmApiModule> RealmApiModules { get; } = [];
    public IList<Action<JsRealm>> RealmSetups { get; } = [];
}
