namespace Okojo.Runtime;

public interface IWorkerScriptSourceLoader
{
    string ResolveScript(string path, string? referrer = null)
    {
        return path;
    }

    string LoadScript(string path, string? referrer = null);
}
