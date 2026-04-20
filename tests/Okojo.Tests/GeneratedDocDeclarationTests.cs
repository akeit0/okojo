using System.Diagnostics;
using System.Reflection;

namespace Okojo.Tests;

public class GeneratedDocDeclarationTests
{
    //[Test]
    [Explicit(
        "This test runs the doc generator and verifies the output. It is marked explicit because it is relatively slow and relies on the doc generator working correctly, so it should only be run when making changes to the doc generator or its input.")]
    public async Task DocGenerator_Emits_Rest_Params_For_ReadOnlySpan()
    {
        var repoRoot = FindRepoRoot();
        var outputRoot = Path.Combine(Path.GetTempPath(), "Okojo.DocGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);

        try
        {
            var cliProject = Path.Combine(repoRoot, "src", "Okojo.DocGenerator.Cli", "Okojo.DocGenerator.Cli.csproj");
            var testsProject = Path.Combine(repoRoot, "tests", "Okojo.Tests", "Okojo.Tests.csproj");

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(cliProject);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(testsProject);
            startInfo.ArgumentList.Add("--out");
            startInfo.ArgumentList.Add(outputRoot);
            startInfo.ArgumentList.Add("--per-type");

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Failed to start doc generator.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.That(process.ExitCode, Is.EqualTo(0),
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");

            var globalsPath = Path.Combine(outputRoot, "globals.d.ts");
            var objectPath = Path.Combine(outputRoot, "Foo", "Bar.d.ts");
            var globalsText = await File.ReadAllTextAsync(globalsPath);
            var objectText = await File.ReadAllTextAsync(objectPath);

            Assert.That(globalsText, Does.Contain("declare function sumNumbers(...values: number[]): number;"));
            Assert.That(globalsText, Does.Contain("declare function describeAny(...values: any[]): string;"));
            Assert.That(globalsText, Does.Contain("declare function background(color: string): void;"));
            Assert.That(globalsText, Does.Contain("declare function background(gray: number): void;"));
            Assert.That(globalsText,
                Does.Contain("declare function background(r: number, g: number, b: number): void;"));
            Assert.That(globalsText,
                Does.Contain("declare function background(r: number, g: number, b: number, a: number): void;"));
            Assert.That(globalsText, Does.Contain("declare function pick(value: string): string;"));
            Assert.That(globalsText, Does.Contain("declare function pick(value: number): string;"));
            Assert.That(objectText, Does.Contain("x: number;"));
            Assert.That(objectText, Does.Contain("static sin(a: number): number;"));
            Assert.That(objectText, Does.Contain("static sumNumbers(...values: number[]): number;"));
            Assert.That(objectText, Does.Contain("static describeJsValues(...values: any[]): string;"));
            Assert.That(objectText, Does.Contain("static describeAny(...values: any[]): string;"));
            Assert.That(objectText, Does.Contain("static pick(value: string): string;"));
            Assert.That(objectText, Does.Contain("static pick(value: number): string;"));
            Assert.That(objectText, Does.Contain("static pick(value: any): string;"));
            Assert.That(objectText, Does.Not.Contain("echo(value: string): string;"));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, true);
        }
    }

    private static string FindRepoRoot()
    {
        var current = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                      ?? throw new InvalidOperationException("Unable to resolve test assembly path.");
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Okojo.slnx")))
                return current;

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
