using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Okojo.SourceGenerator.GlobalGeneration;
using Okojo.SourceGenerator.ObjectGeneration;

namespace Okojo.DocGenerator.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "Usage: Okojo.DocGenerator.Cli --project <path.csproj> [--type <Full.Type.Name>] --out <path-or-directory> [--per-type]");
            return 1;
        }

        string? projectPath = null;
        string? outputPath = null;
        string? typeName = null;
        var perType = false;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--project":
                    projectPath = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--out":
                    outputPath = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--type":
                    typeName = i + 1 < args.Length ? args[++i] : null;
                    break;
                case "--per-type":
                    perType = true;
                    break;
            }

        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Both --project and --out are required.");
            return 2;
        }

        projectPath = Path.GetFullPath(projectPath);
        outputPath = Path.GetFullPath(outputPath);

        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();

        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(projectPath);
        var compilation = await project.GetCompilationAsync();
        if (compilation is null)
        {
            Console.Error.WriteLine($"Failed to compile project '{projectPath}'.");
            return 3;
        }

        var globalModels = new List<GlobalTypeModel>();
        var objectModels = new List<JsObjectTypeModel>();
        CollectTypes(compilation.Assembly.GlobalNamespace, globalModels, objectModels, typeName);

        if (globalModels.Count == 0 && objectModels.Count == 0)
        {
            Console.Error.WriteLine(typeName is null
                ? "No [GenerateJsGlobals] or [GenerateJsObject] types were found."
                : $"Type '{typeName}' with [GenerateJsGlobals] or [GenerateJsObject] was not found.");
            return 4;
        }

        if (perType)
        {
            Directory.CreateDirectory(outputPath);
            foreach (var group in CreateOutputGroups(globalModels, objectModels))
            {
                var filePath = Path.Combine(outputPath, group.RelativeFilePath);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(filePath,
                    TypeScriptDeclarationEmitter.Emit(group.GlobalModels, group.ObjectModels));
                Console.WriteLine($"Wrote {filePath}");
            }

            return 0;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        await File.WriteAllTextAsync(outputPath, TypeScriptDeclarationEmitter.Emit(globalModels, objectModels));
        Console.WriteLine($"Wrote {outputPath}");
        return 0;
    }

    private static IReadOnlyList<DeclarationOutputGroup> CreateOutputGroups(
        IReadOnlyList<GlobalTypeModel> globalModels,
        IReadOnlyList<JsObjectTypeModel> objectModels)
    {
        var groups = new Dictionary<string, DeclarationOutputGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in globalModels)
        {
            var fileName = DeclarationFileNameHelper.GetFileName(model.Symbol,
                DocAttributeReader.ReadDeclarationInfo(model.Symbol).FileName);
            if (!groups.TryGetValue(fileName, out var group))
            {
                group = new() { RelativeFilePath = fileName };
                groups.Add(fileName, group);
            }

            group.GlobalModels.Add(model);
        }

        foreach (var model in objectModels)
        {
            var fileName = DeclarationFileNameHelper.GetFileName(model.Symbol,
                DocAttributeReader.ReadDeclarationInfo(model.Symbol).FileName);
            if (!groups.TryGetValue(fileName, out var group))
            {
                group = new() { RelativeFilePath = fileName };
                groups.Add(fileName, group);
            }

            group.ObjectModels.Add(model);
        }

        return groups.Values.OrderBy(static x => x.RelativeFilePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void CollectTypes(
        INamespaceSymbol ns,
        List<GlobalTypeModel> globalResults,
        List<JsObjectTypeModel> objectResults,
        string? typeName)
    {
        foreach (var memberNamespace in ns.GetNamespaceMembers())
            CollectTypes(memberNamespace, globalResults, objectResults, typeName);

        foreach (var type in ns.GetTypeMembers())
            CollectType(type, globalResults, objectResults, typeName);
    }

    private static void CollectType(
        INamedTypeSymbol type,
        List<GlobalTypeModel> globalResults,
        List<JsObjectTypeModel> objectResults,
        string? typeName)
    {
        if (typeName is null || string.Equals(type.ToDisplayString(), typeName, StringComparison.Ordinal))
        {
            var globalModel = DocAttributeReader.Filter(GlobalExportCollector.Collect(type));
            if (globalModel is not null)
                globalResults.Add(globalModel);

            var objectModel = DocAttributeReader.Filter(JsObjectExportCollector.Collect(type));
            if (objectModel is not null)
                objectResults.Add(objectModel);
        }

        foreach (var nested in type.GetTypeMembers())
            CollectType(nested, globalResults, objectResults, typeName);
    }
}
