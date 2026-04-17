namespace Okojo.Runtime;

public enum ModuleLoadKind : byte
{
    SourceText = 0,
    HostModule = 1
}

public readonly record struct ModuleLoadResult(ModuleLoadKind Kind, string Value)
{
    public static ModuleLoadResult SourceText(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return new(ModuleLoadKind.SourceText, sourceText);
    }

    public static ModuleLoadResult HostModule(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        return new(ModuleLoadKind.HostModule, moduleId);
    }

    public string GetRequiredSourceText()
    {
        return Kind == ModuleLoadKind.SourceText
            ? Value
            : throw new NotSupportedException(
                $"Module load kind '{Kind}' is not yet executable through the source-only module path.");
    }
}
