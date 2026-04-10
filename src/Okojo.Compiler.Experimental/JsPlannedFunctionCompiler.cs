using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
{
    private readonly BytecodeBuilder builder;
    private readonly Dictionary<int, List<CompilerPlannedBinding>> plannedBindingsByScopeId;
    private readonly Dictionary<int, List<CompilerCollectedScope>> scopesByParentScopeId;
    private readonly Stack<ActiveScope> activeScopes;
    private readonly IReadOnlyDictionary<string, CapturedBindingAccess> inheritedCaptures;
    private readonly Dictionary<string, int> parameterRegisterByName;
    private int rootContextSlotCount;

    public JsPlannedFunctionCompiler(JsRealm realm, IReadOnlyDictionary<string, CapturedBindingAccess>? inheritedCaptures = null)
    {
        Vm = realm;
        builder = new BytecodeBuilder(realm);
        plannedBindingsByScopeId = new();
        scopesByParentScopeId = new();
        activeScopes = new();
        this.inheritedCaptures = inheritedCaptures ?? new Dictionary<string, CapturedBindingAccess>(StringComparer.Ordinal);
        parameterRegisterByName = new(StringComparer.Ordinal);
    }

    public JsRealm Vm { get; }
    private bool HasCurrentContext => rootContextSlotCount != 0;

    private readonly record struct BindingStorage(CompilerPlannedBinding Planned, int Register);
}
