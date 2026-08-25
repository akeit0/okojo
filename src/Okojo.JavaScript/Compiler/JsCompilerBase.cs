using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
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

    /// <summary>
    ///     Depth of enclosing breakable constructs (iterations, switch, labeled
    ///     breakable blocks). A finally block inside one can be exited abruptly
    ///     (break/continue) skipping its tail, so its expressions participate in
    ///     completion tracking there (V8 rewriter save/restore is positional).
    /// </summary>
    private int breakableContextDepth;
    protected bool InBreakableContext => breakableContextDepth > 0;

    private protected void EnterBreakableContext() => breakableContextDepth++;

    private protected void ExitBreakableContext() => breakableContextDepth--;

    private protected void SetCompletionSink(int register) => completionSinkRegister = register;

    private protected void ClearCompletionSink() => completionSinkRegister = -1;

    private protected int TakeCompletionSink()
    {
        var register = completionSinkRegister;
        completionSinkRegister = -1;
        return register;
    }

    /// <summary>
    ///     Whether a statement needs the AssignUndefinedBefore reset prefix
    ///     (V8 rewriter.cc): iterations, try, switch always; an if only when its
    ///     arms disagree on guaranteeing a value. Everything else carries.
    /// </summary>
    protected static bool StatementNeedsCompletionReset(FlatAst ast, int nodeIndex)
    {
        var node = ast[nodeIndex];
        while (node.Kind == AstKind.LabeledStatement)
            node = ast[node.Arg1];

        return node.Kind switch
        {
            AstKind.WhileStatement
            or AstKind.DoWhileStatement
            or AstKind.ForStatement
            or AstKind.ForInOfStatement
            or AstKind.TryStatement
            or AstKind.SwitchStatement => true,
            AstKind.IfStatement => node.Arg2 < 0
                || !StatementGuaranteesCompletionValue(ast, node.Arg1)
                || !StatementGuaranteesCompletionValue(ast, node.Arg2),
            _ => false,
        };
    }

    /// <summary>
    ///     Whether a construct ALWAYS leaves a value in the sink when it finishes.
    ///     Used by the if-analysis: an if whose arms disagree needs an undefined
    ///     prefix so the untaken path cannot leak a stale carried value.
    /// </summary>
    protected static bool StatementGuaranteesCompletionValue(FlatAst ast, int nodeIndex)
    {
        var node = ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.ExpressionStatement:
                return true;
            case AstKind.LabeledStatement:
                return StatementGuaranteesCompletionValue(ast, node.Arg1);
            case AstKind.BlockStatement:
            {
                var children = ast.ChildRange(node.Arg0, node.Arg1);
                return children.Length != 0
                    && StatementGuaranteesCompletionValue(ast, children[^1]);
            }
            case AstKind.IfStatement:
                return node.Arg2 >= 0
                    && StatementGuaranteesCompletionValue(ast, node.Arg1)
                    && StatementGuaranteesCompletionValue(ast, node.Arg2);
            default:
                // Break/continue jump away, declarations produce nothing, and
                // iterations/try/switch have their own empty-completion rules.
                return false;
        }
    }

    protected static bool BodyEndsAbruptly(FlatAst ast, int offset, int count)
    {
        var statements = ast.ChildRange(offset, count);
        return statements.Length != 0
            && ast[statements[^1]].Kind is AstKind.ReturnStatement or AstKind.ThrowStatement;
    }

    private protected void RestoreCompletionSink(int register) => completionSinkRegister = register;

    protected void CaptureCompletionValue()
    {
        if (completionSinkRegister >= 0)
            EmitStar(completionSinkRegister);
    }

    protected void EmitRootLocalDebugInfos()
    {
        var endPc = builder.CodeLength;
        if (endPc == 0)
            return;

        EmitLocalDebugInfos(activeScopes.Peek().Bindings, 0, endPc);
    }

    protected void EmitLocalDebugInfos(
        IReadOnlyList<BindingStorage> bindings,
        int startPc,
        int endPc
    )
    {
        if (endPc <= startPc)
            return;

        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var name = binding.Planned.Name;
            if (
                name.Length == 0
                || name.StartsWith("$", StringComparison.Ordinal)
                || name.IndexOf('#') >= 0
            )
                continue;

            JsLocalDebugStorageKind storageKind;
            int storageIndex;
            if (binding.Planned.StorageKind == CompilerPlannedStorageKind.ContextSlot)
            {
                storageKind = JsLocalDebugStorageKind.ContextSlot;
                storageIndex = binding.Planned.StorageIndex;
            }
            else if (
                binding.Planned.StorageKind
                    is CompilerPlannedStorageKind.LocalRegister
                        or CompilerPlannedStorageKind.LexicalRegister
                && binding.Register >= 0
            )
            {
                storageKind = JsLocalDebugStorageKind.Register;
                storageIndex = binding.Register;
            }
            else
                continue;

            var flags = binding.Planned.Kind switch
            {
                CompilerCollectedBindingKind.Parameter => JsLocalDebugFlags.Parameter,
                CompilerCollectedBindingKind.Var
                or CompilerCollectedBindingKind.FunctionDeclaration => JsLocalDebugFlags.Var,
                CompilerCollectedBindingKind.Lexical
                or CompilerCollectedBindingKind.ClassDeclaration
                or CompilerCollectedBindingKind.BlockAlias
                or CompilerCollectedBindingKind.LoopHeadAlias
                or CompilerCollectedBindingKind.CatchAlias
                or CompilerCollectedBindingKind.ClassLexicalAlias => JsLocalDebugFlags.Lexical,
                _ => JsLocalDebugFlags.None,
            };
            if (binding.Planned.IsConst)
                flags |= JsLocalDebugFlags.Const;
            if (binding.Planned.IsCaptured)
                flags |= JsLocalDebugFlags.CapturedByChild;
            if (binding.Planned.Kind == CompilerCollectedBindingKind.FunctionNameSelf)
                flags |= JsLocalDebugFlags.ImmutableFunctionName;

            builder.AddLocalDebugInfo(new(name, storageKind, storageIndex, startPc, endPc, flags));
        }
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

    protected JsCompilerBase(
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
        int ContextSlotCount,
        int DebugStartPc = 0
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
