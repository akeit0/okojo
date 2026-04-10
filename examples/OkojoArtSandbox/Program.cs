using System.Runtime.CompilerServices;

namespace OkojoArtSandbox;

internal static class Program
{
    private static void Main(string[] args)
    {
        var scriptPath = ResolveScriptPath(args);
        using var app = new SilkSketchApp(scriptPath);
        app.Run();
    }

    private static string ResolveScriptPath(string[] args)
    {
        if (args.Length != 0)
            return Path.GetFullPath(args[0]);


        return Path.Combine(GetBaseDirectory(), "scripts", "main.js");
    }

#if DEV
    private static string GetBaseDirectory([CallerFilePath] string callerFilePath = "")
    {
        return Path.GetDirectoryName(callerFilePath) ?? "";
    }
#else
    static string GetBaseDirectory() => AppContext.BaseDirectory;
#endif
}
