namespace Okojo.Node.Cli;

internal sealed record NodeCliOptions(
    string? ScriptPath,
    IReadOnlyList<string> Expressions,
    IReadOnlyList<string> ScriptArguments,
    int StrictMode,
    bool ShowHelp,
    bool PrintEvalResult,
    string? EnvFilePath,
    bool PrintBytecode,
    bool EnableSourceMaps,
    NodeCliInspectMode InspectMode)
{
    public bool IsInspectEnabled => InspectMode != NodeCliInspectMode.None;

    public static NodeCliOptions Parse(string[] args)
    {
        var inspectCommand = args.Length > 0 && string.Equals(args[0], "inspect", StringComparison.Ordinal);
        if (inspectCommand)
            args = args[1..];

        if (args.Length == 0)
            return new(
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                ReplStrictMode.Auto,
                false,
                false,
                null,
                false,
                false,
                inspectCommand ? NodeCliInspectMode.Break : NodeCliInspectMode.None);

        string? scriptPath = null;
        var expressions = new List<string>();
        var trailingArguments = new List<string>();
        var showHelp = false;
        var stopOptionParsing = false;
        var strict = false;
        var noStrict = false;
        var printEvalResult = false;
        string? envFilePath = null;
        var printBytecode = false;
        var enableSourceMaps = false;
        var inspectMode = inspectCommand ? NodeCliInspectMode.Break : NodeCliInspectMode.None;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!stopOptionParsing)
            {
                if (TryReadInlineOption(arg, "--eval", out var inlineEval))
                {
                    expressions.Add(inlineEval!);
                    continue;
                }

                if (TryReadInlineOption(arg, "--print", out var inlinePrint))
                {
                    printEvalResult = true;
                    expressions.Add(inlinePrint!);
                    continue;
                }

                if (TryReadInlineOption(arg, "--env-file", out var inlineEnvFile))
                {
                    envFilePath = string.IsNullOrWhiteSpace(inlineEnvFile) ? ".env" : inlineEnvFile;
                    continue;
                }

                switch (arg)
                {
                    case "-h":
                    case "--help":
                        showHelp = true;
                        continue;
                    case "--":
                        stopOptionParsing = true;
                        continue;
                    case "-e":
                    case "--eval":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("Missing code after -e/--eval.");
                        expressions.Add(args[++i]);
                        continue;
                    case "-p":
                    case "--print":
                        printEvalResult = true;
                        if (i + 1 < args.Length && !LooksLikeOption(args[i + 1])) expressions.Add(args[++i]);
                        continue;
                    case "--strict":
                        strict = true;
                        continue;
                    case "--no-strict":
                        noStrict = true;
                        continue;
                    case "--env-file":
                        envFilePath = i + 1 < args.Length && !LooksLikeOption(args[i + 1]) ? args[++i] : ".env";
                        continue;
                    case "--print-bytecode":
                        printBytecode = true;
                        continue;
                    case "--enable-source-maps":
                        enableSourceMaps = true;
                        continue;
                    case "--inspect":
                        inspectMode = NodeCliInspectMode.Inspect;
                        continue;
                    case "--inspect-brk":
                        inspectMode = NodeCliInspectMode.Break;
                        continue;
                }
            }

            if (scriptPath is null && expressions.Count == 0)
            {
                scriptPath = arg;
                stopOptionParsing = true;
            }
            else
            {
                trailingArguments.Add(arg);
            }
        }

        if (strict && noStrict)
            throw new ArgumentException("Cannot combine --strict and --no-strict.");
        if (printEvalResult && expressions.Count == 0 && scriptPath is null)
            throw new ArgumentException("Expected an expression after -p/--print or combine it with -e.");

        var strictMode = strict ? ReplStrictMode.Strict : noStrict ? ReplStrictMode.Sloppy : ReplStrictMode.Auto;
        return new(
            scriptPath,
            expressions,
            trailingArguments,
            strictMode,
            showHelp,
            printEvalResult,
            envFilePath,
            printBytecode,
            enableSourceMaps,
            inspectMode);
    }

    private static bool TryReadInlineOption(string arg, string optionName, out string? value)
    {
        var prefix = optionName + "=";
        if (arg.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = null;
        return false;
    }

    private static bool LooksLikeOption(string arg)
    {
        return arg.Length > 0 && arg[0] == '-';
    }
}

internal enum NodeCliInspectMode
{
    None = 0,
    Inspect = 1,
    Break = 2
}

internal static class ReplStrictMode
{
    internal const int Auto = 0;
    internal const int Strict = 1;
    internal const int Sloppy = 2;
}
