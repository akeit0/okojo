namespace OkojoHostEventLoopSandbox;

internal sealed class SandboxAssets
{
    public required string ScriptRoot { get; init; }
    public required IReadOnlyDictionary<string, string> FetchPayloads { get; init; }

    public static SandboxAssets CreateDefault()
    {
        return new()
        {
            ScriptRoot = Path.Combine(AppContext.BaseDirectory, "scripts"),
            FetchPayloads = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["https://demo.local/api/data"] = """{"kind":"demo"}""",
                ["https://services.local/users/42"] = """{"id":42,"name":"Ada"}""",
                ["https://services.local/audit/42"] = """{"userId":42,"actions":["login","deploy"]}"""
            }
        };
    }

    public string ReadScript(string scriptPath)
    {
        var relativePath = scriptPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(ScriptRoot, relativePath));
    }
}
