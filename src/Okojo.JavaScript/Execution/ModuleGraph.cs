using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Execution;

internal sealed class ModuleGraph(JsAgent agent)
{
    private readonly Dictionary<string, ModuleRecordNode> nodes = new(StringComparer.Ordinal);

    public int Count => nodes.Count;

    public ModuleRecordNode GetOrCreate(
        string resolvedId,
        string source,
        JsModuleNamespaceObject exportsObject
    )
    {
        if (nodes.TryGetValue(resolvedId, out var existing))
            return existing;

        var node = new ModuleRecordNode(
            resolvedId,
            JavaScriptParser.ParseModule(source, resolvedId),
            exportsObject
        );
        nodes.Add(resolvedId, node);
        return node;
    }

    public bool TryGet(string resolvedId, out ModuleRecordNode node)
    {
        return nodes.TryGetValue(resolvedId, out node!);
    }

    public void Clear()
    {
        foreach (var node in nodes.Values)
            node.Dispose();
        nodes.Clear();
    }

    public bool Remove(string resolvedId)
    {
        if (!nodes.Remove(resolvedId, out var node))
            return false;
        node.Dispose();
        return true;
    }

    public IReadOnlyList<ModuleRecordNode> GetDependencies(ModuleRecordNode node)
    {
        var deps = new List<ModuleRecordNode>();
        if (node.LinkPlan is { } plan)
        {
            for (var i = 0; i < plan.RequestedDependencies.Count; i++)
            {
                var dependency = plan.RequestedDependencies[i];
                if (string.Equals(dependency.ImportType, "text", StringComparison.Ordinal))
                    continue;
                if (nodes.TryGetValue(dependency.ResolvedId, out var dependencyNode))
                    deps.Add(dependencyNode);
            }
            return deps;
        }

        foreach (ref readonly var request in node.Program!.ModuleRequests)
        {
            var attributes = node.Program.GetImportAttributes(request);
            var isText = false;
            for (var i = 0; i < attributes.Length; i++)
                if (
                    string.Equals(
                        node.Program.GetString(attributes[i].KeyStringIndex),
                        "type",
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        node.Program.GetString(attributes[i].ValueStringIndex),
                        "text",
                        StringComparison.Ordinal
                    )
                )
                {
                    isText = true;
                    break;
                }

            if (isText)
                continue;
            var depResolved = agent.ModuleSourceLoader.ResolveSpecifier(
                node.Program.GetString(request.SpecifierStringIndex),
                node.ResolvedId
            );
            if (nodes.TryGetValue(depResolved, out var depNode) && !deps.Contains(depNode))
                deps.Add(depNode);
        }

        return deps;
    }

    public void CollectDependencyClosure(string resolvedId, ISet<string> targets)
    {
        if (!nodes.TryGetValue(resolvedId, out var root))
            return;

        var stack = new Stack<ModuleRecordNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var dependencies = GetDependencies(current);
            for (var index = 0; index < dependencies.Count; index++)
            {
                var dependency = dependencies[index];
                if (!targets.Add(dependency.ResolvedId))
                    continue;

                stack.Push(dependency);
            }
        }
    }

    public void CollectImporterClosure(string resolvedId, ISet<string> targets)
    {
        if (!nodes.TryGetValue(resolvedId, out var root))
            return;

        var stack = new Stack<ModuleRecordNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var importer in EnumerateImporters(current.ResolvedId))
            {
                if (!targets.Add(importer.ResolvedId))
                    continue;

                stack.Push(importer);
            }
        }
    }

    private IEnumerable<ModuleRecordNode> EnumerateImporters(string resolvedId)
    {
        foreach (var node in nodes.Values)
        {
            if (node.ResolvedId == resolvedId)
                continue;

            var dependencies = GetDependencies(node);
            for (var index = 0; index < dependencies.Count; index++)
                if (dependencies[index].ResolvedId == resolvedId)
                {
                    yield return node;
                    break;
                }
        }
    }
}

internal enum ModuleEvalState
{
    Uninitialized = 0,
    Instantiating = 1,
    Evaluating = 2,
    Evaluated = 3,
    Failed = 4,
}

internal sealed class ModuleRecordNode(
    string resolvedId,
    JsAst program,
    JsModuleNamespaceObject exportsObject
) : IDisposable
{
    public string ResolvedId { get; } = resolvedId;
    public JsAst? Program { get; private set; } = program;
    public string SourceText => Program?.SourceText ?? string.Empty;
    public JsModuleNamespaceObject ExportsObject { get; } = exportsObject;
    public ModuleLinkPlan? LinkPlan { get; set; }
    public ModuleExecutionCompilation? Compilation { get; set; }
    public ModuleEvalState State { get; set; }
    public JsPromiseObject? PendingTopLevelAwaitPromise { get; set; }
    public ModuleExecutionBindings? ExecutionBindings { get; set; }
    public IReadOnlyDictionary<string, ModuleVariableBinding>? CompileModuleBindings { get; set; }
    public int PendingAsyncDependencies { get; set; }
    public List<ModuleRecordNode> AsyncParentModules { get; } = [];
    public int AsyncEvaluationOrder { get; set; }
    public bool EvaluationStarted { get; set; }
    public bool RequiresTopLevelAwait { get; set; }
    public ModuleRecordNode? AsyncCycleRoot { get; set; }
    public Exception? LastError { get; set; }

    public void Dispose()
    {
        ReleaseProgram();
    }

    public void ReleaseProgram()
    {
        Program?.Dispose();
        Program = null;
    }
}
