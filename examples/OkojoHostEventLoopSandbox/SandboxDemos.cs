namespace OkojoHostEventLoopSandbox;

internal static class SandboxDemos
{
    public static void RunSingleThreadParallelAsyncDemo(SingleThreadBrowserHost host)
    {
        host.RunScript("/browser/single-thread-parallel.js");
        host.PumpUntil(() => host.MainRealm.Global["singleThreadState"].AsString() == "ready");

        Console.WriteLine("single-thread parallel async flow:");
        Console.WriteLine("  state  = " + host.MainRealm.Global["singleThreadState"].AsString());
        Console.WriteLine("  events = " + host.MainRealm.Eval("singleThreadEvents.join(',')").AsString());
    }

    public static void RunSingleThreadRenderLoopDemo(SingleThreadBrowserHost host)
    {
        host.RunScript("/browser/render-loop.js");
        host.PumpUntil(() => host.MainRealm.Global["renderState"].AsString() == "ready");

        Console.WriteLine("single-thread render loop:");
        Console.WriteLine("  state  = " + host.MainRealm.Global["renderState"].AsString());
        Console.WriteLine("  frame  = " + host.MainRealm.Global["renderTimestamp"].NumberValue);
        Console.WriteLine("  events = " + host.MainRealm.Eval("renderEvents.join(',')").AsString());
    }

    public static void RunThreadAffinityParallelAsyncDemo(ThreadAffinityBrowserHost host)
    {
        host.RunScript("/browser/threadpool-parallel.js");
        host.PumpUntil(() => host.MainRealm.Global["bootState"].AsString() == "ready");

        Console.WriteLine("thread-affinity async flow:");
        Console.WriteLine("  state  = " + host.MainRealm.Global["bootState"].AsString());
        Console.WriteLine("  events = " + host.MainRealm.Eval("events.join(',')").AsString());
    }

    public static void RunRealmIsolationDemo(BrowserThreadPoolHost host)
    {
        var mainRealm = host.MainRealm;
        var isolatedRealm = host.Runtime.CreateRealm();

        host.RunScript(mainRealm, "/browser/realm-main.js");
        host.RunScript(isolatedRealm, "/browser/realm-secondary.js");

        var summary = string.Join(" | ",
            mainRealm.Eval("[realmName, counter, typeof fetch, typeof Worker].join(':')").AsString(),
            isolatedRealm.Eval("[realmName, counter, typeof fetch, typeof Worker].join(':')").AsString());

        Console.WriteLine("realm isolation:");
        Console.WriteLine("  " + summary);
    }

    public static void RunBrowserParallelAsyncDemo(BrowserThreadPoolHost host)
    {
        host.RunScript("/browser/threadpool-parallel.js");
        host.PumpUntil(() => host.MainRealm.Global["bootState"].AsString() == "ready");

        Console.WriteLine("browser parallel async flow:");
        Console.WriteLine("  state  = " + host.MainRealm.Global["bootState"].AsString());
        Console.WriteLine("  events = " + host.MainRealm.Eval("events.join(',')").AsString());
    }

    public static void RunWorkerParallelDemo(BrowserThreadPoolHost host)
    {
        host.RunScript("/browser/worker-main.js");
        host.PumpUntil(() => host.MainRealm.Eval("workerTrace.length").Int32Value == 2);

        Console.WriteLine("worker multi-agent parallel flow:");
        Console.WriteLine("  trace  = " + host.MainRealm.Eval("workerTrace.join(',')").AsString());
        Console.WriteLine("  agents = " + host.Runtime.Agents.Count);
    }

    public static async Task RunServerParallelAsyncDemo(ServerThreadPoolHost host)
    {
        var result = host.LoadModule("/server/main.js");
        var module = await result.ToTask().ConfigureAwait(false);
        var state = module.GetExport("serverState");
        var fetchedKind = module.GetExport("fetchedKind");
        var summary = module.GetExport("summary");
        var tla = module.GetExport("tlaStage");
        var microtask = module.GetExport("microtaskStage");
        var kinds = module.GetExport("kinds");

        Console.WriteLine("server parallel async flow:");
        Console.WriteLine("  async? = " + !result.IsCompleted);
        Console.WriteLine("  state  = " + state.AsString());
        Console.WriteLine("  fetch  = " + fetchedKind.AsString());
        Console.WriteLine("  all    = " + summary.AsString());
        Console.WriteLine("  tla    = " + tla.AsString());
        Console.WriteLine("  micro  = " + microtask.AsString());
        Console.WriteLine("  log    = " + host.MainRealm.Eval("serverLog.join(',')").AsString());
        Console.WriteLine("  kinds  = " + kinds.AsString());
    }

    public static async Task RunServerToServerCommunicationDemo(ServerThreadPoolHost host)
    {
        host.MainRealm.Eval("globalThis.serverLog = [];");
        var result = host.LoadModule("/server/gateway.js");
        var module = await result.ToTask().ConfigureAwait(false);

        Console.WriteLine("server to server flow:");
        Console.WriteLine("  state  = " + module.GetExport("gatewayState").AsString());
        Console.WriteLine("  user   = " + module.GetExport("userName").AsString());
        Console.WriteLine("  count  = " + module.GetExport("actionCount").Int32Value);
        Console.WriteLine("  all    = " + module.GetExport("gatewaySummary").AsString());
        Console.WriteLine("  log    = " + host.MainRealm.Eval("serverLog.join(',')").AsString());
    }
}
