using System.Runtime.CompilerServices;
using Okojo.DotNet.Modules;
using Okojo.Node;

var appRoot = Path.Combine(GetBaseDirectory(), "app");
var entryPath = Path.Combine(appRoot, "main.mjs");

Console.WriteLine("OkojoDotNetModulesSandbox");
// Console.WriteLine($"appRoot: {appRoot}");
// Console.WriteLine($"entry: {entryPath}");
Console.WriteLine();

using var runtime = NodeRuntime.CreateBuilder()
    .ConfigureRuntime(builder => builder.UseDotNetModuleImports())
    .Build();
runtime.RunMainModule(entryPath);

static string GetBaseDirectory([CallerFilePath] string callerFilePath = "")
{
    return Path.GetDirectoryName(callerFilePath) ?? "";
}
