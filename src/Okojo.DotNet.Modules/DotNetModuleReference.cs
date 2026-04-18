namespace Okojo.DotNet.Modules;

public readonly record struct DotNetModuleReference(
    DotNetModuleReferenceKind Kind,
    string Specifier,
    string? Version = null)
{
    public static DotNetModuleReference Package(string packageId, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return new(DotNetModuleReferenceKind.Package, packageId.Trim(), NormalizeVersion(version));
    }

    public static DotNetModuleReference Project(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        return new(DotNetModuleReferenceKind.Project, projectPath.Trim());
    }

    public static DotNetModuleReference AssemblyFile(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        return new(DotNetModuleReferenceKind.AssemblyFile, assemblyPath.Trim());
    }

    private static string? NormalizeVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }
}
