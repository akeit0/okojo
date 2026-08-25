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
    protected int derivedThisContextSlot = -1;
    internal const string DerivedThisBindingName = "\0derived-this";
    protected bool emittingParameterInitializers;
    protected bool emittingInstanceFieldInitializer;

    /// <summary>
    ///     Register receiving the script completion value while the script root
    ///     statement list is being emitted; -1 when completion is not observed
    ///     (function bodies compile through their own compiler instance).
    /// </summary>
    private int completionSinkRegister = -1;
    protected bool CompletionSinkActive => completionSinkRegister >= 0;

    private protected void SetCompletionSink(int register) => completionSinkRegister = register;

    private protected void ClearCompletionSink() => completionSinkRegister = -1;

    private protected int TakeCompletionSink()
    {
        var register = completionSinkRegister;
        completionSinkRegister = -1;
        return register;
    }

    private protected void RestoreCompletionSink(int register) => completionSinkRegister = register;

    protected void CaptureCompletionValue()
    {
        if (completionSinkRegister >= 0)
            EmitStar(completionSinkRegister);
    }

    protected bool strictDeclared;
    protected bool hasNewTarget;
    protected bool isGenerator;
    protected bool isAsync;
    protected int returnInferredNameStringIndex = -1;
    protected bool returnInferredNameFromFirstParameter;
    protected int InstanceFieldClassIndex { get; set; } = -1;
    private BytecodeBuilder.Label optionalChainNullTarget;
    private int nextGeneratorSuspendId;
    private int generatorSwitchInstructionPc = -1;
    private int generatorResumeValueRegister = -1;
    private int generatorResumeModeRegister = -1;
    private readonly List<int> generatorResumeTargets = [];
    private IReadOnlyDictionary<string, PlannedPrivateBinding> visiblePrivateBindings;
    private IReadOnlyList<PrivateBrandSource> activeExactPrivateBrandSources = [];

    protected JsPlannedCompilerBase(
        JsRealm realm,
        IReadOnlyDictionary<string, PlannedPrivateBinding>? privateBindings = null
    )
    {
        Vm = realm;
        builder = new(realm);
        activeScopes = [];
        controlScopes = [];
        visiblePrivateBindings =
            privateBindings
            ?? new Dictionary<string, PlannedPrivateBinding>(StringComparer.Ordinal);
        RegisterPrivateDebugNames(visiblePrivateBindings);
    }

    protected JsRealm Vm { get; }
    protected string CompilerName => GetType().Name;

    private void RegisterPrivateDebugNames(
        IReadOnlyDictionary<string, PlannedPrivateBinding> bindings
    )
    {
        foreach (var (name, binding) in bindings)
            builder.AddPrivateFieldDebugName(
                ((long)binding.BrandId << 32) | (uint)binding.SlotIndex,
                name
            );
    }

    internal readonly record struct PlannedPrivateBinding(
        int BrandId,
        int SlotIndex,
        PlannedPrivateMemberKind Kind = PlannedPrivateMemberKind.Field,
        bool IsStatic = false
    );

    internal enum PlannedPrivateMemberKind : byte
    {
        Field,
        Method,
        Accessor,
    }

    private readonly record struct PrivateBrandSource(int BrandId, int Register);

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
        Destructuring,
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
