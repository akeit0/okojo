namespace Okojo.Runtime;

public interface IWorkerScriptSourceLoader
{
    string LoadScript(string path, string? referrer = null);
}
