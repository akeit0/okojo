namespace Okojo.Runtime;

public sealed class FileWorkerScriptSourceLoader : IWorkerScriptSourceLoader
{
    public string LoadScript(string path, string? referrer = null)
    {
        if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(referrer))
        {
            var baseDir = Path.GetDirectoryName(referrer);
            if (!string.IsNullOrEmpty(baseDir))
                path = Path.Combine(baseDir, path);
        }

        return File.ReadAllText(path);
    }
}
