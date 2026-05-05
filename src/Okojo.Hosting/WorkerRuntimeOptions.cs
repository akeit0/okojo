using Okojo.Runtime;

namespace Okojo.Hosting;

public sealed class WorkerRuntimeOptions
{
    public string? ModuleEntry
    {
        get => ScriptEntry;
        set
        {
            ScriptEntry = value;
            ScriptType = WorkerScriptType.Module;
        }
    }

    public string? ModuleReferrer
    {
        get => ScriptReferrer;
        set => ScriptReferrer = value;
    }

    public string? ScriptEntry { get; set; }
    public string? ScriptReferrer { get; set; }
    public WorkerScriptType ScriptType { get; set; } = WorkerScriptType.Module;
    public bool StartBackgroundHost { get; set; }
}
