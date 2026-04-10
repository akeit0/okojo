using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Runtime;

internal sealed class ModuleGraph(JsAgent agent)
{
    private readonly Dictionary<string, ModuleRecordNode> nodes = new(StringComparer.Ordinal);

    public int Count => nodes.Count;

    public ModuleRecordNode GetOrCreate(string resolvedId, string source, JsModuleNamespaceObject exportsObject)
    {
        if (nodes.TryGetValue(resolvedId, out var existing))
            return existing;

        var parsed = JavaScriptParser.ParseModule(source, resolvedId);
        var node = new ModuleRecordNode(resolvedId, parsed, exportsObject);
        nodes.Add(resolvedId, node);
        return node;
    }

    public bool TryGet(string resolvedId, out ModuleRecordNode node)
    {
        return nodes.TryGetValue(resolvedId, out node!);
    }

    public void Clear()
    {
        nodes.Clear();
    }

    public bool Remove(string resolvedId)
    {
        return nodes.Remove(resolvedId);
    }

    public IReadOnlyList<ModuleRecordNode> GetDependencies(ModuleRecordNode node)
    {
        var deps = new List<ModuleRecordNode>();
        for (var i = 0; i < node.Program.Statements.Count; i++)
            if (node.Program.Statements[i] is JsImportDeclaration importDecl)
            {
                var depResolved = agent.Engine.ModuleSourceLoader.ResolveSpecifier(importDecl.Source, node.ResolvedId);
                if (nodes.TryGetValue(depResolved, out var depNode))
                    deps.Add(depNode);
            }
            else if (node.Program.Statements[i] is JsExportNamedDeclaration named &&
                     !string.IsNullOrEmpty(named.Source))
            {
                var depResolved = agent.Engine.ModuleSourceLoader.ResolveSpecifier(named.Source!, node.ResolvedId);
                if (nodes.TryGetValue(depResolved, out var depNode))
                    deps.Add(depNode);
            }
            else if (node.Program.Statements[i] is JsExportAllDeclaration star)
            {
                var depResolved = agent.Engine.ModuleSourceLoader.ResolveSpecifier(star.Source, node.ResolvedId);
                if (nodes.TryGetValue(depResolved, out var depNode))
                    deps.Add(depNode);
            }

        return deps;
    }
}

internal enum ModuleEvalState
{
    Uninitialized = 0,
    Instantiating = 1,
    Evaluating = 2,
    Evaluated = 3,
    Failed = 4
}

internal sealed class ModuleRecordNode(string resolvedId, JsProgram program, JsModuleNamespaceObject exportsObject)
{
    public string ResolvedId { get; } = resolvedId;
    public JsProgram Program { get; } = program;
    public JsModuleNamespaceObject ExportsObject { get; } = exportsObject;
    public ModuleLinkPlan? LinkPlan { get; set; }
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
}
