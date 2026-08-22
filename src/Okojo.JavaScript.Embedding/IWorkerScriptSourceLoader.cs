namespace Okojo.JavaScript.Embedding;

public interface IWorkerScriptSourceLoader
{
    string LoadScript(string path, string? referrer = null);
}
