using System.Globalization;
using Okojo.DebugServer;
using Okojo.Runtime;

namespace Okojo.Node.Cli;

internal sealed class NodeCliDebuggerHost : IDisposable
{
    private const ulong DefaultCheckInterval = 1024;
    private readonly NodeCliInspectConsole console;

    private readonly DebugServerOptions options;
    private readonly DebuggerSession session;

    private NodeCliDebuggerHost(DebugServerOptions options, DebuggerSession session, NodeCliInspectConsole console)
    {
        this.options = options;
        this.session = session;
        this.console = console;
    }

    public bool ShouldStopOnEntry => options.StopOnEntry;

    public void Dispose()
    {
        console.Dispose();
        session.StopCommandLoop();
    }

    public static NodeCliDebuggerHost? TryCreate(NodeRuntime runtime, NodeCliOptions cli)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(cli);

        if (!cli.IsInspectEnabled)
            return null;

        var options = CreateOptions(cli);
        runtime.Runtime.MainAgent.SetCheckInterval(options.CheckInterval);

        var workingDirectory = cli.ScriptPath is null
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(cli.ScriptPath))!;
        var entrySourcePath = cli.ScriptPath is null ? null : Path.GetFullPath(cli.ScriptPath);

        var sourceMaps = runtime.SourceMapRegistry;
        NodeCliInspectConsole? console = null;
        var session = new DebuggerSession(runtime.Runtime.MainAgent, options, line => console!.OnSessionOutput(line));
        console = new(session, workingDirectory, entrySourcePath, sourceMaps);
        runtime.Runtime.MainAgent.AttachDebugger(session);
        ApplyCheckpointHookSelection(runtime.Runtime.MainAgent, options);
        console.Start();

        return new(options, session, console);
    }

    public bool PublishEntryStopped(string sourcePath)
    {
        return session.PublishEntryStopped(sourcePath);
    }

    public void PublishError(Exception ex)
    {
        session.PublishError(ex);
    }

    public void PublishTerminated(int exitCode)
    {
        session.PublishTerminated(exitCode);
    }

    private static DebugServerOptions CreateOptions(NodeCliOptions cli)
    {
        var args = new List<string>
        {
            "--check-interval",
            DefaultCheckInterval.ToString(CultureInfo.InvariantCulture)
        };

        if (cli.InspectMode == NodeCliInspectMode.Break)
            args.Add("--stop-entry");

        return DebugServerOptions.Parse(args.ToArray());
    }

    private static void ApplyCheckpointHookSelection(JsAgent agent, DebugServerOptions options)
    {
        if (options.StopOnDebuggerStatement)
            agent.EnableDebuggerStatementHook();
        else
            agent.DisableDebuggerStatementHook();

        if (options.StopOnBreakpoint)
            agent.EnableBreakpointHook();
        else
            agent.DisableBreakpointHook();

        if (options.StopOnCall)
            agent.EnableCallHook();
        else
            agent.DisableCallHook();

        if (options.StopOnReturn)
            agent.EnableReturnHook();
        else
            agent.DisableReturnHook();

        if (options.StopOnPump)
            agent.EnablePumpHook();
        else
            agent.DisablePumpHook();

        if (options.StopOnSuspendGenerator)
            agent.EnableSuspendGeneratorHook();
        else
            agent.DisableSuspendGeneratorHook();

        if (options.StopOnResumeGenerator)
            agent.EnableResumeGeneratorHook();
        else
            agent.DisableResumeGeneratorHook();
    }
}
