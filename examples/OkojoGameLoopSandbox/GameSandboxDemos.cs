namespace OkojoGameLoopSandbox;

internal static class GameSandboxDemos
{
    public static void RunCooperativeFrameLoopDemo(GameSandboxAssets assets)
    {
        var budget = FrameBudget.Create(20_000, TimeSpan.FromMilliseconds(8));
        using var host = GameSandboxHost.Create(assets, budget);
        var module = host.LoadModule("/game/cooperative/main.js");
        var result = host.RunFrames(module, 12, budget, 30);

        Console.WriteLine("cooperative 30 fps frame loop:");
        Console.WriteLine("  completed = " + result.Completed);
        Console.WriteLine("  frames    = " + result.FrameCount);
        Console.WriteLine("  snapshot  = " + host.GetSnapshot(module));
        Console.WriteLine("  app log   = " + string.Join(", ", host.AppLogs));
        Console.WriteLine("  signals   = " + string.Join(", ", host.AppSignals));
        Console.WriteLine("  trace     = " + module.CallExport("debugTrace").AsString());
    }

    public static void RunRunawaySyncDemo(GameSandboxAssets assets)
    {
        var budget = FrameBudget.Create(8_000, TimeSpan.FromMilliseconds(4));
        using var host = GameSandboxHost.Create(assets, budget);
        var module = host.LoadModule("/game/runaway-sync/main.js");
        var result = host.RunFrames(module, 5, budget, 30);

        Console.WriteLine("runaway sync script:");
        Console.WriteLine("  completed = " + result.Completed);
        Console.WriteLine("  frames    = " + result.FrameCount);
        Console.WriteLine("  error     = " + result.ErrorCode);
        Console.WriteLine("  message   = " + result.ErrorMessage);
    }

    public static void RunRunawayAsyncDemo(GameSandboxAssets assets)
    {
        var budget = FrameBudget.Create(6_000, TimeSpan.FromMilliseconds(4));
        using var host = GameSandboxHost.Create(assets, budget);
        var module = host.LoadModule("/game/runaway-async/main.js");
        var result = host.RunFrames(module, 5, budget, 30);

        Console.WriteLine("runaway async script:");
        Console.WriteLine("  completed = " + result.Completed);
        Console.WriteLine("  frames    = " + result.FrameCount);
        Console.WriteLine("  error     = " + result.ErrorCode);
        Console.WriteLine("  message   = " + result.ErrorMessage);
        Console.WriteLine("  app log   = " + string.Join(", ", host.AppLogs));
    }
}
