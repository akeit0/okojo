using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.WebPlatform;

public sealed class ServerRuntimeOptions
{
    public HttpClient? HttpClient { get; set; }
    public IList<IRealmApiModule> RealmApiModules { get; } = [];
    public IList<Action<JsRealm>> RealmSetups { get; } = [];
}
