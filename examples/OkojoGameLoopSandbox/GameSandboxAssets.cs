namespace OkojoGameLoopSandbox;

internal sealed class GameSandboxAssets
{
    public required string ScriptRoot { get; init; }

    public static GameSandboxAssets CreateDefault()
    {
        return new()
        {
            ScriptRoot = Path.Combine(AppContext.BaseDirectory, "scripts")
        };
    }

    public string ReadScript(string scriptPath)
    {
        var relativePath = scriptPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllText(Path.Combine(ScriptRoot, relativePath));
    }
}
