using System.Text;

namespace OkojoGameLoopSandbox;

internal static class Program
{
    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var assets = GameSandboxAssets.CreateDefault();

        Console.WriteLine("Okojo game/app sandbox");
        Console.WriteLine();

        GameSandboxDemos.RunCooperativeFrameLoopDemo(assets);
        Console.WriteLine();
        GameSandboxDemos.RunRunawaySyncDemo(assets);
        Console.WriteLine();
        GameSandboxDemos.RunRunawayAsyncDemo(assets);
    }
}
