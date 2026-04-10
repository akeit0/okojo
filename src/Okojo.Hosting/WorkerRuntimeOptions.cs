namespace Okojo.Hosting;

public sealed class WorkerRuntimeOptions
{
    public string? ModuleEntry { get; set; }
    public string? ModuleReferrer { get; set; }
    public bool StartBackgroundHost { get; set; }
}
