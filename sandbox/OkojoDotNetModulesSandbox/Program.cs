using System.Runtime.CompilerServices;
using Okojo.DotNet.Modules;
using Okojo.Node;
using Okojo.Objects;

var appRoot = Path.Combine(GetBaseDirectory(), "app");
var entryPath = Path.Combine(appRoot, "main.mjs");
var support = new DotNetModuleImportSupport();

Console.WriteLine("OkojoDotNetModulesSandbox");
// Console.WriteLine($"appRoot: {appRoot}");
// Console.WriteLine($"entry: {entryPath}");
Console.WriteLine();

using var runtime = NodeRuntime.CreateBuilder()
    .ConfigureRuntime(support.ConfigureRuntime)
    .WrapModuleSourceLoader(support.WrapModuleSourceLoader)
    .Build();
runtime.RunMainModule(entryPath);

static string GetBaseDirectory([CallerFilePath] string callerFilePath = "")
{
    return Path.GetDirectoryName(callerFilePath) ?? "";
}
