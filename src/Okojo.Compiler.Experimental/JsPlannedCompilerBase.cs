using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected readonly BytecodeBuilder builder;
    protected readonly Stack<ActiveScope> activeScopes;
    private readonly Stack<ControlScope> controlScopes;
    private CompilerPlannedBinding[] plannedBindings = [];
    private int[] plannedBindingOffsets = [];
    private int[] plannedBindingCounts = [];
    private CompilerCollectedScope[] childScopes = [];
    private int[] childScopeOffsets = [];
    private int[] childScopeCounts = [];
    protected int rootContextSlotCount;
    protected bool emittingParameterInitializers;
    protected bool emittingInstanceFieldInitializer;
    protected bool strictDeclared;
    protected bool hasNewTarget;
    protected bool isGenerator;
    protected bool isAsync;
    protected int InstanceFieldClassIndex { get; set; } = -1;
    private BytecodeBuilder.Label optionalChainNullTarget;
    private int nextGeneratorSuspendId;
    private int generatorSwitchInstructionPc = -1;
    private int generatorResumeValueRegister = -1;
    private int generatorResumeModeRegister = -1;
    private readonly List<int> generatorResumeTargets = [];

    protected JsPlannedCompilerBase(JsRealm realm)
    {
        Vm = realm;
        builder = new(realm);
        activeScopes = [];
        controlScopes = [];
    }

    protected JsRealm Vm { get; }
    protected string CompilerName => GetType().Name;

    protected virtual IEnumerable<KeyValuePair<string, CapturedBindingAccess>> ExternalCaptures =>
        [];

    protected virtual int ExternalCaptureContextDepthOffset => 0;

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

    private enum AbruptCommand : byte
    {
        Break,
        Continue,
        Return,
    }

    private enum ControlScopeKind : byte
    {
        Iteration,
        ForOf,
        Label,
        Switch,
        Try,
        Finally,
    }

    private readonly record struct ControlScope(
        ControlScopeKind Kind,
        BytecodeBuilder.Label Break,
        BytecodeBuilder.Label Continue,
        BytecodeBuilder.Label Finally,
        int ContextDepth,
        int CompletionKindRegister = -1,
        int CompletionValueRegister = -1,
        int IteratorRegister = -1,
        bool IsAsyncIterator = false,
        string[]? Labels = null,
        List<FinallyAbruptRoute>? FinallyRoutes = null
    );

    private readonly record struct FinallyAbruptRoute(
        int CompletionKind,
        AbruptCommand Command,
        string? Label
    );

    private enum ExpressionResultMode : byte
    {
        Effect,
        Value,
        Test,
    }

    private readonly record struct ExpressionResult(
        ExpressionResultMode Mode,
        BytecodeBuilder.Label Target,
        bool JumpIfTrue
    )
    {
        public static ExpressionResult Effect => new(ExpressionResultMode.Effect, default, false);
        public static ExpressionResult Value => new(ExpressionResultMode.Value, default, false);

        public static ExpressionResult Test(BytecodeBuilder.Label target, bool jumpIfTrue) =>
            new(ExpressionResultMode.Test, target, jumpIfTrue);
    }
}
