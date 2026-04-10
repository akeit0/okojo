using System.Reflection;
using System.Text.Json;
using Okojo.Compiler;
using Okojo.DebugServer;
using Okojo.Hosting;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.SourceMaps;

var options = DebugServerOptions.Parse(args);
WriteVersionBanner();

if (!string.IsNullOrWhiteSpace(options.Cwd)) Directory.SetCurrentDirectory(Path.GetFullPath(options.Cwd));

if (string.IsNullOrWhiteSpace(options.ScriptPath))
{
    Console.Error.WriteLine(
        "Usage: Okojo.DebugServer --script <file.js|file.mjs> [--cwd <dir>] [--module-entry|--script-entry] [--break <source:line>] [--check-interval <n>] [--enable-source-maps] [--stop-entry] [--stop-debugger|--no-stop-debugger] [--stop-breakpoint|--no-stop-breakpoint] [--stop-call] [--stop-return] [--stop-pump] [--stop-suspend] [--stop-resume] [--stop-periodic]");
    return 2;
}

var scriptPath = NormalizePath(options.ScriptPath, options.Cwd);
var sourceMapRegistry = options.EnableSourceMaps ? new SourceMapRegistry() : null;
var moduleSourceLoader = CreateModuleSourceLoader(sourceMapRegistry);
if (sourceMapRegistry is not null)
    PreloadSourceMaps(scriptPath, sourceMapRegistry);

using var runtime = JsRuntime.Create(builder =>
{
    builder.UseThreadPoolHosting();
    builder.UseModuleSourceLoader(moduleSourceLoader);
    if (sourceMapRegistry is not null)
        builder.UseSourceMapRegistry(sourceMapRegistry);
    if (options.CheckInterval != ulong.MaxValue)
        builder.UseAgent(agent => agent.SetCheckInterval(options.CheckInterval));
});
OkojoDebugConsole.Install(runtime.MainRealm);
var session = new DebuggerSession(runtime.MainAgent, options);
runtime.MainAgent.AttachDebugger(session);

ApplyCheckpointHookSelection(runtime.MainAgent, options);
ApplyBreakpoints(runtime.MainAgent, session, options);

var commandThread = new Thread(session.RunCommandLoop)
{
    IsBackground = true,
    Name = "Okojo.DebugServer.CommandLoop"
};
commandThread.Start();

try
{
    if (options.StopOnEntry)
        if (!session.PublishEntryStopped(scriptPath))
        {
            session.PublishTerminated(0);
            return 0;
        }

    var runAsModule = options.RunAsModule ??
                      string.Equals(Path.GetExtension(scriptPath), ".mjs", StringComparison.OrdinalIgnoreCase);
    if (runAsModule)
    {
        _ = runtime.LoadModule(scriptPath);
    }
    else
    {
        var source = runtime.ModuleSourceLoader.LoadSource(scriptPath);
        var program = JavaScriptParser.ParseScript(source, scriptPath);
        var script = JsCompiler.Compile(runtime.MainRealm, program);
        runtime.MainRealm.Execute(script, options.PumpJobsAfterRun);
    }

    session.PublishTerminated(0);
    return 0;
}
catch (Exception ex)
{
    session.PublishError(ex);
    session.PublishTerminated(1);
    return 1;
}
finally
{
    session.StopCommandLoop();
}

void ApplyCheckpointHookSelection(JsAgent agent, DebugServerOptions options)
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

void ApplyBreakpoints(JsAgent agent, DebuggerSession session, DebugServerOptions options)
{
    foreach (var breakpoint in options.Breakpoints)
    {
        var sourcePath = NormalizePath(breakpoint.SourcePath, options.Cwd);
        session.AddBreakpoint(sourcePath, breakpoint.Line);
    }
}

IModuleSourceLoader CreateModuleSourceLoader(SourceMapRegistry? registry)
{
    var fileLoader = new FileModuleSourceLoader();
    return registry is null
        ? fileLoader
        : new SourceMapModuleSourceLoader(fileLoader, registry);
}

void PreloadSourceMaps(string rootScriptPath, SourceMapRegistry registry)
{
    var rootDirectory = Path.GetDirectoryName(rootScriptPath);
    if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
        return;

    foreach (var sourceMapPath in Directory.EnumerateFiles(rootDirectory, "*.map", SearchOption.AllDirectories))
    {
        var generatedPath = sourceMapPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
            ? sourceMapPath[..^4]
            : sourceMapPath;
        if (!File.Exists(generatedPath) || registry.TryGetDocument(generatedPath, out _))
            continue;

        try
        {
            registry.Register(SourceMapParser.Parse(File.ReadAllText(sourceMapPath), generatedPath, sourceMapPath));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (FormatException)
        {
        }
        catch (JsonException)
        {
        }
    }
}

string NormalizePath(string path, string? cwd)
{
    if (Path.IsPathRooted(path))
        return Path.GetFullPath(path);

    return cwd is { Length: > 0 }
        ? Path.GetFullPath(path, Path.GetFullPath(cwd))
        : Path.GetFullPath(path);
}

void WriteVersionBanner()
{
    var assembly = Assembly.GetExecutingAssembly();
    var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    Console.Error.WriteLine($"[okojo] debug server {info}");
}
