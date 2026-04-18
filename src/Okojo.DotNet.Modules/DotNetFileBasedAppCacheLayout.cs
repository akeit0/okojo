namespace Okojo.DotNet.Modules;

public readonly record struct DotNetFileBasedAppCacheLayout(string GlobalPackagesRoot, string RunFileCacheRoot)
{
    public static DotNetFileBasedAppCacheLayout Detect()
    {
        return Create(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(),
            Environment.GetEnvironmentVariable("NUGET_PACKAGES"));
    }

    public static DotNetFileBasedAppCacheLayout Create(
        string userProfilePath,
        string tempPath,
        string? nugetPackagesPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);

        var globalPackages = string.IsNullOrWhiteSpace(nugetPackagesPath)
            ? Path.Combine(userProfilePath, ".nuget", "packages")
            : nugetPackagesPath.Trim();
        var runFileRoot = Path.Combine(tempPath.Trim(), "dotnet", "runfile");
        return new(Path.GetFullPath(globalPackages), Path.GetFullPath(runFileRoot));
    }
}
