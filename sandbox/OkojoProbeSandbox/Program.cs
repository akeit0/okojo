using Okojo.Hosting;
using Okojo.Node;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo;

namespace OkojoProbeSandbox;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = ProbeOptions.Parse(args);
            switch (options.Mode)
            {
                case ProbeMode.Script:
                    RunScript(options);
                    break;
                case ProbeMode.Module:
                    RunModule(options);
                    break;
                case ProbeMode.NodeMain:
                    RunNodeMain(options);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported mode: {options.Mode}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunScript(ProbeOptions options)
    {
        using var runtime = JsRuntime.Create();
        JsValue result = runtime.DefaultRealm.Eval(options.ReadSource());
        PrintValue(result, options.PrintDefault);
    }

    private static void RunModule(ProbeOptions options)
    {
        string source = options.ReadSource();
        string entry = PathUtil.NormalizePath(options.EntryPath ?? "/mods/main.js");
        var loader = new InMemoryModuleLoader(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [entry] = source
        });

        using var runtime = JsRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();

        JsValue moduleNamespace = runtime.MainRealm.Import(entry);
        PrintValue(moduleNamespace, options.PrintDefault);
    }

    private static void RunNodeMain(ProbeOptions options)
    {
        string source = options.ReadSource();
        string entry = PathUtil.NormalizePath(options.EntryPath ?? "/app/main.js");
        var loader = new InMemoryModuleLoader(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [entry] = source
        });

        using var runtime = OkojoNodeRuntime.CreateBuilder()
            .UseModuleSourceLoader(loader)
            .Build();

        JsValue result = runtime.RunMainModule(entry);
        PrintValue(result, options.PrintDefault);
    }

    private static void PrintValue(JsValue value, bool printDefault)
    {
        if (printDefault &&
            value.TryGetObject(out var obj) &&
            obj is JsObject jsObj &&
            jsObj.TryGetProperty("default", out var defaultValue))
        {
            Console.WriteLine(defaultValue);
            return;
        }

        Console.WriteLine(value);
    }
}

internal enum ProbeMode
{
    Script,
    Module,
    NodeMain
}

internal sealed class ProbeOptions
{
    public required ProbeMode Mode { get; init; }
    public required string Source { get; init; }
    public required bool SourceIsFile { get; init; }
    public string? EntryPath { get; init; }
    public bool PrintDefault { get; init; }

    public string ReadSource() => SourceIsFile ? File.ReadAllText(Source) : Source;

    public static ProbeOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException(GetUsage());

        ProbeMode? mode = null;
        string? source = null;
        bool sourceIsFile = false;
        string? entryPath = null;
        bool printDefault = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "script":
                    mode = ProbeMode.Script;
                    break;
                case "module":
                    mode = ProbeMode.Module;
                    break;
                case "node-main":
                    mode = ProbeMode.NodeMain;
                    break;
                case "--inline":
                    source = args[++i];
                    sourceIsFile = false;
                    break;
                case "--file":
                    source = args[++i];
                    sourceIsFile = true;
                    break;
                case "--entry":
                    entryPath = args[++i];
                    break;
                case "--print-raw":
                    printDefault = false;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(GetUsage());
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.{Environment.NewLine}{GetUsage()}");
            }
        }

        if (mode is null || string.IsNullOrWhiteSpace(source))
            throw new ArgumentException(GetUsage());

        return new ProbeOptions
        {
            Mode = mode.Value,
            Source = source,
            SourceIsFile = sourceIsFile,
            EntryPath = entryPath,
            PrintDefault = printDefault
        };
    }

    private static string GetUsage()
    {
        return
            """
            Usage:
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- script --inline "<js>"
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- script --file <path>
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- module --inline "<js>" [--entry /mods/main.js]
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- module --file <path> [--entry /mods/main.js]
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- node-main --inline "<js>" [--entry /app/main.js]
              dotnet run --project sandbox\OkojoProbeSandbox\OkojoProbeSandbox.csproj -- node-main --file <path> [--entry /app/main.js]

            By default, module and node-main print the default export if present.
            Use --print-raw to print the raw returned value instead.
            """;
    }
}

internal sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
{
    private readonly Dictionary<string, string> modules = modules;

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (specifier.StartsWith("./", StringComparison.Ordinal) ||
            specifier.StartsWith("../", StringComparison.Ordinal))
        {
            string basePath = referrer is null ? "/" : PathUtil.NormalizePath(referrer);
            int slash = basePath.LastIndexOf('/');
            string dir = slash >= 0 ? basePath[..(slash + 1)] : "/";
            return PathUtil.NormalizePath(dir + specifier);
        }

        return PathUtil.NormalizePath(specifier);
    }

    public string LoadSource(string resolvedId)
    {
        string normalized = PathUtil.NormalizePath(resolvedId);
        if (modules.TryGetValue(normalized, out var source))
            return source;

        throw new InvalidOperationException("Module not found: " + resolvedId);
    }
}

internal static class PathUtil
{
    public static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (parts.Count != 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return "/" + string.Join("/", parts);
    }
}
