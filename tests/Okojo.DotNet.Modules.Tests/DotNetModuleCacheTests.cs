using Okojo.DotNet.Modules;

namespace Okojo.DotNet.Modules.Tests;

public class DotNetModuleCacheTests
{
    [Test]
    public void CacheLayout_Uses_NuGetOverride_And_RunfileRoot()
    {
        var layout = DotNetFileBasedAppCacheLayout.Create(
            @"C:\Users\akito",
            @"C:\temp",
            @"D:\nuget-cache");

        Assert.That(layout.GlobalPackagesRoot, Is.EqualTo(Path.GetFullPath(@"D:\nuget-cache")));
        Assert.That(layout.RunFileCacheRoot, Is.EqualTo(Path.GetFullPath(@"C:\temp\dotnet\runfile")));
    }

    [Test]
    public void CacheKey_Is_Stable_For_Same_Reference_Set()
    {
        var references = new[]
        {
            DotNetModuleReference.Project("../Shared/Shared.csproj"),
            DotNetModuleReference.Package("Newtonsoft.Json", "13.0.3"),
            DotNetModuleReference.AssemblyFile(@".\packages\Interop.dll")
        };

        var left = DotNetFileBasedAppCacheKey.Create(
            @"C:\apps\hello.cs",
            references,
            "10.0.100");
        var right = DotNetFileBasedAppCacheKey.Create(
            @"C:\elsewhere\hello.cs",
            references.Reverse(),
            "10.0.100");

        Assert.That(left.ApplicationName, Is.EqualTo("hello"));
        Assert.That(right.ApplicationName, Is.EqualTo("hello"));
        Assert.That(left.Fingerprint, Is.EqualTo(right.Fingerprint));
        Assert.That(left.RunFileDirectoryName, Is.EqualTo(right.RunFileDirectoryName));
    }

    [Test]
    public void ModuleReference_AssemblyFile_Uses_AssemblyKind()
    {
        var reference = DotNetModuleReference.AssemblyFile(@".\packages\Interop.dll");

        Assert.That(reference.Kind, Is.EqualTo(DotNetModuleReferenceKind.AssemblyFile));
        Assert.That(reference.Specifier, Is.EqualTo(@".\packages\Interop.dll"));
        Assert.That(reference.Version, Is.Null);
    }
}
