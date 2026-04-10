namespace Okojo.DebugServer;

public sealed class DebugServerOptions
{
    private readonly List<BreakpointSpec> breakpoints = new();

    public string? ScriptPath { get; private set; }
    public string? Cwd { get; private set; }
    public ulong CheckInterval { get; private set; } = ulong.MaxValue;
    public bool PumpJobsAfterRun { get; private set; } = true;
    public bool? RunAsModule { get; private set; }
    public bool StopOnEntry { get; private set; }
    public bool StopOnDebuggerStatement { get; private set; } = true;
    public bool StopOnBreakpoint { get; private set; } = true;
    public bool StopOnCall { get; private set; }
    public bool StopOnReturn { get; private set; }
    public bool StopOnPump { get; private set; }
    public bool StopOnSuspendGenerator { get; private set; }
    public bool StopOnResumeGenerator { get; private set; }
    public bool StopOnPeriodic { get; private set; }
    public bool EnableSourceMaps { get; private set; }
    public DebugStepGranularity StepGranularity { get; private set; } = DebugStepGranularity.Line;
    public IReadOnlyList<BreakpointSpec> Breakpoints => breakpoints;

    public static DebugServerOptions Parse(string[] args)
    {
        var options = new DebugServerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--script":
                    if (i + 1 < args.Length)
                        options.ScriptPath = args[++i];
                    break;
                case "--cwd":
                    if (i + 1 < args.Length)
                        options.Cwd = args[++i];
                    break;
                case "--break":
                    if (i + 1 < args.Length)
                        options.breakpoints.Add(ParseBreakpoint(args[++i], options.Cwd));
                    break;
                case "--check-interval":
                    if (i + 1 < args.Length && ulong.TryParse(args[++i], out var checkInterval) && checkInterval != 0)
                        options.CheckInterval = checkInterval;
                    break;
                case "--step-granularity":
                    if (i + 1 < args.Length)
                        options.StepGranularity = ParseStepGranularity(args[++i]);
                    break;
                case "--enable-source-maps":
                    options.EnableSourceMaps = true;
                    break;
                case "--no-pump-after-run":
                    options.PumpJobsAfterRun = false;
                    break;
                case "--module-entry":
                    options.RunAsModule = true;
                    break;
                case "--script-entry":
                    options.RunAsModule = false;
                    break;
                case "--stop-entry":
                case "--stop-on-entry":
                    options.StopOnEntry = true;
                    break;
                case "--no-stop-entry":
                    options.StopOnEntry = false;
                    break;
                case "--no-stop-debugger":
                    options.StopOnDebuggerStatement = false;
                    break;
                case "--stop-debugger":
                    options.StopOnDebuggerStatement = true;
                    break;
                case "--no-stop-breakpoint":
                    options.StopOnBreakpoint = false;
                    break;
                case "--stop-breakpoint":
                    options.StopOnBreakpoint = true;
                    break;
                case "--stop-call":
                    options.StopOnCall = true;
                    break;
                case "--stop-return":
                    options.StopOnReturn = true;
                    break;
                case "--stop-pump":
                    options.StopOnPump = true;
                    break;
                case "--stop-suspend":
                    options.StopOnSuspendGenerator = true;
                    break;
                case "--stop-resume":
                    options.StopOnResumeGenerator = true;
                    break;
                case "--stop-periodic":
                    options.StopOnPeriodic = true;
                    break;
                case "--stop-all":
                    options.StopOnCall = true;
                    options.StopOnReturn = true;
                    options.StopOnPump = true;
                    options.StopOnSuspendGenerator = true;
                    options.StopOnResumeGenerator = true;
                    options.StopOnPeriodic = true;
                    break;
            }
        }

        return options;
    }

    private static BreakpointSpec ParseBreakpoint(string spec, string? cwd)
    {
        int colon = spec.LastIndexOf(':');
        if (colon <= 0 || colon == spec.Length - 1 || !int.TryParse(spec[(colon + 1)..], out int line))
            throw new ArgumentException("Breakpoint must be in the form sourcePath:line.", nameof(spec));

        string sourcePath = spec[..colon];
        if (!Path.IsPathRooted(sourcePath))
        {
            sourcePath = cwd is { Length: > 0 }
                ? Path.GetFullPath(sourcePath, Path.GetFullPath(cwd))
                : Path.GetFullPath(sourcePath);
        }
        else
        {
            sourcePath = Path.GetFullPath(sourcePath);
        }

        return new BreakpointSpec(sourcePath, line);
    }

    private static DebugStepGranularity ParseStepGranularity(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "line" => DebugStepGranularity.Line,
            "instruction" => DebugStepGranularity.Instruction,
            "pc" => DebugStepGranularity.Instruction,
            _ => throw new ArgumentException($"Unknown step granularity '{raw}'.", nameof(raw))
        };
    }
}

public readonly record struct BreakpointSpec(string SourcePath, int Line);
