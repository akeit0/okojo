using Okojo.Runtime;

namespace Okojo.Tests;

public class FileModuleSourceLoaderTests
{
    [Test]
    public void ResolveSpecifier_Uses_Implicit_Module_Extensions_And_Index_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "okojo-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var appDir = Path.Combine(root, "app");
            var libDir = Path.Combine(appDir, "lib");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(libDir);

            var entry = Path.Combine(appDir, "entry.mjs");
            var util = Path.Combine(appDir, "util.mjs");
            var index = Path.Combine(libDir, "index.js");
            File.WriteAllText(entry, "export {};");
            File.WriteAllText(util, "export const value = 1;");
            File.WriteAllText(index, "export const value = 2;");

            var loader = new FileModuleSourceLoader();

            Assert.That(loader.ResolveSpecifier("./util", entry), Is.EqualTo(Path.GetFullPath(util)));
            Assert.That(loader.ResolveSpecifier("./lib", entry), Is.EqualTo(Path.GetFullPath(index)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
