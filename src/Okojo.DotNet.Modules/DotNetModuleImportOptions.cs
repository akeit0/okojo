namespace Okojo.DotNet.Modules;

public sealed class DotNetModuleImportOptions
{
    public string? GlobalPackagesRoot { get; set; }

    public IReadOnlyList<string> PreferredTargetFrameworks { get; set; } =
    [
        "net10.0",
        "net9.0",
        "net8.0",
        "net7.0",
        "net6.0",
        "net5.0",
        "netcoreapp3.1",
        "netstandard2.1",
        "netstandard2.0"
    ];
}
