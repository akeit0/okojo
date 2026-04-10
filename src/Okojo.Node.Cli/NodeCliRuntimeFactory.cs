using Okojo.Runtime;
using Okojo.SourceMaps;
using Okojo.WebAssembly.Wasmtime;

namespace Okojo.Node.Cli;

internal static class NodeCliRuntimeFactory
{
    public static NodeRuntime CreateRuntime(IHostTaskScheduler hostTaskScheduler, bool enableSourceMaps)
    {
        ArgumentNullException.ThrowIfNull(hostTaskScheduler);

        var sourceMapRegistry = enableSourceMaps ? new SourceMapRegistry() : null;
        return NodeRuntime.CreateBuilder()
            .ConfigureRuntime(builder =>
            {
                builder.UseLowLevelHost(host => host.UseTaskScheduler(hostTaskScheduler));
                if (sourceMapRegistry is not null)
                    builder.UseSourceMapRegistry(sourceMapRegistry);
            })
            .ConfigureTerminal(terminal =>
            {
                terminal.Stdout = Console.Out;
                terminal.Stderr = Console.Error;
                terminal.StdinIsTty = !Console.IsInputRedirected;
                terminal.StdoutIsTty = !Console.IsOutputRedirected;
                terminal.StderrIsTty = !Console.IsErrorRedirected;
                if (terminal.StdoutIsTty)
                {
                    terminal.StdoutColumns = Console.WindowWidth;
                    terminal.StdoutRows = Console.WindowHeight;
                }

                if (terminal.StderrIsTty)
                {
                    terminal.StderrColumns = Console.WindowWidth;
                    terminal.StderrRows = Console.WindowHeight;
                }
            })
            .UseWebAssembly(wasm => wasm
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .Build();
    }
}
