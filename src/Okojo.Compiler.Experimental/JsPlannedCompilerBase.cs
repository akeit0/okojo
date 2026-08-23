using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected readonly BytecodeBuilder builder;
    protected readonly Stack<ActiveScope> activeScopes;
    private readonly Stack<LoopTargets> loopTargets;
    private CompilerPlannedBinding[] plannedBindings = [];
    private int[] plannedBindingOffsets = [];
    private int[] plannedBindingCounts = [];
    private CompilerCollectedScope[] childScopes = [];
    private int[] childScopeOffsets = [];
    private int[] childScopeCounts = [];
    protected int rootContextSlotCount;
    protected bool emittingParameterInitializers;

    protected JsPlannedCompilerBase(JsRealm realm)
    {
        Vm = realm;
        builder = new(realm);
        activeScopes = [];
        loopTargets = [];
    }

    protected JsRealm Vm { get; }
    protected string CompilerName => GetType().Name;

    protected virtual IEnumerable<KeyValuePair<string, CapturedBindingAccess>> ExternalCaptures =>
        [];

    protected virtual bool TryResolveExternalBinding(
        string name,
        out CapturedBindingAccess access,
        out int contextDepth
    )
    {
        access = default;
        contextDepth = 0;
        return false;
    }

    protected virtual void EmitRootContextBindings() { }

    protected readonly record struct BindingStorage(CompilerPlannedBinding Planned, int Register);

    protected readonly record struct ActiveScope(
        int ScopeId,
        IReadOnlyList<BindingStorage> Bindings,
        int ContextSlotCount
    )
    {
        public bool HasContext => ContextSlotCount != 0;
    }

    private readonly record struct LoopTargets(
        BytecodeBuilder.Label Break,
        BytecodeBuilder.Label Continue,
        int ContextDepth
    );
}
