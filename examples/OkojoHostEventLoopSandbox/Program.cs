using System.Text;

namespace OkojoHostEventLoopSandbox;

internal static class Program
{
    private static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var assets = SandboxAssets.CreateDefault();
        using var browserHost = BrowserThreadPoolHost.Create(assets);
        using var serverHost = ServerThreadPoolHost.Create(assets);
        using var singleThreadBrowserHost = SingleThreadBrowserHost.Create(assets);
        using var threadAffinityBrowserHost = ThreadAffinityBrowserHost.Create(assets);

        Console.WriteLine("Okojo host event loop sandbox");
        Console.WriteLine();

        SandboxDemos.RunRealmIsolationDemo(browserHost);
        Console.WriteLine();
        SandboxDemos.RunSingleThreadParallelAsyncDemo(singleThreadBrowserHost);
        Console.WriteLine();
        SandboxDemos.RunSingleThreadRenderLoopDemo(singleThreadBrowserHost);
        Console.WriteLine();
        SandboxDemos.RunThreadAffinityParallelAsyncDemo(threadAffinityBrowserHost);
        Console.WriteLine();
        SandboxDemos.RunBrowserParallelAsyncDemo(browserHost);
        Console.WriteLine();
        SandboxDemos.RunWorkerParallelDemo(browserHost);
        Console.WriteLine();
        await SandboxDemos.RunServerParallelAsyncDemo(serverHost);
        Console.WriteLine();
        await SandboxDemos.RunServerToServerCommunicationDemo(serverHost);
    }
}
