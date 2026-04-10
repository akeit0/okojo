using Okojo.Bytecode;
using Okojo.Runtime;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    private readonly BytecodeBuilder builder;
    private readonly Dictionary<int, List<CompilerPlannedBinding>> plannedBindingsByScopeId;
    private readonly Dictionary<int, List<CompilerCollectedScope>> scopesByParentScopeId;
    private readonly Dictionary<string, RootBindingStorage> rootBindingsByName;
    private readonly Stack<ActiveScope> activeScopes;
    private int rootContextSlotCount;

    public JsPlannedScriptCompiler(JsRealm realm)
    {
        Vm = realm;
        builder = new BytecodeBuilder(realm);
        plannedBindingsByScopeId = new();
        scopesByParentScopeId = new();
        rootBindingsByName = new(StringComparer.Ordinal);
        activeScopes = new();
    }

    public JsRealm Vm { get; }

    private bool HasCurrentContext => rootContextSlotCount != 0;
}
