namespace Okojo.Benchmarks;

internal static class ScriptSourceLoader
{
    public static string LoadScenario(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
            throw new ArgumentException("Scenario must be non-empty.", nameof(scenario));

        var fileName = scenario + ".js";
        var baseDir = AppContext.BaseDirectory;

        var directPath = Path.Combine(baseDir, "scripts", fileName);
        if (File.Exists(directPath)) return File.ReadAllText(directPath);

        var repoPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "benchmarks", "Okojo.Benchmarks",
            "scripts", fileName));
        if (File.Exists(repoPath)) return File.ReadAllText(repoPath);

        throw new FileNotFoundException($"Benchmark script not found for scenario '{scenario}'.", directPath);
    }
}
