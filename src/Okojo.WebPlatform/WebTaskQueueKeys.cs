using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.WebPlatform;

public static class WebTaskQueueKeys
{
    public static HostTaskQueueKey Timers { get; } = new("timers");
    public static HostTaskQueueKey Messages { get; } = new("messages");
    public static HostTaskQueueKey Network { get; } = new("network");
    public static HostTaskQueueKey Rendering { get; } = new("rendering");
}
