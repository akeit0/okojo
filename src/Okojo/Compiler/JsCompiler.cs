using Okojo.Bytecode;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Compiler;

public sealed partial class JsCompiler : IDisposable
{
    private const string SuperBaseInternalBindingName = "$[[SuperBase]]";
    private const string SyntheticArgumentsBindingName = "$arguments";
    private const string DerivedThisInternalBindingName = "$[[DerivedThis]]";
    private static readonly int SyntheticArgumentsSymbolId = CompilerSymbolId.SyntheticArguments.Value;
    private static readonly int DerivedThisSymbolId = CompilerSymbolId.DerivedThis.Value;
    private static readonly int SuperBaseSymbolId = CompilerSymbolId.SuperBase.Value;
    private readonly Stack<bool> activeAbruptEmptyNormalizations;
    private readonly Stack<LexicalAliasScope> activeBlockLexicalAliases;
    private readonly Stack<IReadOnlyDictionary<string, PrivateFieldBinding>> activeClassPrivateBindingScopes;
    private readonly Stack<ActiveClassPrivateSourceScope> activeClassPrivateSourceScopes;
    private readonly Stack<FinallyFlowContext> activeFinallyFlow;
    private readonly Stack<ForAwaitLoopContext> activeForAwaitLoops;
    private readonly Stack<ForOfIteratorLoopContext> activeForOfIteratorLoops;
    private readonly Stack<int> activeLocalDebugScopeStarts;
    private readonly Stack<HashSet<int>> activePerIterationContextSlots;
    private readonly Stack<StatementCompletionState> activeStatementCompletionStates;
    private readonly Stack<int> activeSwitchCompletionRegisters;
    private readonly Stack<BytecodeBuilder.Label> breakTargets;

    private readonly BytecodeBuilder builder;
    private readonly CompilerIdentifierName? classLexicalNameForMethodResolution;
    private readonly IReadOnlyDictionary<string, PrivateFieldBinding>? classPrivateNameToBinding;
    private readonly JsCompilerContext? compileContext;

    private readonly Dictionary<int, int> currentContextPerIterationBaseDepthBySymbol;
    private readonly Dictionary<int, int> currentContextSlotById;
    private readonly bool emitImplicitSuperForwardAll;
    private readonly HashSet<int> forcedAliasContextSlotSymbolIds;
    private readonly bool forceModuleFunctionContext;
    private readonly Dictionary<int, List<BlockLexicalBinding>> forHeadLexicalsByPosition;
    private readonly Dictionary<int, List<BlockLexicalBinding>> forInOfHeadLexicalsByPosition;
    private readonly Dictionary<int, List<BlockLexicalBinding>> forInOfHeadTdzLexicalsByPosition;
    private readonly JsBytecodeFunctionKind functionKind;
    private readonly List<int> generatorResumeTargetPcBySuspendId;
    private readonly bool hasSuperReference;
    private readonly HashSet<int> initializedParameterBindingIds;
    private readonly bool isArrowFunction;
    private readonly bool isDerivedConstructor;
    private readonly HashSet<int> knownInitializedLexicals;
    private readonly Stack<LabeledTargets> labeledTargets;
    private readonly Dictionary<int, LocalBindingInfo> localBindingInfoById;
    private readonly HashSet<int> localRegisters;
    private readonly Dictionary<int, int> locals;
    private readonly Stack<LoopTargets> loopTargets;
    private readonly IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings;
    private readonly Dictionary<int, List<BlockLexicalBinding>> nestedBlockLexicals;
    private readonly JsCompiler? parent;
    private readonly bool requiresArgumentsObject;
    private readonly HashSet<int> skipLexicalRegisterPrologueHoleInit;
    private readonly Dictionary<int, int> suspendPinnedRegisterRefCounts;
    private readonly HashSet<int> switchLexicalInternalNames;
    private readonly Dictionary<int, List<BlockLexicalBinding>> switchLexicalsByPosition;
    private readonly Dictionary<int, string> symbolNamesById;
    private readonly Dictionary<string, int> syntheticSymbolIdsByName;
    private readonly Dictionary<string, int> topLevelLexicalDeclarationPositionByName;
    private readonly bool useMethodEnvironmentCapture;
    private int activeCatchableTryDepth;
    private int blockLexicalUniqueId;
    private int cachedNewTargetRegister = -1;
    private int[]? compiledArgumentsMappedSlots;
    private bool compiledFunctionHasNewTarget;
    private (int Atom, int Slot, bool IsConst)[]? compiledTopLevelLexicalBindings;
    private FunctionParameterPlan? currentFunctionParameterPlan;
    private int derivedThisContextSlot = -1;
    private bool disposed;
    private bool emittingParameterInitializers;
    private bool enableAliasScopeRegisterAllocation;
    private int finallyTempUniqueId;
    private bool functionHasParameterExpressions;
    private int generatorResumeModeTempRegister = -1;
    private int generatorResumeValueTempRegister = -1;
    private int generatorSwitchInstructionPc = -1;
    private bool hasEmittedDeferredInstanceInitializers;
    private JsIdentifierTable? identifierTable;
    private int lexicalThisContextDepth = -1;
    private int lexicalThisContextSlot = -1;
    private int nextGeneratorSuspendId;
    private int nextSyntheticSymbolOrdinal = 1;
    private IReadOnlyList<InstanceFieldInitializerPlan>? pendingInstanceFieldInitializers;
    private IReadOnlyList<PrivateAccessorInitPlan>? pendingPrivateAccessorInitializers;
    private IReadOnlyList<PrivateFieldInitPlan>? pendingPrivateFieldInitializers;
    private IReadOnlyList<PrivateMethodInitPlan>? pendingPrivateMethodInitializers;
    private IReadOnlyList<PublicFieldInitPlan>? pendingPublicFieldInitializers;
    private bool requiresClosureBinding;
    private SourceCode? sourceCode;
    private bool strictDeclared;
    private int superBaseContextSlot = -1;
    private int syntheticArgumentsRegister = -1;
    private bool usesClassLexicalBinding;

    public JsCompiler(JsRealm realm)
        : this(
            realm,
            null,
            null,
            null,
            false,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null)
    {
    }

    public JsCompiler(JsRealm realm, JsCompilerContext compileContext)
        : this(
            realm,
            null,
            compileContext,
            null,
            false,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null)
    {
    }

    public JsCompiler(JsRealm realm, JsCompilerContext compileContext, JsBytecodeFunctionKind topLevelFunctionKind)
        : this(
            realm,
            null,
            compileContext,
            null,
            false,
            topLevelFunctionKind,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null)
    {
    }

    internal JsCompiler(
        JsRealm realm,
        IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings,
        bool forceModuleFunctionContext = true)
        : this(
            realm,
            null,
            null,
            moduleVariableBindings,
            forceModuleFunctionContext,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null)
    {
    }

    private JsCompiler(
        JsRealm realm,
        JsCompiler? parent,
        JsCompilerContext? compileContext,
        IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings,
        bool forceModuleFunctionContext,
        JsBytecodeFunctionKind functionKind,
        bool isArrowFunction,
        bool requiresArgumentsObject,
        bool isDerivedConstructor,
        bool emitImplicitSuperForwardAll,
        bool hasSuperReference,
        bool useMethodEnvironmentCapture,
        CompilerIdentifierName? classLexicalNameForMethodResolution,
        IReadOnlyDictionary<string, PrivateFieldBinding>? classPrivateNameToBinding,
        SourceCode? sourceCode)
    {
        this.parent = parent;
        this.compileContext = compileContext;
        this.moduleVariableBindings = moduleVariableBindings;
        this.forceModuleFunctionContext = forceModuleFunctionContext;

        this.functionKind = functionKind;
        this.isArrowFunction = isArrowFunction;
        this.requiresArgumentsObject = requiresArgumentsObject;
        this.isDerivedConstructor = isDerivedConstructor;
        this.emitImplicitSuperForwardAll = emitImplicitSuperForwardAll;
        this.hasSuperReference = hasSuperReference;
        this.useMethodEnvironmentCapture = useMethodEnvironmentCapture;
        this.classLexicalNameForMethodResolution = classLexicalNameForMethodResolution;
        this.sourceCode = sourceCode;
        Vm = realm;
        builder = new(Vm);
        this.classPrivateNameToBinding = classPrivateNameToBinding;
        locals = Vm.RentCompileDictionary<int, int>(32);
        localBindingInfoById = Vm.RentCompileDictionary<int, LocalBindingInfo>(32);
        symbolNamesById = Vm.RentCompileDictionary<int, string>(32);
        syntheticSymbolIdsByName = Vm.RentCompileDictionary<string, int>(32, StringComparer.Ordinal);
        localRegisters = Vm.RentCompileHashSet<int>(32);
        currentContextSlotById = Vm.RentCompileDictionary<int, int>(16);
        currentContextPerIterationBaseDepthBySymbol = Vm.RentCompileDictionary<int, int>(16);
        nestedBlockLexicals = Vm.RentCompileDictionary<int, List<BlockLexicalBinding>>(8);
        switchLexicalsByPosition = Vm.RentCompileDictionary<int, List<BlockLexicalBinding>>(8);
        forHeadLexicalsByPosition = Vm.RentCompileDictionary<int, List<BlockLexicalBinding>>(8);
        forInOfHeadLexicalsByPosition = Vm.RentCompileDictionary<int, List<BlockLexicalBinding>>(8);
        forInOfHeadTdzLexicalsByPosition = Vm.RentCompileDictionary<int, List<BlockLexicalBinding>>(8);
        forcedAliasContextSlotSymbolIds = Vm.RentCompileHashSet<int>(16);
        activeBlockLexicalAliases = Vm.RentCompileStack<LexicalAliasScope>(8);
        activeLocalDebugScopeStarts = Vm.RentCompileStack<int>(8);
        activePerIterationContextSlots = Vm.RentCompileStack<HashSet<int>>(4);
        breakTargets = Vm.RentCompileStack<BytecodeBuilder.Label>(8);
        loopTargets = Vm.RentCompileStack<LoopTargets>(8);
        labeledTargets = Vm.RentCompileStack<LabeledTargets>(8);
        activeStatementCompletionStates = Vm.RentCompileStack<StatementCompletionState>(8);
        activeAbruptEmptyNormalizations = Vm.RentCompileStack<bool>(8);
        activeSwitchCompletionRegisters = Vm.RentCompileStack<int>(4);
        activeFinallyFlow = Vm.RentCompileStack<FinallyFlowContext>(8);
        activeForAwaitLoops = Vm.RentCompileStack<ForAwaitLoopContext>(4);
        activeForOfIteratorLoops = Vm.RentCompileStack<ForOfIteratorLoopContext>(4);
        activeClassPrivateBindingScopes =
            Vm.RentCompileStack<IReadOnlyDictionary<string, PrivateFieldBinding>>(4);
        activeClassPrivateSourceScopes = Vm.RentCompileStack<ActiveClassPrivateSourceScope>(4);
        skipLexicalRegisterPrologueHoleInit = Vm.RentCompileHashSet<int>(16);
        knownInitializedLexicals = Vm.RentCompileHashSet<int>(16);
        topLevelLexicalDeclarationPositionByName =
            Vm.RentCompileDictionary<string, int>(16, StringComparer.Ordinal);
        switchLexicalInternalNames = Vm.RentCompileHashSet<int>(16);
        generatorResumeTargetPcBySuspendId = Vm.RentCompileList<int>(8);
        suspendPinnedRegisterRefCounts = Vm.RentCompileDictionary<int, int>(8);
        initializedParameterBindingIds = Vm.RentCompileHashSet<int>(8);
        syntheticSymbolIdsByName[SyntheticArgumentsBindingName] = SyntheticArgumentsSymbolId;
        syntheticSymbolIdsByName[DerivedThisInternalBindingName] = DerivedThisSymbolId;
        syntheticSymbolIdsByName[SuperBaseInternalBindingName] = SuperBaseSymbolId;
        if (sourceCode?.Source is not null)
            builder.SetSourceText(sourceCode.Source);
    }

    private JsCompiler(JsCompiler parent, JsBytecodeFunctionKind functionKind = 0,
        bool isArrowFunction = false, bool requiresArgumentsObject = false,
        bool isDerivedConstructor = false,
        bool emitImplicitSuperForwardAll = false,
        bool hasSuperReference = false,
        bool useMethodEnvironmentCapture = false,
        bool forceModuleFunctionContext = false,
        CompilerIdentifierName? classLexicalNameForMethodResolution = null,
        IReadOnlyDictionary<string, PrivateFieldBinding>? classPrivateNameToBinding = null)
        : this(
            parent.Vm,
            parent,
            parent.compileContext,
            parent.moduleVariableBindings,
            forceModuleFunctionContext,
            functionKind,
            isArrowFunction,
            requiresArgumentsObject,
            isDerivedConstructor,
            emitImplicitSuperForwardAll,
            hasSuperReference,
            useMethodEnvironmentCapture,
            classLexicalNameForMethodResolution,
            classPrivateNameToBinding,
            parent.sourceCode)
    {
        identifierTable = parent.identifierTable;
    }

    private string? CurrentSourceText => sourceCode?.Source;
    private string? CurrentSourcePath => sourceCode?.Path;

    public JsRealm Vm { get; }

    private bool CanReturnNormally =>
        activeForAwaitLoops.Count == 0 &&
        activeFinallyFlow.Count == 0 &&
        activeForOfIteratorLoops.Count == 0;

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        builder.Dispose();

        foreach (var kv in nestedBlockLexicals)
        {
            var list = kv.Value;
            list.Clear();
            Vm.ReturnCompileList(list);
        }

        foreach (var kv in switchLexicalsByPosition)
        {
            var list = kv.Value;
            list.Clear();
            Vm.ReturnCompileList(list);
        }

        foreach (var kv in forHeadLexicalsByPosition)
        {
            var list = kv.Value;
            list.Clear();
            Vm.ReturnCompileList(list);
        }

        foreach (var kv in forInOfHeadLexicalsByPosition)
        {
            var list = kv.Value;
            list.Clear();
            Vm.ReturnCompileList(list);
        }

        foreach (var kv in forInOfHeadTdzLexicalsByPosition)
        {
            var list = kv.Value;
            list.Clear();
            Vm.ReturnCompileList(list);
        }

        while (activeBlockLexicalAliases.Count != 0)
        {
            var aliasScope = activeBlockLexicalAliases.Pop();
            if (aliasScope.OwnsBindings && aliasScope.Bindings is List<BlockLexicalBinding> ownedBindings)
                Vm.ReturnCompileList(ownedBindings);
        }

        while (activePerIterationContextSlots.Count != 0)
        {
            var slotSet = activePerIterationContextSlots.Pop();
            Vm.ReturnCompileHashSet(slotSet);
        }

        Vm.ReturnCompileDictionary(locals);
        Vm.ReturnCompileDictionary(localBindingInfoById);
        Vm.ReturnCompileDictionary(symbolNamesById);
        Vm.ReturnCompileDictionary(syntheticSymbolIdsByName);
        Vm.ReturnCompileHashSet(localRegisters);
        Vm.ReturnCompileDictionary(currentContextSlotById);
        Vm.ReturnCompileDictionary(currentContextPerIterationBaseDepthBySymbol);
        Vm.ReturnCompileDictionary(nestedBlockLexicals);
        Vm.ReturnCompileDictionary(switchLexicalsByPosition);
        Vm.ReturnCompileDictionary(forHeadLexicalsByPosition);
        Vm.ReturnCompileDictionary(forInOfHeadLexicalsByPosition);
        Vm.ReturnCompileDictionary(forInOfHeadTdzLexicalsByPosition);
        Vm.ReturnCompileHashSet(forcedAliasContextSlotSymbolIds);
        Vm.ReturnCompileStack(activeBlockLexicalAliases);
        Vm.ReturnCompileStack(activeLocalDebugScopeStarts);
        Vm.ReturnCompileStack(activePerIterationContextSlots);
        Vm.ReturnCompileStack(breakTargets);
        Vm.ReturnCompileStack(loopTargets);
        Vm.ReturnCompileStack(labeledTargets);
        Vm.ReturnCompileStack(activeStatementCompletionStates);
        Vm.ReturnCompileStack(activeAbruptEmptyNormalizations);
        Vm.ReturnCompileStack(activeSwitchCompletionRegisters);
        Vm.ReturnCompileStack(activeFinallyFlow);
        Vm.ReturnCompileStack(activeForAwaitLoops);
        Vm.ReturnCompileStack(activeForOfIteratorLoops);
        Vm.ReturnCompileStack(activeClassPrivateBindingScopes);
        Vm.ReturnCompileStack(activeClassPrivateSourceScopes);
        Vm.ReturnCompileHashSet(skipLexicalRegisterPrologueHoleInit);
        Vm.ReturnCompileHashSet(knownInitializedLexicals);
        Vm.ReturnCompileDictionary(topLevelLexicalDeclarationPositionByName);
        Vm.ReturnCompileHashSet(switchLexicalInternalNames);
        Vm.ReturnCompileList(generatorResumeTargetPcBySuspendId);
        Vm.ReturnCompileDictionary(suspendPinnedRegisterRefCounts);
        Vm.ReturnCompileHashSet(initializedParameterBindingIds);
    }

    public static JsScript Compile(JsRealm realm, JsProgram program)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(program);

        using var compiler = new JsCompiler(realm);
        return compiler.Compile(program);
    }

    public static JsScript Compile(JsRealm realm, JsProgram program, JsCompilerContext compileContext)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(compileContext);

        using var compiler = new JsCompiler(realm, compileContext);
        return compiler.Compile(program);
    }

    public static JsScript Compile(
        JsRealm realm,
        JsProgram program,
        JsCompilerContext compileContext,
        JsBytecodeFunctionKind topLevelFunctionKind)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(compileContext);

        using var compiler = new JsCompiler(realm, compileContext, topLevelFunctionKind);
        return compiler.Compile(program);
    }

    public JsScript Compile(JsProgram program)
    {
        sourceCode = new(program.SourceText, program.SourcePath);
        identifierTable = program.IdentifierTable;
        strictDeclared = program.StrictDeclared;
        builder.SetSourceText(sourceCode.Source);
        builder.SetStrictDeclared(program.StrictDeclared);
        // 1. Lexical names in root
        if (!IsReplTopLevelMode())
            foreach (var name in program.TopLevelLexicalNames)
                GetOrCreateLocal(name);

        // 2. Script statements (function declarations are hoisted in CompileStatements after context analysis)
        var script = CompileStatements(program.Statements) with
        {
            SourcePath = program.SourcePath,
            SourceCode = sourceCode
        };
        script.BindAgent(Vm.Agent);
        return script;
    }

    public static JsCompiler CreateForModuleExecution(
        JsRealm realm,
        IReadOnlyDictionary<string, ModuleVariableBinding>? moduleVariableBindings,
        bool forceModuleFunctionContext = true)
    {
        return new(realm, moduleVariableBindings, forceModuleFunctionContext);
    }

    private static CompiledFunctionShape CreateFunctionShape(
        bool isGenerator,
        bool isAsync,
        bool isArrow = false,
        bool isMethod = false,
        bool isImplicitlyStrict = false,
        bool isClassConstructor = false,
        bool isDerivedConstructor = false,
        bool emitImplicitSuperForwardAll = false)
    {
        return new(
            GetFunctionKind(isGenerator, isAsync),
            isArrow,
            isMethod,
            isImplicitlyStrict,
            isClassConstructor,
            isDerivedConstructor,
            emitImplicitSuperForwardAll);
    }

    public JsBytecodeFunction CompileFunction(JsFunctionExpression functionExpression, string? name = null)
    {
        var parameterPlan = FunctionParameterPlan.FromFunction(functionExpression);
        return CompileFunctionObject(
            name ?? functionExpression.Name,
            parameterPlan,
            functionExpression.Body,
            CreateFunctionShape(
                functionExpression.IsGenerator,
                functionExpression.IsAsync,
                functionExpression.IsArrow),
            true,
            sourceStartPosition: functionExpression.Position);
    }

    private void HoistFunction(JsFunctionDeclaration f)
    {
        var funcObj = CompileHoistedFunctionTemplate(f);
        if (funcObj.RequiresClosureBinding) requiresClosureBinding = true;
        var idx = builder.AddObjectConstant(funcObj);
        EmitCreateClosureByIndex(idx);
        EmitInheritCurrentFunctionPrivateBrandStateIfNeeded();
        var hasResolvedLocalTarget =
            TryGetResolvedAliasSymbolId(new CompilerIdentifierName(f.Name, f.NameId), out var resolvedSymbolId,
                out _) &&
            (HasLocalBinding(resolvedSymbolId) || TryGetCurrentContextSlot(resolvedSymbolId, out _));
        if (!hasResolvedLocalTarget &&
            UsesGlobalScriptBindingsMode() &&
            !HasLocalBinding(f.Name) &&
            !TryGetCurrentContextSlot(f.Name, out _))
        {
            var nameIdx = builder.AddAtomizedStringConstant(f.Name);
            EmitStaGlobalFunctionDeclarationByIndex(nameIdx, builder.GetOrAllocateGlobalBindingFeedbackSlot(f.Name));
            return;
        }

        StoreIdentifier(f.Name, true);
    }

    private JsBytecodeFunction CompileFunctionObject(
        string? name,
        FunctionParameterPlan parameterPlan,
        JsBlockStatement body,
        CompiledFunctionShape functionShape,
        bool immutableSelfBinding = false,
        bool useMethodEnvironmentCapture = false,
        CompilerIdentifierName? classLexicalNameForMethodResolution = null,
        int sourceStartPosition = -1,
        int sourceEndPosition = -1,
        IReadOnlyDictionary<string, PrivateFieldBinding>? classPrivateNameToBinding = null,
        IReadOnlyList<PrivateFieldInitPlan>? privateFieldInitializers = null,
        IReadOnlyList<PrivateAccessorInitPlan>? privateAccessorInitializers = null,
        IReadOnlyList<PrivateMethodInitPlan>? privateMethodInitializers = null,
        IReadOnlyList<InstanceFieldInitializerPlan>? instanceFieldInitializers = null,
        IReadOnlyList<PublicFieldInitPlan>? publicFieldInitializers = null,
        bool forceHasSuperReference = false)
    {
        var parameters = parameterPlan.Names;
        var parameterInitializers = parameterPlan.Initializers;
        var effectivePrivateNameToBinding = classPrivateNameToBinding ?? GetVisiblePrivateNameBindings();
        var requiresArgumentsObject =
            ShouldCreateArgumentsObjectForFunction(parameters, parameterInitializers, body, functionShape.IsArrow);
        var hasSuperReference = forceHasSuperReference || FunctionUsesSuper(parameterInitializers, body);
        var needsMethodEnvironmentForNestedArrowSuper =
            !functionShape.IsArrow && FunctionUsesSuperInNestedArrows(parameterInitializers, body);
        var effectiveUseMethodEnvironmentCapture =
            useMethodEnvironmentCapture || needsMethodEnvironmentForNestedArrowSuper;

        if (functionShape.IsArrow)
            EnsureDerivedThisContextSlotIfNeeded();

        // IMPORTANT: New compiler for function body starts with fresh locals
        using var funcCompiler = new JsCompiler(
            this,
            functionShape.Kind,
            functionShape.IsArrow,
            requiresArgumentsObject,
            functionShape.IsDerivedConstructor,
            functionShape.EmitImplicitSuperForwardAll,
            useMethodEnvironmentCapture: effectiveUseMethodEnvironmentCapture,
            hasSuperReference: hasSuperReference,
            classLexicalNameForMethodResolution: classLexicalNameForMethodResolution,
            classPrivateNameToBinding: effectivePrivateNameToBinding);
        if (activeBlockLexicalAliases.Count != 0)
        {
            var aliasScopes = activeBlockLexicalAliases.ToArray();
            for (var i = aliasScopes.Length - 1; i >= 0; i--)
                funcCompiler.activeBlockLexicalAliases.Push(
                    aliasScopes[i] with { OwnsBindings = false, Inherited = true });
        }

        if (functionShape.IsArrow &&
            funcCompiler.TryResolveDerivedThisContextAccess(out var lexicalThisSlot, out var lexicalThisDepth))
        {
            funcCompiler.lexicalThisContextSlot = lexicalThisSlot;
            funcCompiler.lexicalThisContextDepth = lexicalThisDepth;
        }

        funcCompiler.RegisterPrivateFieldDebugNamesForFunction(effectivePrivateNameToBinding);

        funcCompiler.pendingPrivateFieldInitializers = privateFieldInitializers;
        funcCompiler.pendingPrivateAccessorInitializers = privateAccessorInitializers;
        funcCompiler.pendingPrivateMethodInitializers = privateMethodInitializers;
        funcCompiler.pendingInstanceFieldInitializers = instanceFieldInitializers;
        funcCompiler.pendingPublicFieldInitializers = publicFieldInitializers;

        // Parameters start at R0, R1...
        foreach (var binding in parameterPlan.Bindings)
        {
            funcCompiler.GetOrCreateLocal(new CompilerIdentifierName(binding.Name, binding.NameId));
            for (var i = 0; i < binding.BoundIdentifiers.Count; i++)
            {
                var boundIdentifier = binding.BoundIdentifiers[i];
                funcCompiler.GetOrCreateLocal(new CompilerIdentifierName(boundIdentifier.Name, boundIdentifier.NameId));
            }
        }

        var initializeSelf = immutableSelfBinding && !string.IsNullOrEmpty(name);
        var selfBindingInternalName = initializeSelf
            ? $"{name}#nfe{sourceStartPosition}"
            : null;
        if (initializeSelf && selfBindingInternalName is not null)
            funcCompiler.PushInheritedAliasScope(new(name!), selfBindingInternalName);
        var effectiveStrictDeclared =
            strictDeclared || body.StrictDeclared || functionShape.IsImplicitlyStrict ||
            functionShape.IsClassConstructor;
        funcCompiler.strictDeclared = effectiveStrictDeclared;
        funcCompiler.builder.SetStrictDeclared(effectiveStrictDeclared);
        JsScript script;
        try
        {
            script = funcCompiler.CompileStatementsCore(
                body.Statements,
                initializeSelf ? selfBindingInternalName : null,
                parameterPlan);
        }
        finally
        {
            if (initializeSelf && selfBindingInternalName is not null)
                funcCompiler.PopAliasScope();
        }

        var functionSourceText = TryGetFunctionSourceText(sourceStartPosition,
            sourceEndPosition >= 0 ? sourceEndPosition : body.EndPosition);
        var function = new JsBytecodeFunction(Vm, script with { FunctionSourceText = functionSourceText },
            name ?? "", funcCompiler.requiresClosureBinding,
            effectiveStrictDeclared,
            hasNewTarget: funcCompiler.compiledFunctionHasNewTarget, kind: functionShape.Kind,
            isArrow: functionShape.IsArrow, isMethod: functionShape.IsMethod,
            formalParameterCount: parameters.Count, hasSimpleParameterList: parameterPlan.HasSimpleParameterList,
            isClassConstructor: functionShape.IsClassConstructor,
            isDerivedConstructor: functionShape.IsDerivedConstructor,
            hasEagerGeneratorParameterBinding: funcCompiler.functionHasParameterExpressions &&
                                               functionShape.Kind is JsBytecodeFunctionKind.Generator
                                                   or JsBytecodeFunctionKind.AsyncGenerator,
            expectedArgumentCount: parameterPlan.FunctionLength);
        function.SuperBaseContextSlot = funcCompiler.superBaseContextSlot;
        function.DerivedThisContextSlot = funcCompiler.derivedThisContextSlot;
        function.LexicalThisContextSlot = funcCompiler.lexicalThisContextSlot;
        function.LexicalThisContextDepth = funcCompiler.lexicalThisContextDepth;
        function.ArgumentsMappedSlots = funcCompiler.compiledArgumentsMappedSlots;
        function.UsesClassLexicalBinding = funcCompiler.usesClassLexicalBinding;
        function.UsesMethodEnvironmentCapture = funcCompiler.useMethodEnvironmentCapture;
        return function;
    }

    public JsScript CompileStatements(
        IEnumerable<JsStatement> statements,
        string? selfName = null,
        IReadOnlyList<string>? parameterNames = null,
        IReadOnlyList<JsExpression?>? parameterInitializers = null,
        int restParameterIndex = -1)
    {
        var parameterPlan = parameterNames is null || parameterInitializers is null
            ? null
            : FunctionParameterPlan.FromCompilerInputs(parameterNames, null, parameterInitializers,
                restParameterIndex);
        return CompileStatementsCore(statements, selfName, parameterPlan);
    }

    private JsScript CompileStatementsCore(
        IEnumerable<JsStatement> statements,
        string? selfName,
        FunctionParameterPlan? parameterPlan = null)
    {
        cachedNewTargetRegister = -1;
        compiledFunctionHasNewTarget = false;
        nextGeneratorSuspendId = 0;
        generatorSwitchInstructionPc = -1;
        generatorResumeTargetPcBySuspendId.Clear();
        generatorResumeValueTempRegister = -1;
        generatorResumeModeTempRegister = -1;
        superBaseContextSlot = -1;
        derivedThisContextSlot = -1;
        currentFunctionParameterPlan = parameterPlan;
        compiledArgumentsMappedSlots = null;
        topLevelLexicalDeclarationPositionByName.Clear();
        forcedAliasContextSlotSymbolIds.Clear();
        ClearParameterBindingFlags();
        if (currentFunctionParameterPlan is not null)
            foreach (var parameter in currentFunctionParameterPlan.Bindings)
            {
                var parameterName = parameter.Name;
                MarkParameterBinding(parameterName);
                for (var i = 0; i < parameter.BoundIdentifiers.Count; i++)
                    MarkParameterBinding(parameter.BoundIdentifiers[i].Name);
            }

        functionHasParameterExpressions =
            currentFunctionParameterPlan is not null &&
            (currentFunctionParameterPlan.HasInitializers || currentFunctionParameterPlan.HasPatternBindings);
        emittingParameterInitializers = false;
        ClearInitializedParameterBindings();
        hasEmittedDeferredInstanceInitializers = false;
        var statementList = statements as IReadOnlyList<JsStatement> ?? statements.ToList();

        if (selfName != null)
        {
            GetOrCreateLocal(selfName);
            MarkImmutableFunctionNameBinding(selfName);
        }

        if (functionKind != JsBytecodeFunctionKind.Normal)
        {
            generatorSwitchInstructionPc = builder.CodeLength;
            EmitRaw(JsOpCode.SwitchOnGeneratorState, 0xFF, 0, 0);
        }

        PredeclareLocals(statementList, false);
        if (requiresArgumentsObject)
            EnsureSyntheticArgumentsRegister();
        ClearCapturedByChildFlags();
        PrecomputeDirectChildCaptures(statementList);
        AssignCurrentContextSlots();
        ComputeArgumentsMappedSlots();
        ComputeSafeLexicalRegisterPrologueHoleInitSkips(statementList);

        EmitFunctionContextPrologueIfNeeded();
        EmitSuperBaseContextSlotInitIfNeeded();
        ValidateTopLevelRestrictedLexicalDeclarations();

        if (emitImplicitSuperForwardAll)
        {
            EmitCallRuntime(RuntimeId.CallSuperConstructorForwardAll, 0, 0);
            if (isDerivedConstructor && HasPendingInstanceInitializers())
            {
                hasEmittedDeferredInstanceInitializers = true;
                EmitPendingInstanceInitializers();
            }
        }

        EmitTopLevelVarBindingPrologue();

        if (requiresArgumentsObject)
        {
            var argumentsReg = EnsureSyntheticArgumentsRegister();
            EmitRaw(JsOpCode.CreateMappedArguments);
            EmitStarRegister(argumentsReg);
            if (currentContextSlotById.TryGetValue(SyntheticArgumentsSymbolId, out var argumentsSlot))
            {
                EmitLdaRegister(argumentsReg);
                EmitStaCurrentContextSlot(argumentsSlot);
            }
        }

        EmitRestParameterPrologue();

        EmitRegisterLocalsPrologueInitialization();
        enableAliasScopeRegisterAllocation = true;
        try
        {
            if (selfName != null)
            {
                builder.EmitLda(JsOpCode.LdaCurrentFunction);
                StoreIdentifier(selfName, true, selfName);
            }

            EmitParameterInitializerAndPatternPrologue();
            EmitHoistedFunctionDeclarations(statementList);
            if (functionHasParameterExpressions &&
                functionKind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator)
            {
                EmitLdaUndefined();
                EmitGeneratorSuspendResume(minimizeLiveRange: true, guaranteedNextOnly: true, isPrestartSuspend: true);
            }

            // Match V8-like lowering shape: materialize lexical new.target register
            // only for functions that actually reference new.target.
            var newTargetCount = CountNewTargetExpressions(statementList);
            compiledFunctionHasNewTarget = newTargetCount > 0;
            if (compiledFunctionHasNewTarget)
            {
                cachedNewTargetRegister = AllocateSyntheticLocal("$new.target");
                builder.EmitLda(JsOpCode.LdaNewTarget);
                EmitStarRegister(cachedNewTargetRegister);
            }

            if (!isDerivedConstructor && HasPendingInstanceInitializers()) EmitPendingInstanceInitializers();

            var trackScriptCompletion =
                IsTopLevelScriptMode() && !StatementListLeavesDirectCompletionValue(statementList);
            var scriptCompletionReg = -1;
            if (trackScriptCompletion)
            {
                scriptCompletionReg = AllocateTemporaryRegister();
                EmitLdaTheHole();
                EmitStarRegister(scriptCompletionReg);
                PushStatementCompletionState(scriptCompletionReg, false);
            }

            // Keep bytecode compact and closer to reference engines: do not emit
            // dead statements that appear after an unconditional return.
            var bodyStatementResultsUsed = parent is null && moduleVariableBindings is null;
            var alwaysReturns = false;
            JsStatement? lastReachableStatement = null;
            try
            {
                void EmitBodyStatementList()
                {
                    foreach (var statement in statementList)
                    {
                        lastReachableStatement = statement;
                        VisitStatement(statement, bodyStatementResultsUsed);
                        if (StatementAlwaysReturns(statement) || StatementNeverCompletesNormally(statement))
                        {
                            alwaysReturns = true;
                            break;
                        }
                    }
                }

                if (BlockNeedsExplicitResourceScope(statementList))
                    EmitExplicitResourceScope(EmitBodyStatementList, BlockNeedsAsyncExplicitResourceScope(statementList));
                else
                    EmitBodyStatementList();
            }
            finally
            {
                if (trackScriptCompletion) PopStatementCompletionState();
            }

            EmitModuleLocalLiveExportEpilogue();
            if (!alwaysReturns)
            {
                if (trackScriptCompletion)
                    EmitLoadRegisterOrUndefinedIfHole(scriptCompletionReg);
                else if ((parent is not null || moduleVariableBindings is not null) &&
                         (lastReachableStatement is null ||
                          !StatementLeavesKnownUndefinedValueInCurrentContext(lastReachableStatement)))
                    EmitLdaUndefined();
                EmitRaw(JsOpCode.Return);
            }

            if (trackScriptCompletion)
                ReleaseTemporaryRegister(scriptCompletionReg);

            if (functionKind != JsBytecodeFunctionKind.Normal)
                PatchGeneratorSwitchTable();
        }
        finally
        {
            enableAliasScopeRegisterAllocation = false;
        }

        EmitRootLocalDebugInfos();
        compiledTopLevelLexicalBindings = BuildTopLevelLexicalBindingsMetadata();
        var script = builder.ToScript();
        return script with
        {
            SourcePath = CurrentSourcePath,
            SourceCode = sourceCode,
            TopLevelLexicalAtoms = compiledTopLevelLexicalBindings?.Select(static entry => entry.Atom).ToArray(),
            TopLevelLexicalSlots = compiledTopLevelLexicalBindings?.Select(static entry => entry.Slot).ToArray(),
            TopLevelLexicalConstFlags = compiledTopLevelLexicalBindings?.Select(static entry => entry.IsConst).ToArray()
        };
    }

    private (int Atom, int Slot, bool IsConst)[]? BuildTopLevelLexicalBindingsMetadata()
    {
        if (!UsesPersistentGlobalLexicalBindingsMode())
            return null;

        List<(int Atom, int Slot, bool IsConst)>? bindings = null;
        foreach (var kvp in currentContextSlotById.OrderBy(static kvp => kvp.Value))
        {
            if (!IsLexicalLocalBinding(kvp.Key))
                continue;

            var name = GetSymbolName(kvp.Key);
            bindings ??= new(4);
            bindings.Add((Vm.Atoms.InternNoCheck(name), kvp.Value, IsConstLocalBinding(kvp.Key)));
        }

        return bindings?.ToArray();
    }

    private int AllocateClassPrivateFieldBrandId()
    {
        return Vm.Agent.AllocatePrivateBrandId();
    }

    private void EnsureDerivedThisContextSlotIfNeeded()
    {
        if (!isDerivedConstructor || derivedThisContextSlot >= 0)
            return;

        derivedThisContextSlot = currentContextSlotById.Count;
        currentContextSlotById[DerivedThisSymbolId] = derivedThisContextSlot;
    }

    private IReadOnlyDictionary<string, PrivateFieldBinding>? GetVisiblePrivateNameBindings()
    {
        if (activeClassPrivateBindingScopes.Count != 0)
            return activeClassPrivateBindingScopes.Peek();
        return classPrivateNameToBinding;
    }

    private IReadOnlyList<int>? CollectVisiblePrivateBrandIds()
    {
        var visiblePrivateBindings = GetVisiblePrivateNameBindings();
        if (visiblePrivateBindings is null || visiblePrivateBindings.Count == 0)
            return null;

        List<int>? brandIds = null;
        foreach (var binding in visiblePrivateBindings.Values)
        {
            brandIds ??= new(2);
            if (!brandIds.Contains(binding.BrandId))
                brandIds.Add(binding.BrandId);
        }

        return brandIds;
    }

    private bool TryResolvePrivateMemberBinding(JsMemberExpression member, out PrivateFieldBinding binding)
    {
        binding = default;
        if (!member.IsPrivate || member.IsComputed)
            return false;
        if (member.Property is not JsLiteralExpression { Value: string sourcePrivateName })
            return false;
        var visiblePrivateBindings = GetVisiblePrivateNameBindings();
        if (visiblePrivateBindings is null ||
            !visiblePrivateBindings.TryGetValue(sourcePrivateName, out var resolvedBinding))
            throw new NotSupportedException($"Private name '{sourcePrivateName}' is not declared in this class scope.");

        binding = resolvedBinding;
        return true;
    }

    private bool TryResolvePrivateIdentifierBinding(JsPrivateIdentifierExpression identifier,
        out PrivateFieldBinding binding)
    {
        binding = default;
        var visiblePrivateBindings = GetVisiblePrivateNameBindings();
        if (visiblePrivateBindings is null ||
            !visiblePrivateBindings.TryGetValue(identifier.Name, out var resolvedBinding))
            throw new NotSupportedException($"Private name '{identifier.Name}' is not declared in this class scope.");

        binding = resolvedBinding;
        return true;
    }

    private void EmitPrivateFieldInitializer(in PrivateFieldInitPlan initPlan)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var objReg = AllocateTemporaryRegisterBlock(2);
            var valueReg = objReg + 1;
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStarRegister(objReg);
            if (initPlan.Initializer is not null)
                VisitExpressionWithInferredName(initPlan.Initializer, initPlan.SourceName);
            else
                EmitLdaUndefined();
            EmitStarRegister(valueReg);
            EmitPrivateFieldOp(JsOpCode.InitPrivateField, objReg, valueReg, initPlan.Binding.BrandId,
                initPlan.Binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrivateFieldInitializerOnTarget(int targetReg, in PrivateFieldBinding binding,
        JsExpression? initializer, string? sourceName = null)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var valueReg = AllocateTemporaryRegister();
            if (initializer is not null)
                VisitExpressionWithInferredName(initializer, sourceName);
            else
                EmitLdaUndefined();
            EmitStarRegister(valueReg);
            EmitPrivateFieldOp(JsOpCode.InitPrivateField, targetReg, valueReg, binding.BrandId, binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrivateAccessorInitializer(in PrivateAccessorInitPlan initPlan)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var objReg = AllocateTemporaryRegisterBlock(3);
            var getterReg = objReg + 1;
            var setterReg = objReg + 2;
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStarRegister(objReg);

            if (initPlan.Getter is not null)
            {
                var getterParameterPlan = FunctionParameterPlan.FromFunction(initPlan.Getter);
                var getterObj = CompileFunctionObject(
                    $"get {initPlan.SourceName}",
                    getterParameterPlan,
                    initPlan.Getter.Body,
                    CreateFunctionShape(
                        initPlan.Getter.IsGenerator,
                        initPlan.Getter.IsAsync,
                        initPlan.Getter.IsArrow,
                        true),
                    sourceStartPosition: initPlan.Getter.Position,
                    useMethodEnvironmentCapture: true,
                    classPrivateNameToBinding: classPrivateNameToBinding);
                if (getterObj.RequiresClosureBinding)
                    requiresClosureBinding = true;
                var currentFunctionReg = AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaCurrentFunction);
                EmitStarRegister(currentFunctionReg);
                EmitCreateClosureForMethodWithEnvironment(getterObj, objReg, privateBrandSourceReg: currentFunctionReg);
            }
            else
            {
                EmitLdaUndefined();
            }

            EmitStarRegister(getterReg);

            if (initPlan.Setter is not null)
            {
                var setterParameterPlan = FunctionParameterPlan.FromFunction(initPlan.Setter);
                var setterObj = CompileFunctionObject(
                    $"set {initPlan.SourceName}",
                    setterParameterPlan,
                    initPlan.Setter.Body,
                    CreateFunctionShape(
                        initPlan.Setter.IsGenerator,
                        initPlan.Setter.IsAsync,
                        initPlan.Setter.IsArrow,
                        true),
                    sourceStartPosition: initPlan.Setter.Position,
                    useMethodEnvironmentCapture: true,
                    classPrivateNameToBinding: classPrivateNameToBinding);
                if (setterObj.RequiresClosureBinding)
                    requiresClosureBinding = true;
                var currentFunctionReg = AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaCurrentFunction);
                EmitStarRegister(currentFunctionReg);
                EmitCreateClosureForMethodWithEnvironment(setterObj, objReg, privateBrandSourceReg: currentFunctionReg);
            }
            else
            {
                EmitLdaUndefined();
            }

            EmitStarRegister(setterReg);

            EmitPrivateAccessorInitOp(objReg, getterReg, setterReg, initPlan.Binding.BrandId,
                initPlan.Binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrivateMethodInitializer(in PrivateMethodInitPlan initPlan)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var objReg = AllocateTemporaryRegisterBlock(2);
            var methodReg = objReg + 1;
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStarRegister(objReg);
            EmitLoadCurrentFunctionPrivateMethodValue(initPlan.Binding.SlotIndex);
            EmitStarRegister(methodReg);

            EmitPrivateMethodInitOp(objReg, methodReg, initPlan.Binding.BrandId, initPlan.Binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrivateMethodInitializerOnTarget(
        int targetReg,
        string sourceName,
        in PrivateFieldBinding binding,
        JsFunctionExpression functionExpr,
        IReadOnlyList<int>? inheritedPrivateBrandIds = null,
        int inheritedPrivateBrandSourceReg = -1,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings = null)
    {
        var parameterPlan = FunctionParameterPlan.FromFunction(functionExpr);
        var methodObj = CompileFunctionObject(
            sourceName,
            parameterPlan,
            functionExpr.Body,
            CreateFunctionShape(
                functionExpr.IsGenerator,
                functionExpr.IsAsync,
                functionExpr.IsArrow,
                true),
            sourceStartPosition: functionExpr.Position,
            useMethodEnvironmentCapture: true,
            classPrivateNameToBinding: classPrivateNameToBinding);
        if (methodObj.RequiresClosureBinding)
            requiresClosureBinding = true;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            EmitCreateClosureForMethodWithEnvironment(methodObj, targetReg, privateBrandSourceReg: targetReg,
                inheritedPrivateBrandIds: inheritedPrivateBrandIds,
                inheritedPrivateBrandSourceReg: inheritedPrivateBrandSourceReg,
                explicitPrivateBrandMappings: explicitPrivateBrandMappings);
            var methodReg = AllocateTemporaryRegister();
            EmitStarRegister(methodReg);

            EmitPrivateMethodInitOp(targetReg, methodReg, binding.BrandId, binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrivateAccessorInitializerOnTarget(
        int targetReg,
        in PrivateFieldBinding binding,
        JsBytecodeFunction? getterObj,
        JsBytecodeFunction? setterObj,
        IReadOnlyList<int>? inheritedPrivateBrandIds = null,
        int inheritedPrivateBrandSourceReg = -1,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings = null)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var getterReg = AllocateTemporaryRegisterBlock(2);
            var setterReg = getterReg + 1;
            if (getterObj is not null)
                EmitCreateClosureForMethodWithEnvironment(getterObj, targetReg, privateBrandSourceReg: targetReg,
                    inheritedPrivateBrandIds: inheritedPrivateBrandIds,
                    inheritedPrivateBrandSourceReg: inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings: explicitPrivateBrandMappings);
            else
                EmitLdaUndefined();

            EmitStarRegister(getterReg);

            if (setterObj is not null)
                EmitCreateClosureForMethodWithEnvironment(setterObj, targetReg, privateBrandSourceReg: targetReg,
                    inheritedPrivateBrandIds: inheritedPrivateBrandIds,
                    inheritedPrivateBrandSourceReg: inheritedPrivateBrandSourceReg,
                    explicitPrivateBrandMappings: explicitPrivateBrandMappings);
            else
                EmitLdaUndefined();

            EmitStarRegister(setterReg);

            EmitPrivateAccessorInitOp(targetReg, getterReg, setterReg, binding.BrandId, binding.SlotIndex);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPublicFieldInitializer(in PublicFieldInitPlan initPlan)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var targetReg = AllocateTemporaryRegisterBlock(initPlan.ComputedKeyIndex >= 0 ? 3 : 2);
            var keyReg = initPlan.ComputedKeyIndex >= 0 ? targetReg + 1 : -1;
            var valueReg = initPlan.ComputedKeyIndex >= 0 ? targetReg + 2 : targetReg + 1;
            builder.EmitLda(JsOpCode.LdaThis);
            EmitStarRegister(targetReg);
            if (initPlan.Element.FieldInitializer is not null)
                VisitExpressionWithInferredName(initPlan.Element.FieldInitializer, initPlan.SourceName);
            else
                EmitLdaUndefined();
            EmitStarRegister(valueReg);
            if (initPlan.ComputedKeyIndex < 0)
            {
                EmitDefineClassField(targetReg, initPlan.Element, valueReg);
            }
            else
            {
                EmitLda(initPlan.ComputedKeyIndex);
                EmitStarRegister(keyReg);
                EmitCallRuntime(RuntimeId.LoadCurrentFunctionInstanceFieldKey, keyReg, 1);
                EmitStarRegister(keyReg);
                EmitCallRuntime(RuntimeId.DefineClassField, targetReg, 3);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private static bool ShouldAssignAnonymousFunctionName(JsExpression initializer)
    {
        return initializer switch
        {
            JsFunctionExpression { Name: null } => true,
            JsClassExpression { Name: null, Elements: var classElements } =>
                ShouldInferAnonymousClassName(classElements),
            _ => false
        };
    }

    private static JsExpression ApplyInferredNameIfNeeded(JsExpression expression, string? inferredName)
    {
        if (string.IsNullOrEmpty(inferredName) || !ShouldAssignAnonymousFunctionName(expression))
            return expression;

        return expression switch
        {
            JsFunctionExpression
            {
                Name: null,
                Parameters: var parameters,
                Body: var body,
                IsGenerator: var isGenerator,
                IsAsync: var isAsync,
                IsArrow: var isArrow,
                ParameterInitializers: var parameterInitializers,
                ParameterPatterns: var parameterPatterns,
                ParameterPositions: var parameterPositions,
                ParameterBindingKinds: var parameterBindingKinds,
                FunctionLength: var functionLength,
                HasSimpleParameterList: var hasSimpleParameterList,
                HasSuperBindingHint: var hasSuperBindingHint,
                HasDuplicateParameters: var hasDuplicateParameters,
                RestParameterIndex: var restParameterIndex,
                ParameterIds: var parameterIds
            } => new JsFunctionExpression(
                inferredName,
                parameters,
                body,
                isGenerator,
                isAsync,
                isArrow,
                parameterInitializers,
                parameterPatterns,
                parameterPositions,
                parameterBindingKinds,
                functionLength,
                hasSimpleParameterList,
                hasSuperBindingHint,
                hasDuplicateParameters,
                restParameterIndex,
                -1,
                parameterIds),
            JsClassExpression { Name: null } classExpr when ShouldInferAnonymousClassName(classExpr.Elements) => new
                JsClassExpression(
                    inferredName,
                    classExpr.Elements,
                    classExpr.Decorators,
                    classExpr.HasExtends,
                    classExpr.ExtendsExpression,
                    classExpr.NameId)
                {
                    Position = classExpr.Position,
                    EndPosition = classExpr.EndPosition
                },
            _ => expression
        };
    }

    private bool HasPendingInstanceInitializers()
    {
        return pendingPrivateFieldInitializers is { Count: > 0 } ||
               pendingPrivateAccessorInitializers is { Count: > 0 } ||
               pendingPrivateMethodInitializers is { Count: > 0 } ||
               pendingInstanceFieldInitializers is { Count: > 0 } ||
               pendingPublicFieldInitializers is { Count: > 0 };
    }

    private void EmitPendingInstanceInitializers()
    {
        if (pendingPrivateMethodInitializers is { Count: > 0 })
            for (var i = 0; i < pendingPrivateMethodInitializers.Count; i++)
                EmitPrivateMethodInitializer(pendingPrivateMethodInitializers[i]);

        if (pendingInstanceFieldInitializers is { Count: > 0 })
        {
            for (var i = 0; i < pendingInstanceFieldInitializers.Count; i++)
            {
                var initializer = pendingInstanceFieldInitializers[i];
                if (initializer.Kind == InstanceFieldInitializerKind.PrivateField)
                    EmitPrivateFieldInitializer(initializer.PrivateField);
                else
                    EmitPublicFieldInitializer(initializer.PublicField);
            }
        }
        else
        {
            if (pendingPrivateFieldInitializers is { Count: > 0 })
                for (var i = 0; i < pendingPrivateFieldInitializers.Count; i++)
                    EmitPrivateFieldInitializer(pendingPrivateFieldInitializers[i]);

            if (pendingPublicFieldInitializers is { Count: > 0 })
                for (var i = 0; i < pendingPublicFieldInitializers.Count; i++)
                    EmitPublicFieldInitializer(pendingPublicFieldInitializers[i]);
        }

        if (pendingPrivateAccessorInitializers is { Count: > 0 })
            for (var i = 0; i < pendingPrivateAccessorInitializers.Count; i++)
                EmitPrivateAccessorInitializer(pendingPrivateAccessorInitializers[i]);
    }


    private static long PackPrivateBrandSlotKey(int brandId, int slotIndex)
    {
        return ((long)brandId << 32) | (uint)slotIndex;
    }


    private void RegisterPrivateFieldDebugNamesForFunction(
        IReadOnlyDictionary<string, PrivateFieldBinding>? classPrivateNameToBinding)
    {
        if (classPrivateNameToBinding is null || classPrivateNameToBinding.Count == 0)
            return;

        foreach (var entry in classPrivateNameToBinding)
        {
            var binding = entry.Value;
            builder.AddPrivateFieldDebugName(PackPrivateBrandSlotKey(binding.BrandId, binding.SlotIndex), entry.Key);
        }
    }

    private void EmitParameterInitializerAndPatternPrologue()
    {
        var parameterPlan = currentFunctionParameterPlan;
        if (parameterPlan is null)
            return;
        if (parameterPlan.Bindings.Count == 0)
            return;

        KeyValuePair<int, int>[]? hiddenLocals = null;
        var hiddenLocalCount = 0;
        var prevEmitting = emittingParameterInitializers;
        ClearInitializedParameterBindings();
        if (functionHasParameterExpressions)
        {
            emittingParameterInitializers = true;
            hiddenLocalCount = HideNonParameterLocalsForInitializerScope(out hiddenLocals);
        }

        try
        {
            for (var i = 0; i < parameterPlan.Bindings.Count; i++)
            {
                var binding = parameterPlan.Bindings[i];
                var parameterName = binding.Name;
                _ = TryResolveLocalBinding(new CompilerIdentifierName(parameterName, binding.NameId),
                    out var resolvedParameter);
                var initializer = binding.Initializer;
                if (initializer is null)
                {
                    MarkInitializedParameterBinding(resolvedParameter.SymbolId);
                    continue;
                }

                if (!TryGetLocalRegister(resolvedParameter.SymbolId, out var parameterReg))
                    parameterReg = GetOrCreateLocal(new CompilerIdentifierName(parameterName, binding.NameId));

                var doneLabel = builder.CreateLabel();
                EmitLdaRegister(parameterReg);
                builder.EmitJump(JsOpCode.JumpIfNotUndefined, doneLabel);
                VisitExpressionWithInferredName(initializer, parameterName);
                StoreIdentifier(resolvedParameter.Name, true, parameterName);
                builder.BindLabel(doneLabel);
                MarkInitializedParameterBinding(resolvedParameter.SymbolId);
            }

            for (var i = 0; i < parameterPlan.Bindings.Count; i++)
            {
                var binding = parameterPlan.Bindings[i];
                if (binding.Pattern is null)
                    continue;

                VisitExpression(new JsAssignmentExpression(
                    JsAssignmentOperator.Assign,
                    binding.Pattern,
                    new JsIdentifierExpression(binding.Name, binding.NameId)));
                for (var j = 0; j < binding.BoundIdentifiers.Count; j++)
                {
                    var boundIdentifier = binding.BoundIdentifiers[j];
                    _ = TryResolveLocalBinding(new CompilerIdentifierName(boundIdentifier.Name, boundIdentifier.NameId),
                        out var resolvedBound);
                    MarkInitializedParameterBinding(resolvedBound.SymbolId);
                }
            }
        }
        finally
        {
            emittingParameterInitializers = prevEmitting;
            if (!emittingParameterInitializers)
                ClearInitializedParameterBindings();
            RestoreHiddenLocalsAfterInitializerScope(hiddenLocals, hiddenLocalCount);
        }
    }

    private void EmitLabeledStatement(JsLabeledStatement labeledStmt)
    {
        var labels = Vm.RentCompileList<string>(4);
        try
        {
            JsStatement target = labeledStmt;
            while (target is JsLabeledStatement nested)
            {
                labels.Add(nested.Label);
                target = nested.Statement;
            }

            switch (target)
            {
                case JsWhileStatement whileStmt:
                    EmitWhileStatement(whileStmt, labels);
                    return;
                case JsDoWhileStatement doWhileStmt:
                    EmitDoWhileStatement(doWhileStmt, labels);
                    return;
                case JsForStatement forStmt:
                    EmitForStatement(forStmt, labels);
                    return;
                case JsForInOfStatement forInOfStmt:
                    EmitForInOfStatement(forInOfStmt, labels);
                    return;
                case JsSwitchStatement switchStmt:
                    EmitSwitchStatement(switchStmt, labels);
                    return;
            }

            var breakLabel = builder.CreateLabel();
            PushLabeledTargets(labels, breakLabel, default, false);
            try
            {
                VisitStatement(target);
            }
            finally
            {
                PopLabeledTargets(labels.Count);
            }

            builder.BindLabel(breakLabel);
        }
        finally
        {
            labels.Clear();
            Vm.ReturnCompileList(labels);
        }
    }

    private void EmitWhileStatement(JsWhileStatement whileStmt, IReadOnlyList<string>? labels = null)
    {
        var perIterationSlots = TryCollectDirectLoopBodyPerIterationContextSlots(whileStmt.Body);
        var needsPerIterationContext = perIterationSlots is not null;
        if (needsPerIterationContext)
        {
            activePerIterationContextSlots.Push(perIterationSlots!);
            EmitPushClonedCurrentContext();
        }

        var loopLabel = builder.CreateLabel();
        var loopContinueLabel = builder.CreateLabel();
        var loopBreakLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var completionReg = AllocateLoopCompletionRegisterIfNeeded(whileStmt.Body, "$while");
        builder.BindLabel(loopLabel);
        VisitExpression(whileStmt.Test);
        EmitJumpIfToBooleanFalse(endLabel);
        loopTargets.Push(new(loopBreakLabel, loopContinueLabel));
        breakTargets.Push(loopBreakLabel);
        if (labels is not null && labels.Count != 0)
            PushLabeledTargets(labels, loopBreakLabel, loopContinueLabel, true);
        try
        {
            VisitLoopBodyWithCompletion(whileStmt.Body, completionReg);
        }
        finally
        {
            if (labels is not null && labels.Count != 0)
                PopLabeledTargets(labels.Count);
            breakTargets.Pop();
            loopTargets.Pop();
        }

        builder.BindLabel(loopContinueLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        if (needsPerIterationContext)
            EmitRotatePerIterationContext();
        EmitJump(loopLabel);
        builder.BindLabel(loopBreakLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        EmitJump(endLabel);
        builder.BindLabel(endLabel);
        if (needsPerIterationContext)
        {
            EmitRaw(JsOpCode.PopContext);
            Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
        }

        EmitLoadLoopCompletionValue(completionReg);
    }

    private void EmitDoWhileStatement(JsDoWhileStatement doWhileStmt, IReadOnlyList<string>? labels = null)
    {
        var perIterationSlots = TryCollectDirectLoopBodyPerIterationContextSlots(doWhileStmt.Body);
        var needsPerIterationContext = perIterationSlots is not null;
        if (needsPerIterationContext)
        {
            activePerIterationContextSlots.Push(perIterationSlots!);
            EmitPushClonedCurrentContext();
        }

        var loopLabel = builder.CreateLabel();
        var loopContinueLabel = builder.CreateLabel();
        var loopBreakLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var completionReg = AllocateLoopCompletionRegisterIfNeeded(doWhileStmt.Body, "$dowhile");
        builder.BindLabel(loopLabel);
        loopTargets.Push(new(loopBreakLabel, loopContinueLabel));
        breakTargets.Push(loopBreakLabel);
        if (labels is not null && labels.Count != 0)
            PushLabeledTargets(labels, loopBreakLabel, loopContinueLabel, true);
        try
        {
            VisitLoopBodyWithCompletion(doWhileStmt.Body, completionReg);
        }
        finally
        {
            if (labels is not null && labels.Count != 0)
                PopLabeledTargets(labels.Count);
            breakTargets.Pop();
            loopTargets.Pop();
        }

        builder.BindLabel(loopContinueLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        if (needsPerIterationContext)
            EmitRotatePerIterationContext();
        VisitExpression(doWhileStmt.Test);
        var testFalseLabel = builder.CreateLabel();
        builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, testFalseLabel);
        EmitJump(loopLabel);
        builder.BindLabel(loopBreakLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        EmitJump(endLabel);
        builder.BindLabel(testFalseLabel);
        builder.BindLabel(endLabel);
        if (needsPerIterationContext)
        {
            EmitRaw(JsOpCode.PopContext);
            Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
        }

        EmitLoadLoopCompletionValue(completionReg);
    }

    private void EmitForStatement(JsForStatement forStmt, IReadOnlyList<string>? labels = null)
    {
        PushForHeadLexicalAliases(forStmt);
        try
        {
            var completionReg = AllocateLoopCompletionRegisterIfNeeded(forStmt.Body, "$for");
            string? knownInitializedLoopLexical = null;
            HashSet<int>? perIterationSlots = null;
            if (forStmt.Init is JsVariableDeclarationStatement initDecl)
            {
                EmitSourcePosition(initDecl.Position);
                EmitVariableDeclarationStatement(initDecl, false);
                if (TryGetSafeSingleLexicalInitDeclarator(initDecl, out var resolvedInitBinding))
                    knownInitializedLoopLexical = resolvedInitBinding.Name;
                if (ShouldUsePerIterationContextForForLoop(initDecl))
                    perIterationSlots =
                        CollectPerIterationContextSlots(initDecl.Declarators.Select(static d => d.Name));
            }
            else if (forStmt.Init is JsExpression initExpr)
            {
                VisitExpression(initExpr, false);
            }

            if (knownInitializedLoopLexical is not null)
                MarkKnownInitializedLexical(knownInitializedLoopLexical);

            perIterationSlots = UnionPerIterationContextSlots(
                perIterationSlots,
                TryCollectDirectLoopBodyPerIterationContextSlots(forStmt.Body));
            var needsPerIterationContext = perIterationSlots is not null;
            if (needsPerIterationContext)
            {
                activePerIterationContextSlots.Push(perIterationSlots!);
                EmitPushClonedCurrentContext();
            }

            var loopLabel = builder.CreateLabel();
            var continueLabel = builder.CreateLabel();
            var loopBreakLabel = builder.CreateLabel();
            var endLabel = builder.CreateLabel();
            builder.BindLabel(loopLabel);

            if (forStmt.Test is not null)
            {
                VisitExpression(forStmt.Test);
                EmitJumpIfToBooleanFalse(endLabel);
            }

            loopTargets.Push(new(loopBreakLabel, continueLabel));
            breakTargets.Push(loopBreakLabel);
            if (labels is not null && labels.Count != 0)
                PushLabeledTargets(labels, loopBreakLabel, continueLabel, true);
            try
            {
                VisitLoopBodyWithCompletion(forStmt.Body, completionReg);
            }
            finally
            {
                if (labels is not null && labels.Count != 0)
                    PopLabeledTargets(labels.Count);
                breakTargets.Pop();
                loopTargets.Pop();
            }

            builder.BindLabel(continueLabel);
            EmitStoreLoopCompletionValueIfNeeded(completionReg);
            if (needsPerIterationContext)
                EmitRotatePerIterationContext();
            if (forStmt.Update is not null)
                VisitExpression(forStmt.Update, false);

            EmitJump(loopLabel);
            builder.BindLabel(loopBreakLabel);
            EmitStoreLoopCompletionValueIfNeeded(completionReg);
            if (ShouldEmitLoopBreakExitJump(completionReg, needsPerIterationContext))
                EmitJump(endLabel);
            builder.BindLabel(endLabel);
            if (needsPerIterationContext)
            {
                EmitRaw(JsOpCode.PopContext);
                Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
            }

            EmitLoadLoopCompletionValue(completionReg);

            if (knownInitializedLoopLexical is not null)
                UnmarkKnownInitializedLexical(knownInitializedLoopLexical);
        }
        finally
        {
            PopForHeadLexicalAliases(forStmt);
        }
    }

    private void EmitRestParameterPrologue()
    {
        var parameterPlan = currentFunctionParameterPlan;
        if (parameterPlan is null)
            return;
        if (!parameterPlan.TryGetRestBinding(out var restIndex, out var restBinding))
            return;

        if (requiresArgumentsObject)
        {
            var argsReg = EnsureSyntheticArgumentsRegister();
            var argStart = AllocateTemporaryRegisterBlock(2);
            EmitMoveRegister(argsReg, argStart);
            EmitLda(restIndex);
            EmitStarRegister(argStart + 1);
            EmitCallRuntime(RuntimeId.CreateRestParameterFromArrayLike, argStart, 2);
        }
        else
        {
            EmitRaw(JsOpCode.CreateRestParameter, (byte)restIndex);
        }

        StoreIdentifier(restBinding.Name);
    }


    private bool ShouldUsePerIterationContextForForLoop(JsVariableDeclarationStatement initDecl)
    {
        if (currentContextSlotById.Count == 0)
            return false;
        if (!initDecl.Kind.IsLexical())
            return false;

        foreach (var decl in initDecl.Declarators)
            if (TryResolveLocalBinding(decl.Name, out var resolved) &&
                IsCapturedByChildBinding(resolved.SymbolId))
                return true;

        return false;
    }

    private HashSet<int>? TryCollectDirectLoopBodyPerIterationContextSlots(JsStatement loopBody)
    {
        if (currentContextSlotById.Count == 0)
            return null;
        if (loopBody is not JsBlockStatement bodyBlock)
            return null;
        if (!nestedBlockLexicals.TryGetValue(bodyBlock.Position, out var bindings) || bindings.Count == 0)
            return null;

        HashSet<int>? slots = null;
        for (var i = 0; i < bindings.Count; i++)
        {
            var symbolId = bindings[i].InternalSymbolId;
            if (!IsCapturedByChildBinding(symbolId))
                continue;
            if (!TryGetCurrentContextSlot(symbolId, out _))
                continue;

            slots ??= Vm.RentCompileHashSet<int>(bindings.Count);
            _ = slots.Add(symbolId);
        }

        return slots;
    }

    private HashSet<int>? UnionPerIterationContextSlots(HashSet<int>? existing, HashSet<int>? additional)
    {
        if (existing is null)
            return additional;
        if (additional is null)
            return existing;

        foreach (var symbolId in additional)
            _ = existing.Add(symbolId);
        Vm.ReturnCompileHashSet(additional);
        return existing;
    }

    private HashSet<int> CollectPerIterationContextSlots(IEnumerable<string> sourceNames)
    {
        var slots = Vm.RentCompileHashSet<int>(4);
        foreach (var sourceName in sourceNames)
            if (TryResolveLocalBinding(sourceName, out var resolved) &&
                TryGetCurrentContextSlot(resolved.SymbolId, out _))
                _ = slots.Add(resolved.SymbolId);

        return slots;
    }

    private int GetActivePerIterationContextDepthForSymbol(int symbolId)
    {
        if (activePerIterationContextSlots.Count == 0)
            return 0;

        var depth = 0;
        foreach (var slots in activePerIterationContextSlots)
        {
            if (slots.Contains(symbolId))
                return depth;
            depth++;
        }

        if (currentContextPerIterationBaseDepthBySymbol.TryGetValue(symbolId, out var baseDepth))
            return Math.Max(0, depth - baseDepth);

        return depth;
    }

    private int GetActivePerIterationContextDepthForSymbol(string resolvedName)
    {
        return TryGetSymbolId(resolvedName, out var symbolId)
            ? GetActivePerIterationContextDepthForSymbol(symbolId)
            : 0;
    }

    private void RecordCurrentContextPerIterationBaseDepth(IReadOnlyList<BlockLexicalBinding> bindings)
    {
        if (bindings.Count == 0)
            return;

        var baseDepth = activePerIterationContextSlots.Count;
        for (var i = 0; i < bindings.Count; i++)
        {
            var symbolId = bindings[i].InternalSymbolId;
            if (currentContextPerIterationBaseDepthBySymbol.ContainsKey(symbolId))
                continue;
            if (!TryGetCurrentContextSlot(symbolId, out _))
                continue;
            currentContextPerIterationBaseDepthBySymbol[symbolId] = baseDepth;
        }
    }

    private void EmitPushClonedCurrentContext()
    {
        var slotCount = currentContextSlotById.Count;
        if (slotCount == 0)
            return;

        EmitCreateFunctionContextWithCells(slotCount);
        EmitRaw(JsOpCode.PushContextAcc);
        for (var slot = 0; slot < slotCount; slot++)
        {
            EmitLdaContextSlot(0, slot, 1, true);
            EmitStaCurrentContextSlot(slot);
        }
    }

    private void EmitRotatePerIterationContext()
    {
        var slotCount = currentContextSlotById.Count;
        if (slotCount == 0)
            return;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var copyStart = AllocateTemporaryRegisterBlock(slotCount);
            for (var slot = 0; slot < slotCount; slot++)
            {
                EmitLdaCurrentContextSlot(slot, true);
                EmitStarRegister(copyStart + slot);
            }

            EmitRaw(JsOpCode.PopContext);
            EmitCreateFunctionContextWithCells(slotCount);
            EmitRaw(JsOpCode.PushContextAcc);

            for (var slot = 0; slot < slotCount; slot++)
            {
                EmitLdaRegister(copyStart + slot);
                EmitStaCurrentContextSlot(slot);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void PushLabeledTargets(IReadOnlyList<string> labels, BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget, bool hasContinueTarget)
    {
        for (var i = 0; i < labels.Count; i++)
            labeledTargets.Push(new(labels[i], breakTarget, continueTarget, hasContinueTarget));
    }

    private void EmitSwitchStatement(JsSwitchStatement switchStmt, IReadOnlyList<string>? labels = null)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            VisitExpression(switchStmt.Discriminant);
            var discriminantReg = AllocateTemporaryRegister();
            EmitStarRegister(discriminantReg);

            PushSwitchLexicalAliases(switchStmt);
            try
            {
                var completionReg = AllocateTemporaryRegister();
                EmitLdaUndefined();
                EmitStarRegister(completionReg);

                var caseCount = switchStmt.Cases.Count;
                var caseLabels = Vm.RentCompileList<BytecodeBuilder.Label>(caseCount == 0 ? 1 : caseCount);
                var endLabel = builder.CreateLabel();
                var defaultLabel = endLabel;
                try
                {
                    for (var i = 0; i < caseCount; i++)
                        caseLabels.Add(builder.CreateLabel());

                    for (var i = 0; i < caseCount; i++)
                    {
                        var c = switchStmt.Cases[i];
                        if (c.Test is null)
                        {
                            defaultLabel = caseLabels[i];
                            continue;
                        }

                        VisitExpression(c.Test);
                        EmitTestEqualStrictRegister(discriminantReg);
                        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, caseLabels[i]);
                    }

                    EmitJump(defaultLabel);

                    breakTargets.Push(endLabel);
                    if (labels is not null && labels.Count != 0)
                        PushLabeledTargets(labels, endLabel, default, false);
                    PushStatementCompletionState(completionReg, false);
                    activeSwitchCompletionRegisters.Push(completionReg);
                    try
                    {
                        for (var i = 0; i < caseCount; i++)
                        {
                            builder.BindLabel(caseLabels[i]);
                            var c = switchStmt.Cases[i];
                            for (var j = 0; j < c.Consequent.Count; j++)
                                VisitStatement(c.Consequent[j]);
                        }
                    }
                    finally
                    {
                        activeSwitchCompletionRegisters.Pop();
                        PopStatementCompletionState();
                        if (labels is not null && labels.Count != 0)
                            PopLabeledTargets(labels.Count);
                        breakTargets.Pop();
                    }

                    builder.BindLabel(endLabel);
                    EmitLdaRegister(completionReg);
                }
                finally
                {
                    caseLabels.Clear();
                    Vm.ReturnCompileList(caseLabels);
                }
            }
            finally
            {
                PopSwitchLexicalAliases(switchStmt);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void PopLabeledTargets(int count)
    {
        for (var i = 0; i < count; i++)
            labeledTargets.Pop();
    }

    private bool TryResolveBreakTarget(string? label, out BytecodeBuilder.Label breakTarget)
    {
        if (label is null)
        {
            if (loopTargets.Count == 0)
            {
                breakTarget = default;
                return false;
            }

            breakTarget = loopTargets.Peek().BreakTarget;
            return true;
        }

        foreach (var targets in labeledTargets)
            if (string.Equals(targets.Label, label, StringComparison.Ordinal))
            {
                breakTarget = targets.BreakTarget;
                return true;
            }

        breakTarget = default;
        return false;
    }

    private bool TryResolveContinueTarget(string? label, out BytecodeBuilder.Label continueTarget,
        out ContinueLabelError error)
    {
        if (label is null)
        {
            if (loopTargets.Count == 0)
            {
                continueTarget = default;
                error = ContinueLabelError.UndefinedLabel;
                return false;
            }

            continueTarget = loopTargets.Peek().ContinueTarget;
            error = ContinueLabelError.None;
            return true;
        }

        foreach (var targets in labeledTargets)
        {
            if (!string.Equals(targets.Label, label, StringComparison.Ordinal))
                continue;

            if (!targets.HasContinueTarget)
            {
                continueTarget = default;
                error = ContinueLabelError.NotIterationLabel;
                return false;
            }

            continueTarget = targets.ContinueTarget;
            error = ContinueLabelError.None;
            return true;
        }

        continueTarget = default;
        error = ContinueLabelError.UndefinedLabel;
        return false;
    }

    private void ThrowUndefinedLabelSyntaxError(string label, int position)
    {
        throw new JsParseException($"Undefined label '{label}'", position, CurrentSourceText);
    }

    private void ThrowIllegalBreakSyntaxError(int position)
    {
        throw new JsParseException("Illegal break statement", position, CurrentSourceText);
    }

    private void ThrowIllegalContinueNoLoopSyntaxError(int position)
    {
        throw new JsParseException("Illegal continue statement: no surrounding iteration statement", position,
            CurrentSourceText);
    }

    private void ThrowIllegalContinueLabelSyntaxError(string label, int position)
    {
        throw new JsParseException(
            $"Illegal continue statement: '{label}' does not denote an iteration statement",
            position, CurrentSourceText);
    }


    private void EmitTryStatement(JsTryStatement tryStmt)
    {
        if (tryStmt.Handler is null && tryStmt.Finalizer is null)
            throw new NotSupportedException("try without catch/finally is not supported.");

        if (tryStmt.Finalizer is null)
        {
            EmitTryCatchWithoutFinally(tryStmt);
            return;
        }

        EmitTryFinallyWithOptionalCatch(tryStmt);
    }

    private void EmitTryCatchWithoutFinally(JsTryStatement tryStmt)
    {
        var catchLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var normalResultReg = AllocateTemporaryRegister();

        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        activeCatchableTryDepth++;
        try
        {
            EmitLdaUndefined();
            activeAbruptEmptyNormalizations.Push(true);
            VisitStatement(tryStmt.Block);
            activeAbruptEmptyNormalizations.Pop();
            EmitNormalizeAccumulatorHoleToUndefined();
            EmitStarRegister(normalResultReg);
        }
        finally
        {
            activeCatchableTryDepth--;
        }

        EmitRaw(JsOpCode.PopTry);
        EmitLdaRegister(normalResultReg);
        builder.EmitJump(JsOpCode.Jump, endLabel);
        builder.BindLabel(catchLabel);

        if (tryStmt.Handler is null)
            throw new NotSupportedException("try without catch/finally is not supported.");

        activeAbruptEmptyNormalizations.Push(true);
        EmitCatchBody(tryStmt.Handler);
        activeAbruptEmptyNormalizations.Pop();
        EmitNormalizeAccumulatorHoleToUndefined();
        builder.BindLabel(endLabel);
    }

    private void EmitFinallyFlowScope(Action emitTryBody, Action<int, int> emitFinalizerBody)
    {
        var catchLabel = builder.CreateLabel();
        var finallyFromTryLabel = builder.CreateLabel();
        var finallyEntryLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var returnLabel = builder.CreateLabel();
        var throwLabel = builder.CreateLabel();

        var completionKindReg = AllocateSyntheticLocal($"$finally.kind.{finallyTempUniqueId}");
        var completionValueReg = AllocateSyntheticLocal($"$finally.value.{finallyTempUniqueId}");
        var routeMap = new FinallyJumpRouteMap();
        finallyTempUniqueId++;

        EmitLdaZero();
        EmitStarRegister(completionKindReg);
        EmitLdaTheHole();
        EmitStarRegister(completionValueReg);

        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        activeFinallyFlow.Push(new(completionKindReg, completionValueReg, finallyFromTryLabel, true, routeMap));
        try
        {
            activeAbruptEmptyNormalizations.Push(true);
            try
            {
                emitTryBody();
            }
            finally
            {
                activeAbruptEmptyNormalizations.Pop();
            }

            EmitStarRegister(completionValueReg);
        }
        finally
        {
            activeFinallyFlow.Pop();
        }

        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyFromTryLabel);
        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(catchLabel);
        EmitStarRegister(completionValueReg);
        EmitLda(2);
        EmitStarRegister(completionKindReg);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyEntryLabel);
        var finalizerCompletionReg = AllocateTemporaryRegister();
        EmitLdaTheHole();
        EmitStarRegister(finalizerCompletionReg);
        PushStatementCompletionState(finalizerCompletionReg, false);
        try
        {
            activeAbruptEmptyNormalizations.Push(true);
            try
            {
                emitFinalizerBody(completionKindReg, completionValueReg);
            }
            finally
            {
                activeAbruptEmptyNormalizations.Pop();
            }
        }
        finally
        {
            PopStatementCompletionState();
            ReleaseTemporaryRegister(finalizerCompletionReg);
        }

        var kindCompareReg = AllocateTemporaryRegister();
        var routeCompareReg = -1;
        try
        {
            void EmitCompletionKindJump(int kind, BytecodeBuilder.Label target, BytecodeBuilder.Label fallthrough)
            {
                EmitLda(kind);
                EmitStarRegister(kindCompareReg);
                EmitLdaRegister(completionKindReg);
                EmitTestEqualStrictRegister(kindCompareReg);
                EmitJumpIfToBooleanFalse(fallthrough);
                EmitJump(target);
            }

            var notReturnLabel = builder.CreateLabel();
            EmitCompletionKindJump(1, returnLabel, notReturnLabel);
            builder.BindLabel(notReturnLabel);

            var notThrowLabel = builder.CreateLabel();
            EmitCompletionKindJump(2, throwLabel, notThrowLabel);
            builder.BindLabel(notThrowLabel);

            BytecodeBuilder.Label breakLabel = default;
            BytecodeBuilder.Label continueLabel = default;
            if (routeMap.HasBreakRoutes)
            {
                var notBreakLabel = builder.CreateLabel();
                breakLabel = builder.CreateLabel();
                EmitCompletionKindJump(3, breakLabel, notBreakLabel);
                builder.BindLabel(notBreakLabel);
            }

            if (routeMap.HasContinueRoutes)
            {
                var notContinueLabel = builder.CreateLabel();
                continueLabel = builder.CreateLabel();
                EmitCompletionKindJump(4, continueLabel, notContinueLabel);
                builder.BindLabel(notContinueLabel);
            }

            EmitLoadRegisterOrUndefinedIfHole(completionValueReg);
            EmitJump(endLabel);

            builder.BindLabel(returnLabel);
            EmitLdaRegister(completionValueReg);
            EmitReturnConsideringFinallyFlow();

            builder.BindLabel(throwLabel);
            EmitLdaRegister(completionValueReg);
            EmitThrowConsideringFinallyFlow();

            if (breakLabel.IsInitialized || continueLabel.IsInitialized)
                routeCompareReg = AllocateTemporaryRegister();

            if (breakLabel.IsInitialized)
            {
                var noBreakRouteMatchedLabel = builder.CreateLabel();
                builder.BindLabel(breakLabel);
                EmitFinallyRouteDispatch(routeMap, false, completionValueReg, routeCompareReg,
                    noBreakRouteMatchedLabel);
                builder.BindLabel(noBreakRouteMatchedLabel);
                EmitJump(endLabel);
            }

            if (continueLabel.IsInitialized)
            {
                builder.BindLabel(continueLabel);
                EmitFinallyRouteDispatch(routeMap, true, completionValueReg, routeCompareReg, endLabel);
            }
        }
        finally
        {
            if (routeCompareReg >= 0)
                ReleaseTemporaryRegister(routeCompareReg);
            ReleaseTemporaryRegister(kindCompareReg);
        }

        builder.BindLabel(endLabel);
    }

    private void EmitTryFinallyWithOptionalCatch(JsTryStatement tryStmt)
    {
        if (tryStmt.Handler is null)
        {
            EmitFinallyFlowScope(
                () => VisitStatement(tryStmt.Block),
                (_, _) => VisitStatement(tryStmt.Finalizer!));
            return;
        }

        var catchLabel = builder.CreateLabel();
        var finallyFromTryLabel = builder.CreateLabel();
        var finallyFromCatchLabel = builder.CreateLabel();
        var finallyEntryLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var returnLabel = builder.CreateLabel();
        var throwLabel = builder.CreateLabel();
        var notReturnLabel = builder.CreateLabel();
        var notThrowLabel = builder.CreateLabel();

        var completionKindReg = AllocateSyntheticLocal($"$finally.kind.{finallyTempUniqueId}");
        var completionValueReg = AllocateSyntheticLocal($"$finally.value.{finallyTempUniqueId}");
        var kindCompareReg = AllocateSyntheticLocal($"$finally.kindcmp.{finallyTempUniqueId}");
        var routeCompareReg = AllocateSyntheticLocal($"$finally.routecmp.{finallyTempUniqueId}");
        var routeMap = new FinallyJumpRouteMap();
        finallyTempUniqueId++;

        // completion = normal, value = empty
        EmitLdaZero();
        EmitStarRegister(completionKindReg);
        EmitLdaTheHole();
        EmitStarRegister(completionValueReg);

        // try { ... }
        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        activeFinallyFlow.Push(new(
            completionKindReg,
            completionValueReg,
            finallyFromTryLabel,
            tryStmt.Handler is null,
            routeMap));
        try
        {
            if (tryStmt.Handler is not null)
                activeCatchableTryDepth++;
            try
            {
                activeAbruptEmptyNormalizations.Push(true);
                VisitStatement(tryStmt.Block);
                activeAbruptEmptyNormalizations.Pop();
                EmitStarRegister(completionValueReg);
            }
            finally
            {
                if (tryStmt.Handler is not null)
                    activeCatchableTryDepth--;
            }
        }
        finally
        {
            activeFinallyFlow.Pop();
        }

        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyFromTryLabel);
        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        // catch path (either user catch, or synthetic catch for try/finally).
        builder.BindLabel(catchLabel);
        if (tryStmt.Handler is not null)
        {
            var catchThrowLabel = builder.CreateLabel();

            builder.EmitJump(JsOpCode.PushTry, catchThrowLabel);
            activeFinallyFlow.Push(new(
                completionKindReg,
                completionValueReg,
                finallyFromCatchLabel,
                true,
                routeMap));
            try
            {
                activeAbruptEmptyNormalizations.Push(true);
                EmitCatchBody(tryStmt.Handler);
                activeAbruptEmptyNormalizations.Pop();
                EmitStarRegister(completionValueReg);
            }
            finally
            {
                activeFinallyFlow.Pop();
            }

            EmitRaw(JsOpCode.PopTry);
            EmitJump(finallyEntryLabel);

            builder.BindLabel(finallyFromCatchLabel);
            EmitRaw(JsOpCode.PopTry);
            EmitJump(finallyEntryLabel);

            builder.BindLabel(catchThrowLabel);
        }

        {
            // exception from try (runtime throw path): preserve thrown value
            EmitStarRegister(completionValueReg);
            EmitLda(2);
            EmitStarRegister(completionKindReg);
            EmitJump(finallyEntryLabel);
        }

        // finally { ... }
        builder.BindLabel(finallyEntryLabel);
        var finalizerCompletionReg = AllocateTemporaryRegister();
        EmitLdaTheHole();
        EmitStarRegister(finalizerCompletionReg);
        PushStatementCompletionState(finalizerCompletionReg, false);
        try
        {
            activeAbruptEmptyNormalizations.Push(true);
            VisitStatement(tryStmt.Finalizer!);
            activeAbruptEmptyNormalizations.Pop();
        }
        finally
        {
            PopStatementCompletionState();
            ReleaseTemporaryRegister(finalizerCompletionReg);
        }

        // completion dispatch after finally
        EmitLda(1);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        EmitJumpIfToBooleanFalse(notReturnLabel);
        EmitJump(returnLabel);

        builder.BindLabel(notReturnLabel);
        EmitLda(2);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        EmitJumpIfToBooleanFalse(notThrowLabel);
        EmitJump(throwLabel);
        builder.BindLabel(notThrowLabel);

        BytecodeBuilder.Label breakLabel = default;
        BytecodeBuilder.Label continueLabel = default;
        if (routeMap.HasBreakRoutes)
        {
            var notBreakLabel = builder.CreateLabel();
            breakLabel = builder.CreateLabel();
            EmitLda(3);
            EmitStarRegister(kindCompareReg);
            EmitLdaRegister(completionKindReg);
            EmitTestEqualStrictRegister(kindCompareReg);
            EmitJumpIfToBooleanFalse(notBreakLabel);
            EmitJump(breakLabel);
            builder.BindLabel(notBreakLabel);
        }

        if (routeMap.HasContinueRoutes)
        {
            var notContinueLabel = builder.CreateLabel();
            continueLabel = builder.CreateLabel();
            EmitLda(4);
            EmitStarRegister(kindCompareReg);
            EmitLdaRegister(completionKindReg);
            EmitTestEqualStrictRegister(kindCompareReg);
            EmitJumpIfToBooleanFalse(notContinueLabel);
            EmitJump(continueLabel);
            builder.BindLabel(notContinueLabel);
        }

        EmitLoadRegisterOrUndefinedIfHole(completionValueReg);
        EmitJump(endLabel);

        builder.BindLabel(returnLabel);
        EmitLdaRegister(completionValueReg);
        EmitReturnConsideringFinallyFlow();

        builder.BindLabel(throwLabel);
        EmitLdaRegister(completionValueReg);
        EmitThrowConsideringFinallyFlow();

        if (breakLabel.IsInitialized)
        {
            var noBreakRouteMatchedLabel = builder.CreateLabel();
            builder.BindLabel(breakLabel);
            EmitFinallyRouteDispatch(routeMap, false, completionValueReg, routeCompareReg,
                noBreakRouteMatchedLabel);
            builder.BindLabel(noBreakRouteMatchedLabel);
            EmitJump(endLabel);
        }

        if (continueLabel.IsInitialized)
        {
            builder.BindLabel(continueLabel);
            EmitFinallyRouteDispatch(routeMap, true, completionValueReg, routeCompareReg, endLabel);
        }

        builder.BindLabel(endLabel);
    }

    private void EmitCatchBody(JsCatchClause catchClause)
    {
        var thrownValueReg = AllocateTemporaryRegister();
        EmitStarRegister(thrownValueReg);
        var catchBindings = PushCatchBindingAliases(catchClause);

        try
        {
            if (catchClause.BindingPattern is not null)
            {
                if (!string.IsNullOrEmpty(catchClause.ParamName) && catchBindings is not null)
                {
                    var catchBinding = catchBindings[0];
                    EmitLdaRegister(thrownValueReg);
                    StoreIdentifier(catchBinding.InternalName, true,
                        catchBinding.SourceName);
                }
                else
                {
                    EmitLdaRegister(thrownValueReg);
                    switch (catchClause.BindingPattern)
                    {
                        case JsArrayExpression arrayPattern:
                            EmitArrayDestructuringAssignmentFromRegister(arrayPattern, thrownValueReg,
                                initializeIdentifiers: true);
                            break;
                        case JsObjectExpression objectPattern:
                            EmitObjectDestructuringAssignmentFromRegister(objectPattern, thrownValueReg,
                                initializeIdentifiers: true);
                            break;
                        default:
                            throw new NotSupportedException("Catch binding pattern is not supported.");
                    }
                }
            }
            else if (catchBindings is not null)
            {
                var catchBinding = catchBindings[0];
                EmitLdaRegister(thrownValueReg);
                StoreIdentifier(catchBinding.InternalName, true,
                    catchBinding.SourceName);
            }

            EmitLdaUndefined();
            VisitStatement(catchClause.Body);
        }
        finally
        {
            ReleaseTemporaryRegister(thrownValueReg);
            if (catchBindings is not null)
            {
                for (var i = 0; i < catchBindings.Count; i++)
                {
                    var catchBinding = catchBindings[i];
                    if (ShouldTrackKnownInitializedLexical(catchBinding.InternalName))
                        UnmarkKnownInitializedLexical(catchBinding.InternalName);
                }

                PopAliasScope();
            }
        }
    }

    private void EmitReturnConsideringFinallyFlow()
    {
        var preservedValueReg = -1;
        if (activeForAwaitLoops.Count != 0)
        {
            preservedValueReg = AllocateTemporaryRegister();
            EmitStarRegister(preservedValueReg);
            MarkAllActiveForAwaitLoopsForClose();
            EmitLdaRegister(preservedValueReg);
        }

        if (activeForOfIteratorLoops.Count != 0)
            EmitPreserveAccumulator(() => EmitCloseAllActiveForOfIteratorLoops(true));

        if (activeFinallyFlow.Count == 0)
        {
            EmitRaw(JsOpCode.Return);
            return;
        }

        var flow = activeFinallyFlow.Peek();
        EmitStarRegister(flow.ValueRegister);
        EmitLda(1);
        EmitStarRegister(flow.KindRegister);
        EmitJump(flow.TargetLabel);
    }

    private void EmitBreakConsideringFinallyFlow(BytecodeBuilder.Label breakTarget)
    {
        MarkForAwaitLoopsForBreakTarget(breakTarget);
        if (activeForOfIteratorLoops.Count != 0)
            EmitPreserveAccumulator(() => EmitCloseForOfIteratorLoopsForBreakTarget(breakTarget));

        if (activeFinallyFlow.Count == 0)
        {
            EmitJump(breakTarget);
            return;
        }

        var flow = activeFinallyFlow.Peek();
        var routeId = flow.Routes.GetOrAddRouteId(breakTarget, false);
        EmitLda((byte)routeId);
        EmitStarRegister(flow.ValueRegister);
        EmitLda(3);
        EmitStarRegister(flow.KindRegister);
        EmitJump(flow.TargetLabel);
    }

    private void EmitContinueConsideringFinallyFlow(BytecodeBuilder.Label continueTarget)
    {
        MarkForAwaitLoopsForContinueTarget(continueTarget);
        if (activeForOfIteratorLoops.Count != 0)
            EmitPreserveAccumulator(() => EmitCloseForOfIteratorLoopsForContinueTarget(continueTarget));

        if (activeFinallyFlow.Count == 0)
        {
            EmitJump(continueTarget);
            return;
        }

        var flow = activeFinallyFlow.Peek();
        var routeId = flow.Routes.GetOrAddRouteId(continueTarget, true);
        EmitLda((byte)routeId);
        EmitStarRegister(flow.ValueRegister);
        EmitLda(4);
        EmitStarRegister(flow.KindRegister);
        EmitJump(flow.TargetLabel);
    }

    private void EmitThrowConsideringFinallyFlow()
    {
        var preservedValueReg = -1;
        if (activeForAwaitLoops.Count != 0)
        {
            preservedValueReg = AllocateTemporaryRegister();
            EmitStarRegister(preservedValueReg);
            MarkAllActiveForAwaitLoopsForClose();
            EmitLdaRegister(preservedValueReg);
        }

        var throwHandledInsideActiveForOfLoop = activeForOfIteratorLoops.Count != 0 &&
                                                activeCatchableTryDepth > activeForOfIteratorLoops.Peek()
                                                    .CatchableTryDepthAtEntry;
        var throwLocallyCatchable = activeCatchableTryDepth != 0;

        if (!throwHandledInsideActiveForOfLoop && activeForOfIteratorLoops.Count != 0)
            EmitPreserveAccumulator(() => EmitCloseAllActiveForOfIteratorLoops(false));

        if (activeFinallyFlow.Count == 0 || throwLocallyCatchable)
        {
            EmitRaw(JsOpCode.Throw);
            return;
        }

        var flow = activeFinallyFlow.Peek();
        if (!flow.InterceptThrow)
        {
            EmitRaw(JsOpCode.Throw);
            return;
        }

        EmitStarRegister(flow.ValueRegister);
        EmitLda(2);
        EmitStarRegister(flow.KindRegister);
        EmitJump(flow.TargetLabel);
    }

    private void EmitFinallyRouteDispatch(
        FinallyJumpRouteMap routeMap,
        bool isContinue,
        int completionValueReg,
        int routeCompareReg,
        BytecodeBuilder.Label fallthroughLabel)
    {
        var routes = routeMap.Routes.Where(r => r.IsContinue == isContinue).ToList();
        for (var i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            var nextRouteLabel = builder.CreateLabel();

            EmitLda((byte)route.RouteId);
            EmitStarRegister(routeCompareReg);
            EmitLdaRegister(completionValueReg);
            EmitTestEqualStrictRegister(routeCompareReg);
            EmitJumpIfToBooleanFalse(nextRouteLabel);
            if (isContinue)
                EmitContinueConsideringFinallyFlow(route.Target);
            else
                EmitBreakConsideringFinallyFlow(route.Target);
            builder.BindLabel(nextRouteLabel);
        }

        EmitJump(fallthroughLabel);
    }

    private void MarkAllActiveForAwaitLoopsForClose()
    {
        if (activeForAwaitLoops.Count == 0)
            return;

        foreach (var context in activeForAwaitLoops)
            EmitSetBooleanRegister(context.CloseRequestedRegister, true);
    }

    private void MarkForAwaitLoopsForBreakTarget(BytecodeBuilder.Label breakTarget)
    {
        if (activeForAwaitLoops.Count == 0)
            return;

        foreach (var context in activeForAwaitLoops)
            if (context.BreakTarget == breakTarget)
            {
                EmitSetBooleanRegister(context.CloseRequestedRegister, true);
                break;
            }
    }

    private void MarkForAwaitLoopsForContinueTarget(BytecodeBuilder.Label continueTarget)
    {
        if (activeForAwaitLoops.Count == 0)
            return;

        foreach (var context in activeForAwaitLoops)
        {
            if (context.ContinueTarget == continueTarget)
                break;

            EmitSetBooleanRegister(context.CloseRequestedRegister, true);
        }
    }

    private void EmitSetBooleanRegister(int register, bool value)
    {
        EmitLda(value);
        EmitStarRegister(register);
    }

    private void EmitPreserveAccumulator(Action emitter)
    {
        var preservedReg = AllocateTemporaryRegister();
        try
        {
            EmitStarRegister(preservedReg);
            emitter();
            EmitLdaRegister(preservedReg);
        }
        finally
        {
            ReleaseTemporaryRegister(preservedReg);
        }
    }

    private void EmitCloseAllActiveForOfIteratorLoops(bool normalClose)
    {
        foreach (var context in activeForOfIteratorLoops)
            EmitCloseForOfIteratorLoop(context.IteratorRegister, normalClose);
    }

    private void EmitCloseForOfIteratorLoopsForBreakTarget(BytecodeBuilder.Label breakTarget)
    {
        foreach (var context in activeForOfIteratorLoops)
        {
            EmitCloseForOfIteratorLoop(context.IteratorRegister, true);
            if (context.BreakTarget == breakTarget)
                break;
        }
    }

    private void EmitCloseForOfIteratorLoopsForContinueTarget(BytecodeBuilder.Label continueTarget)
    {
        foreach (var context in activeForOfIteratorLoops)
        {
            if (context.ContinueTarget == continueTarget)
                break;

            EmitCloseForOfIteratorLoop(context.IteratorRegister, true);
        }
    }

    private void EmitCloseForOfIteratorLoop(int iteratorRegister, bool normalClose)
    {
        EmitCallRuntime(
            normalClose
                ? RuntimeId.DestructureIteratorClose
                : RuntimeId.DestructureIteratorCloseBestEffort,
            iteratorRegister,
            1);
    }

    private void EmitJump(BytecodeBuilder.Label target)
    {
        builder.EmitJump(JsOpCode.Jump, target);
    }

    private void EmitJumpIfToBooleanTrue(BytecodeBuilder.Label target)
    {
        builder.EmitJumpIfTruethy(JsOpCode.JumpIfToBooleanTrue, target);
    }

    private void EmitJumpIfToBooleanFalse(BytecodeBuilder.Label target)
    {
        builder.EmitJumpIfFalsy(JsOpCode.JumpIfToBooleanFalse, target);
    }

    private void EmitStoreCompletionValueIfNotHole(int completionReg)
    {
        var valueReg = AllocateTemporaryRegister();
        var skipLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitStarRegister(valueReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(valueReg);
        EmitJumpIfToBooleanTrue(skipLabel);
        EmitLdaRegister(valueReg);
        EmitStarRegister(completionReg);
        EmitJump(endLabel);
        builder.BindLabel(skipLabel);
        builder.BindLabel(endLabel);
        ReleaseTemporaryRegister(valueReg);
    }

    private void PushStatementCompletionState(int register, bool knownNonHole)
    {
        activeStatementCompletionStates.Push(new(register, knownNonHole));
    }

    private StatementCompletionState PopStatementCompletionState()
    {
        return activeStatementCompletionStates.Pop();
    }

    private static bool ShouldEmitLoopBreakExitJump(int completionReg, bool needsPerIterationContext)
    {
        return completionReg >= 0 || needsPerIterationContext;
    }

    private bool LoopCompletionIsObservable()
    {
        return activeStatementCompletionStates.Count != 0 || activeSwitchCompletionRegisters.Count != 0;
    }

    private bool LoopBodyNeedsCompletionTracking(JsStatement body)
    {
        if (!LoopCompletionIsObservable())
            return false;

        return StatementCanProduceTrackedCompletion(body) || StatementCanCompleteAbruptEmpty(body);
    }

    private int AllocateLoopCompletionRegisterIfNeeded(JsStatement body, string loopKind)
    {
        if (!LoopBodyNeedsCompletionTracking(body))
            return -1;

        var completionReg = AllocateSyntheticLocal($"{loopKind}.cpl.{finallyTempUniqueId++}");
        EmitLdaUndefined();
        EmitStarRegister(completionReg);
        return completionReg;
    }

    private void EmitStoreLoopCompletionValueIfNeeded(int completionReg)
    {
        if (completionReg >= 0)
            EmitStoreCompletionValueIfNotHole(completionReg);
    }

    private void VisitLoopBodyWithCompletion(JsStatement body, int completionReg)
    {
        if (completionReg < 0)
        {
            VisitStatement(body);
            return;
        }

        PushStatementCompletionState(completionReg, true);
        try
        {
            VisitStatement(body);
        }
        finally
        {
            PopStatementCompletionState();
        }
    }

    private void EmitLoadLoopCompletionValue(int completionReg)
    {
        if (completionReg >= 0)
        {
            EmitLoadRegisterOrUndefinedIfHole(completionReg);
            return;
        }

        EmitLdaUndefined();
    }

    private void EmitLoadRegisterOrUndefinedIfHole(int register)
    {
        var valueReg = AllocateTemporaryRegister();
        var hasValueLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitMoveRegister(register, valueReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(valueReg);
        builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, hasValueLabel);
        EmitLdaUndefined();
        EmitJump(endLabel);
        builder.BindLabel(hasValueLabel);
        EmitLdaRegister(valueReg);
        builder.BindLabel(endLabel);
        ReleaseTemporaryRegister(valueReg);
    }

    private void EmitNormalizeAccumulatorHoleToUndefined()
    {
        var valueReg = AllocateTemporaryRegister();
        var hasValueLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitStarRegister(valueReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(valueReg);
        builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, hasValueLabel);
        EmitLdaUndefined();
        EmitJump(endLabel);
        builder.BindLabel(hasValueLabel);
        EmitLdaRegister(valueReg);
        builder.BindLabel(endLabel);
        ReleaseTemporaryRegister(valueReg);
    }

    private void EmitLdaUndefined()
    {
        builder.EmitLda(JsOpCode.LdaUndefined);
    }

    private void EmitLdaNull()
    {
        builder.EmitLda(JsOpCode.LdaNull);
    }

    private void EmitLdaTheHole()
    {
        builder.EmitLda(JsOpCode.LdaTheHole);
    }

    private void EmitLdaTrue()
    {
        builder.EmitLda(JsOpCode.LdaTrue);
    }

    private void EmitLdaFalse()
    {
        builder.EmitLda(JsOpCode.LdaFalse);
    }

    private void EmitLdaZero()
    {
        builder.EmitLda(JsOpCode.LdaZero);
    }

    private void EmitLda(bool value)
    {
        if (value)
            EmitLdaTrue();
        else
            EmitLdaFalse();
    }

    private void EmitLda(long value)
    {
        if (value == 0)
        {
            EmitLdaZero();
        }
        else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaSmi, (byte)value);
        }
        else if (value >= short.MinValue && value <= short.MaxValue)
        {
            var v = (short)value;
            builder.EmitLda(JsOpCode.LdaSmiWide, (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF));
        }
        else if (value >= int.MinValue && value <= int.MaxValue)
        {
            var v = (int)value;
            builder.EmitLda(JsOpCode.LdaSmiExtraWide,
                (byte)(v & 0xFF),
                (byte)((v >> 8) & 0xFF),
                (byte)((v >> 16) & 0xFF),
                (byte)((v >> 24) & 0xFF));
        }
        else
        {
            var idx = builder.AddNumericConstant(value);
            EmitLdaNumericConstantByIndex(idx);
        }
    }

    private void EmitLdaNumericConstantByIndex(int idx)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaNumericConstant, (byte)idx);
            return;
        }

        if ((uint)idx <= ushort.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaNumericConstantWide, (byte)(idx & 0xFF), (byte)((idx >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Numeric constant pool index exceeds ushort operand capacity.");
    }

    private void EmitLdaStringConstantByIndex(int idx)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaStringConstant, (byte)idx);
            return;
        }

        EmitLdaTypedConstByIndex(Tag.JsTagString, idx);
    }

    private void EmitLdaTypedConstByIndex(Tag tag, int idx)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaTypedConst, (byte)tag, (byte)idx);
            return;
        }

        if ((uint)idx <= ushort.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaTypedConstWide, (byte)tag, (byte)(idx & 0xFF), (byte)((idx >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Typed constant pool index exceeds ushort operand capacity.");
    }

    private void EmitGeneratorSuspendResume(int delegateIteratorRegister = -1, bool minimizeLiveRange = false,
        bool guaranteedNextOnly = false, bool isAwaitSuspend = false, bool isPrestartSuspend = false,
        bool inspectActiveDelegateOnNext = false, BytecodeBuilder.Label? delegateCompletedAsNextLabel = null)
    {
        byte firstRegister = 0;
        var liveCountInt = builder.RegisterCount;
        if (minimizeLiveRange && TryGetConservativeSuspendLiveRange(out var first, out var count))
        {
            firstRegister = first;
            liveCountInt = count;
        }

        if (liveCountInt > byte.MaxValue)
            throw new NotSupportedException("Generator live register range exceeds bytecode operand capacity.");
        var liveCount = (byte)liveCountInt;

        var suspendIdInt = nextGeneratorSuspendId++;
        if ((uint)suspendIdInt > byte.MaxValue)
            throw new NotSupportedException("Generator suspend point id exceeds bytecode operand capacity.");
        var suspendId = (byte)suspendIdInt;

        var genRegister = isPrestartSuspend
            ? (byte)0xFD
            : isAwaitSuspend
                ? (byte)0xFE
                : delegateIteratorRegister >= 0
                    ? (byte)delegateIteratorRegister
                    : (byte)0xFF;
        EmitRaw(JsOpCode.SuspendGenerator, genRegister, firstRegister, liveCount, suspendId);
        if (functionKind != JsBytecodeFunctionKind.Normal && generatorSwitchInstructionPc >= 0)
        {
            while (generatorResumeTargetPcBySuspendId.Count <= suspendIdInt)
                generatorResumeTargetPcBySuspendId.Add(-1);
            generatorResumeTargetPcBySuspendId[suspendIdInt] = builder.CodeLength; // ResumeGenerator pc
        }

        EmitRaw(JsOpCode.ResumeGenerator, genRegister, firstRegister, liveCount);

        if (functionKind != JsBytecodeFunctionKind.Normal)
        {
            if (generatorResumeValueTempRegister < 0)
                generatorResumeValueTempRegister = AllocateSyntheticLocal("$gen.resume.value");
            if (generatorResumeModeTempRegister < 0)
                generatorResumeModeTempRegister = AllocateSyntheticLocal("$gen.resume.mode");

            EmitStarRegister(generatorResumeValueTempRegister);
            if (guaranteedNextOnly)
            {
                EmitCallRuntime(RuntimeId.GeneratorClearResumeState, 0, 0);
                EmitLdaRegister(generatorResumeValueTempRegister);
            }
            else
            {
                var nextModeLabel = builder.CreateLabel();
                var returnModeLabel = builder.CreateLabel();
                var throwModeLabel = builder.CreateLabel();
                var continueLabel = builder.CreateLabel();
                var delegateStillActiveLabel = inspectActiveDelegateOnNext ? builder.CreateLabel() : default;

                EmitCallRuntime(RuntimeId.GeneratorGetResumeMode, 0, 0);
                EmitStarRegister(generatorResumeModeTempRegister);
                EmitLdaZero();
                EmitTestEqualStrictRegister(generatorResumeModeTempRegister);
                builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, nextModeLabel);
                builder.EmitLda(JsOpCode.LdaSmi, 1);
                EmitTestEqualStrictRegister(generatorResumeModeTempRegister);
                builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, returnModeLabel);
                builder.EmitJump(JsOpCode.Jump, throwModeLabel);

                builder.BindLabel(nextModeLabel);
                EmitCallRuntime(RuntimeId.GeneratorClearResumeState, 0, 0);
                if (inspectActiveDelegateOnNext)
                {
                    if (delegateCompletedAsNextLabel is null)
                        throw new InvalidOperationException("yield* delegate completion label is required.");
                    EmitCallRuntime(RuntimeId.GeneratorHasActiveDelegateIterator, 0, 0);
                    builder.EmitJump(JsOpCode.JumpIfTrue, delegateStillActiveLabel);
                    EmitLdaRegister(generatorResumeValueTempRegister);
                    builder.EmitJump(JsOpCode.Jump, delegateCompletedAsNextLabel.Value);
                    builder.BindLabel(delegateStillActiveLabel);
                }

                EmitLdaRegister(generatorResumeValueTempRegister);
                builder.EmitJump(JsOpCode.Jump, continueLabel);

                builder.BindLabel(returnModeLabel);
                EmitCallRuntime(RuntimeId.GeneratorClearResumeState, 0, 0);
                EmitLdaRegister(generatorResumeValueTempRegister);
                EmitReturnConsideringFinallyFlow();

                builder.BindLabel(throwModeLabel);
                EmitCallRuntime(RuntimeId.GeneratorClearResumeState, 0, 0);
                EmitLdaRegister(generatorResumeValueTempRegister);
                EmitThrowConsideringFinallyFlow();

                builder.BindLabel(continueLabel);
            }
        }
    }

    private static bool IsGuaranteedFulfilledAwait(JsExpression argument)
    {
        if (argument is not JsLiteralExpression lit)
            return false;
        return lit.Value switch
        {
            null => true,
            double => true,
            bool => true,
            string => true,
            _ when lit.Value is JsValue => true, // includes `undefined` token literal
            _ => false
        };
    }

    private static bool ExpressionMaySuspendInCurrentFunction(JsExpression expr)
    {
        switch (expr)
        {
            case JsAwaitExpression:
            case JsYieldExpression:
                return true;
            case JsAssignmentExpression a:
                return ExpressionMaySuspendInCurrentFunction(a.Left) || ExpressionMaySuspendInCurrentFunction(a.Right);
            case JsBinaryExpression b:
                return ExpressionMaySuspendInCurrentFunction(b.Left) || ExpressionMaySuspendInCurrentFunction(b.Right);
            case JsConditionalExpression c:
                return ExpressionMaySuspendInCurrentFunction(c.Test) ||
                       ExpressionMaySuspendInCurrentFunction(c.Consequent) ||
                       ExpressionMaySuspendInCurrentFunction(c.Alternate);
            case JsCallExpression c:
                if (ExpressionMaySuspendInCurrentFunction(c.Callee))
                    return true;
                foreach (var arg in c.Arguments)
                    if (ExpressionMaySuspendInCurrentFunction(arg))
                        return true;

                return false;
            case JsNewExpression n:
                if (ExpressionMaySuspendInCurrentFunction(n.Callee))
                    return true;
                foreach (var arg in n.Arguments)
                    if (ExpressionMaySuspendInCurrentFunction(arg))
                        return true;

                return false;
            case JsMemberExpression m:
                return ExpressionMaySuspendInCurrentFunction(m.Object) ||
                       (m.IsComputed && ExpressionMaySuspendInCurrentFunction(m.Property));
            case JsSequenceExpression s:
                foreach (var e in s.Expressions)
                    if (ExpressionMaySuspendInCurrentFunction(e))
                        return true;

                return false;
            case JsArrayExpression a:
                foreach (var e in a.Elements)
                    if (e is not null && ExpressionMaySuspendInCurrentFunction(e))
                        return true;

                return false;
            case JsObjectExpression o:
                foreach (var prop in o.Properties)
                {
                    if (prop.IsComputed && prop.ComputedKey is not null &&
                        ExpressionMaySuspendInCurrentFunction(prop.ComputedKey))
                        return true;
                    if (prop.Value is not null && ExpressionMaySuspendInCurrentFunction(prop.Value))
                        return true;
                }

                return false;
            case JsTemplateExpression t:
                foreach (var e in t.Expressions)
                    if (ExpressionMaySuspendInCurrentFunction(e))
                        return true;

                return false;
            case JsTaggedTemplateExpression tt:
                if (ExpressionMaySuspendInCurrentFunction(tt.Tag))
                    return true;
                foreach (var e in tt.Template.Expressions)
                    if (ExpressionMaySuspendInCurrentFunction(e))
                        return true;

                return false;
            case JsUnaryExpression u:
                return ExpressionMaySuspendInCurrentFunction(u.Argument);
            case JsUpdateExpression u:
                return ExpressionMaySuspendInCurrentFunction(u.Argument);
            default:
                return false;
        }
    }

    private static bool ArgumentsMaySuspendInCurrentFunction(IReadOnlyList<JsExpression> arguments)
    {
        foreach (var argument in arguments)
            if (ExpressionMaySuspendInCurrentFunction(argument))
                return true;

        return false;
    }

    private bool TryGetConservativeSuspendLiveRange(out byte firstRegister, out int liveCount)
    {
        var minReg = int.MaxValue;
        var maxReg = -1;
        foreach (var reg in locals.Values)
        {
            if (reg < minReg) minReg = reg;
            if (reg > maxReg) maxReg = reg;
        }

        if (builder.TryGetActiveTemporaryRegisterRange(out var activeTempMinReg, out var activeTempMaxReg))
        {
            if (activeTempMinReg < minReg) minReg = activeTempMinReg;
            if (activeTempMaxReg > maxReg) maxReg = activeTempMaxReg;
        }

        foreach (var (reg, refCount) in suspendPinnedRegisterRefCounts)
        {
            if (refCount <= 0)
                continue;
            if (reg < minReg) minReg = reg;
            if (reg > maxReg) maxReg = reg;
        }

        if (maxReg < 0)
        {
            firstRegister = 0;
            liveCount = 0;
            return false;
        }

        firstRegister = (byte)minReg;
        liveCount = maxReg - minReg + 1;
        return true;
    }

    private void PinSuspendRegister(int register)
    {
        if (suspendPinnedRegisterRefCounts.TryGetValue(register, out var count))
            suspendPinnedRegisterRefCounts[register] = count + 1;
        else
            suspendPinnedRegisterRefCounts[register] = 1;
    }

    private void UnpinSuspendRegister(int register)
    {
        if (!suspendPinnedRegisterRefCounts.TryGetValue(register, out var count))
            return;
        if (count <= 1)
            suspendPinnedRegisterRefCounts.Remove(register);
        else
            suspendPinnedRegisterRefCounts[register] = count - 1;
    }

    private void PatchGeneratorSwitchTable()
    {
        if (generatorSwitchInstructionPc < 0)
            return;

        var tableLen = generatorResumeTargetPcBySuspendId.Count;
        if (tableLen > byte.MaxValue)
            throw new NotSupportedException("Generator switch table length exceeds bytecode operand capacity.");

        var tableStart = builder.GeneratorSwitchTargetCount;
        if (tableStart > byte.MaxValue)
            throw new NotSupportedException("Generator switch table start exceeds bytecode operand capacity.");

        for (var i = 0; i < tableLen; i++)
        {
            var targetPc = generatorResumeTargetPcBySuspendId[i];
            if (targetPc < 0)
                throw new InvalidOperationException("Generator switch table has an unresolved suspend target.");
            builder.AddGeneratorSwitchTarget(targetPc);
        }

        builder.PatchByte(generatorSwitchInstructionPc + 2, (byte)tableStart);
        builder.PatchByte(generatorSwitchInstructionPc + 3, (byte)tableLen);
    }

    private void EmitArrayLiteralIntoRegister(JsArrayExpression arrExpr, int targetReg)
    {
        if (TryEmitCompileTimeArrayLiteralIntoRegister(arrExpr, targetReg))
            return;

        var hasSpread = false;
        for (var i = 0; i < arrExpr.Elements.Count; i++)
            if (arrExpr.Elements[i] is JsSpreadExpression)
            {
                hasSpread = true;
                break;
            }

        if (hasSpread)
        {
            EmitArrayLiteralWithSpreadIntoRegister(arrExpr, targetReg);
            return;
        }

        var literalIdx = builder.AddObjectConstant(arrExpr.Elements.Count);
        EmitCreateArrayLiteralByIndex(literalIdx);
        EmitStarRegister(targetReg);
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            for (var i = 0; i < arrExpr.Elements.Count; i++)
            {
                var elem = arrExpr.Elements[i];
                if (elem is null) EmitLdaTheHole();
                else VisitExpression(elem);

                EmitInitializeArrayElement(targetReg, i);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private bool TryEmitCompileTimeArrayLiteralIntoRegister(JsArrayExpression arrExpr, int targetReg)
    {
        if (!TryCreateCompileTimeArrayLiteralPayload(arrExpr, out var literalValues))
            return false;

        var literalIdx = builder.AddObjectConstant(literalValues);
        EmitCreateArrayLiteralByIndex(literalIdx);
        EmitStarRegister(targetReg);
        return true;
    }

    private static bool TryCreateCompileTimeArrayLiteralPayload(JsArrayExpression arrExpr, out JsValue[] values)
    {
        if (arrExpr.Elements.Count == 0)
        {
            values = Array.Empty<JsValue>();
            return true;
        }

        for (var i = 0; i < arrExpr.Elements.Count; i++)
            if (!TryGetCompileTimeArrayLiteralElementValue(arrExpr.Elements[i], out _))
            {
                values = Array.Empty<JsValue>();
                return false;
            }

        values = new JsValue[arrExpr.Elements.Count];
        for (var i = 0; i < arrExpr.Elements.Count; i++)
            _ = TryGetCompileTimeArrayLiteralElementValue(arrExpr.Elements[i], out values[i]);

        return true;
    }

    private static bool TryGetCompileTimeArrayLiteralElementValue(JsExpression? element, out JsValue value)
    {
        if (element is null)
        {
            value = JsValue.TheHole;
            return true;
        }

        if (element is not JsLiteralExpression literal)
        {
            value = JsValue.Undefined;
            return false;
        }

        switch (literal.Value)
        {
            case null:
                value = JsValue.Null;
                return true;
            case bool b:
                value = b ? JsValue.True : JsValue.False;
                return true;
            case int i32:
                value = JsValue.FromInt32(i32);
                return true;
            case long i64 when i64 >= int.MinValue && i64 <= int.MaxValue:
                value = JsValue.FromInt32((int)i64);
                return true;
            case double d when d >= int.MinValue && d <= int.MaxValue && d == Math.Truncate(d):
                value = JsValue.FromInt32((int)d);
                return true;
            case double d:
                value = new(d);
                return true;
            case string s:
                value = JsValue.FromString(s);
                return true;
            case JsValue jsValue:
                value = jsValue;
                return true;
            default:
                value = JsValue.Undefined;
                return false;
        }
    }

    private void EmitArrayLiteralWithSpreadIntoRegister(JsArrayExpression arrExpr, int targetReg)
    {
        EmitCreateEmptyArrayLiteral();
        EmitStarRegister(targetReg);

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var keyReg = AllocateTemporaryRegister();
            var indexReg = AllocateTemporaryRegister();
            EmitLdaZero();
            EmitStarRegister(indexReg);

            for (var i = 0; i < arrExpr.Elements.Count; i++)
            {
                var elem = arrExpr.Elements[i];
                if (elem is JsSpreadExpression spread)
                {
                    VisitExpression(spread.Argument);
                    var sourceReg = AllocateTemporaryRegister();
                    EmitStarRegister(sourceReg);

                    var argStart = AllocateTemporaryRegisterBlock(3);
                    EmitMoveRegister(targetReg, argStart);
                    EmitMoveRegister(sourceReg, argStart + 1);
                    EmitMoveRegister(indexReg, argStart + 2);
                    EmitCallRuntime(RuntimeId.AppendArraySpread, argStart, 3);
                    EmitStarRegister(indexReg);
                    continue;
                }

                EmitMoveRegister(indexReg, keyReg);

                if (elem is null)
                    EmitLdaTheHole();
                else
                    VisitExpression(elem);

                EmitDefineOwnKeyedProperty(targetReg, keyReg);
                EmitLdaRegister(indexReg);
                EmitRaw(JsOpCode.Inc);
                EmitStarRegister(indexReg);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitObjectLiteralIntoRegister(JsObjectExpression objExpr, int targetReg)
    {
        if (objExpr.Properties.Count == 0)
        {
            EmitRaw(JsOpCode.CreateEmptyObjectLiteral);
            EmitStarRegister(targetReg);
            return;
        }

        var atomTable = Vm.Atoms;
        var shapePrefixEnd = objExpr.Properties.Count;
        var prefixAccessorKindByAtom = new Dictionary<int, bool>();
        for (var i = 0; i < objExpr.Properties.Count; i++)
        {
            var p = objExpr.Properties[i];
            if (p.Kind == JsObjectPropertyKind.Spread || p.IsComputed ||
                TryGetCanonicalArrayIndexObjectLiteralKey(p, out _))
            {
                shapePrefixEnd = i;
                break;
            }

            var atom = atomTable.InternNoCheck(p.Key);
            var isAccessor = p.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter;
            if (prefixAccessorKindByAtom.TryGetValue(atom, out var existingIsAccessor) &&
                existingIsAccessor != isAccessor)
            {
                shapePrefixEnd = i;
                break;
            }

            prefixAccessorKindByAtom[atom] = isAccessor;
        }

        var namePlanByProperty = new NamedLiteralPropertyPlan[objExpr.Properties.Count];
        var orderedUniqueNamedAtoms = new List<int>(objExpr.Properties.Count);
        var finalFlagsByAtom = new Dictionary<int, JsShapePropertyFlags>();
        var firstSeen = new HashSet<int>();
        for (var i = 0; i < shapePrefixEnd; i++)
        {
            var prop = objExpr.Properties[i];
            var atom = atomTable.InternNoCheck(prop.Key);
            if (firstSeen.Add(atom))
                orderedUniqueNamedAtoms.Add(atom);

            var initFlags = prop.Kind switch
            {
                JsObjectPropertyKind.Data => JsShapePropertyFlags.Open,
                JsObjectPropertyKind.Getter => JsShapePropertyFlags.HasGetter,
                JsObjectPropertyKind.Setter => JsShapePropertyFlags.HasSetter,
                _ => throw new NotImplementedException(
                    $"Object property kind {prop.Kind} is not supported in Okojo Phase 1.")
            };

            if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                initFlags |= JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable;

            if (!finalFlagsByAtom.TryGetValue(atom, out var currentFlags))
                finalFlagsByAtom[atom] = NormalizeObjectLiteralFinalFlags(initFlags);
            else
                finalFlagsByAtom[atom] = MergeObjectLiteralPropertyFlags(currentFlags, initFlags, prop.Key);

            namePlanByProperty[i] = new(atom, initFlags);
        }

        var shape = Vm.EmptyShape;
        for (var i = 0; i < orderedUniqueNamedAtoms.Count; i++)
        {
            var atom = orderedUniqueNamedAtoms[i];
            shape = shape.GetOrAddTransition(atom, finalFlagsByAtom[atom], out _);
        }

        var literalBoilerplateIdx = builder.AddObjectConstant(shape);
        EmitCreateObjectLiteralByIndex(literalBoilerplateIdx);
        EmitStarRegister(targetReg);

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var keyReg = -1;
            for (var i = 0; i < objExpr.Properties.Count; i++)
            {
                var prop = objExpr.Properties[i];
                if (prop.Kind == JsObjectPropertyKind.Spread)
                {
                    EmitObjectLiteralSpread(targetReg, prop.Value);
                    continue;
                }

                if (TryGetCanonicalArrayIndexObjectLiteralKey(prop, out var index))
                {
                    EmitObjectLiteralIndexedKey(index);
                    if (keyReg == -1)
                        keyReg = AllocateTemporaryRegister();
                    EmitStarRegister(keyReg);
                    if (prop.Kind is JsObjectPropertyKind.Data)
                    {
                        EmitObjectLiteralDataValue(prop, targetReg);
                        EmitDefineOwnKeyedProperty(targetReg, keyReg);
                    }
                    else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                    {
                        EmitDefineObjectLiteralAccessor(targetReg, keyReg, prop);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                    }

                    continue;
                }

                if (prop.IsComputed)
                {
                    if (prop.ComputedKey is null)
                        throw new InvalidOperationException("Computed object literal key expression is missing.");
                    VisitExpression(prop.ComputedKey);
                    if (keyReg == -1)
                        keyReg = AllocateTemporaryRegister();
                    EmitStarRegister(keyReg);
                    EmitCallRuntime(RuntimeId.NormalizePropertyKey, keyReg, 1);
                    EmitStarRegister(keyReg);
                    if (prop.Kind is JsObjectPropertyKind.Data)
                    {
                        EmitObjectLiteralDataValue(prop, targetReg);
                        EmitDefineOwnKeyedProperty(targetReg, keyReg);
                    }
                    else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                    {
                        EmitDefineObjectLiteralAccessor(targetReg, keyReg, prop);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                    }

                    continue;
                }

                if (i < shapePrefixEnd)
                {
                    if (!prop.IsComputed) EmitObjectLiteralDataValue(prop, targetReg);

                    if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                    {
                        if (keyReg == -1)
                            keyReg = AllocateTemporaryRegister();
                        var keyIdx = builder.AddObjectConstant(prop.Key);
                        EmitLdaStringConstantByIndex(keyIdx);
                        EmitStarRegister(keyReg);
                        EmitDefineObjectLiteralAccessor(targetReg, keyReg, prop);
                        continue;
                    }

                    var plan = namePlanByProperty[i];
                    if (!shape.TryGetSlotInfo(plan.Atom, out var slotInfo))
                        throw new InvalidOperationException("Missing precomputed object literal shape slot.");
                    var slot = prop.Kind == JsObjectPropertyKind.Setter &&
                               (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) ==
                               JsShapePropertyFlags.BothAccessor
                        ? slotInfo.AccessorSetterSlot
                        : slotInfo.Slot;
                    EmitInitializeNamedProperty(targetReg, slot);
                }
                else
                {
                    if (prop.Kind is JsObjectPropertyKind.Data)
                    {
                        if (keyReg == -1)
                            keyReg = AllocateTemporaryRegister();
                        var keyIdx = builder.AddObjectConstant(prop.Key);
                        EmitLdaStringConstantByIndex(keyIdx);
                        EmitStarRegister(keyReg);
                        EmitObjectLiteralDataValue(prop, targetReg);
                        EmitDefineOwnKeyedProperty(targetReg, keyReg);
                    }
                    else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                    {
                        if (keyReg == -1)
                            keyReg = AllocateTemporaryRegister();
                        var keyIdx = builder.AddObjectConstant(prop.Key);
                        EmitLdaStringConstantByIndex(keyIdx);
                        EmitStarRegister(keyReg);
                        EmitDefineObjectLiteralAccessor(targetReg, keyReg, prop);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                    }
                }
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitDefineObjectLiteralAccessor(int targetReg, int keyReg, JsObjectProperty prop)
    {
        if (prop.Kind is not (JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter))
            throw new InvalidOperationException(
                "Object literal accessor emitter requires getter/setter property kind.");

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(4);
            var keyArgReg = argStart + 1;
            var getterReg = argStart + 2;
            var setterReg = argStart + 3;

            EmitMoveRegister(targetReg, argStart);
            EmitMoveRegister(keyReg, keyArgReg);

            if (prop.Kind == JsObjectPropertyKind.Getter)
                EmitObjectLiteralDataValue(prop, targetReg);
            else
                EmitLdaUndefined();
            EmitStarRegister(getterReg);

            if (prop.Kind == JsObjectPropertyKind.Setter)
                EmitObjectLiteralDataValue(prop, targetReg);
            else
                EmitLdaUndefined();
            EmitStarRegister(setterReg);

            EmitCallRuntime(RuntimeId.DefineObjectAccessor, argStart, 4);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private bool TryEmitFoldedUnaryLiteral(JsUnaryExpression unary)
    {
        if (unary.Argument is not JsLiteralExpression lit) return false;

        switch (unary.Operator)
        {
            case JsUnaryOperator.LogicalNot:
            {
                var b = lit.Value switch
                {
                    null => false,
                    bool bb => bb,
                    double d => d != 0 && !double.IsNaN(d),
                    JsBigInt bi => !bi.Value.IsZero,
                    string s => s.Length > 0,
                    _ => throw new NotImplementedException($"Unary literal fold ! for {lit.Value?.GetType().Name}")
                };
                EmitLda(!b);
                return true;
            }
            case JsUnaryOperator.BitwiseNot:
            {
                if (lit.Value is not double d) return false;
                var v = ToInt32ForLiteralFold(d);
                EmitNumberLiteral(~v);
                return true;
            }
            case JsUnaryOperator.Plus:
            {
                if (lit.Value is not double d) return false;
                EmitNumberLiteral(d);
                return true;
            }
            case JsUnaryOperator.Minus:
            {
                if (lit.Value is not double d) return false;
                EmitNumberLiteral(-d);
                return true;
            }
            case JsUnaryOperator.Void:
            {
                EmitLdaUndefined();
                return true;
            }
            default:
                return false;
        }
    }

    private static JsShapePropertyFlags NormalizeObjectLiteralFinalFlags(JsShapePropertyFlags initFlags)
    {
        if ((initFlags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) == 0)
            return JsShapePropertyFlags.Open;
        return (initFlags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) |
               JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable;
    }

    private static JsShapePropertyFlags MergeObjectLiteralPropertyFlags(JsShapePropertyFlags currentFlags,
        JsShapePropertyFlags nextInitFlags, string keyForError)
    {
        var currentAccessor = (currentFlags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
        var nextAccessor = (nextInitFlags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;

        if (!currentAccessor && !nextAccessor)
            return JsShapePropertyFlags.Open; // duplicate data property; last write wins

        if (currentAccessor && nextAccessor)
        {
            var merged = (currentFlags | nextInitFlags) &
                         (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter);
            return merged | JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable;
        }

        if (nextAccessor)
        {
            var accessorFlags = nextInitFlags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter);
            return accessorFlags | JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable;
        }

        return JsShapePropertyFlags.Open;
    }

    private void EmitNumberLiteral(double d)
    {
        if (!IsNegativeZero(d) && d % 1 == 0 && d >= int.MinValue && d <= int.MaxValue)
            EmitLda((long)d);
        else
            EmitLdaNumericConstantByIndex(builder.AddNumericConstant(d));
    }

    private static bool IsNegativeZero(double d)
    {
        return d == 0d && BitConverter.DoubleToInt64Bits(d) == unchecked((long)0x8000000000000000);
    }

    private static bool TryMapBinaryOperatorToOkojoOpCode(JsBinaryOperator op, out JsOpCode jsOp)
    {
        jsOp = op switch
        {
            JsBinaryOperator.Add => JsOpCode.Add,
            JsBinaryOperator.Subtract => JsOpCode.Sub,
            JsBinaryOperator.Multiply => JsOpCode.Mul,
            JsBinaryOperator.Divide => JsOpCode.Div,
            JsBinaryOperator.Modulo => JsOpCode.Mod,
            JsBinaryOperator.Exponentiate => JsOpCode.Exp,
            JsBinaryOperator.BitwiseAnd => JsOpCode.BitwiseAnd,
            JsBinaryOperator.BitwiseOr => JsOpCode.BitwiseOr,
            JsBinaryOperator.BitwiseXor => JsOpCode.BitwiseXor,
            JsBinaryOperator.ShiftLeft => JsOpCode.ShiftLeft,
            JsBinaryOperator.ShiftRight => JsOpCode.ShiftRight,
            JsBinaryOperator.ShiftRightLogical => JsOpCode.ShiftRightLogical,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThan,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThan,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqual,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqual,
            JsBinaryOperator.Equal => JsOpCode.TestEqual,
            JsBinaryOperator.NotEqual => JsOpCode.TestNotEqual,
            JsBinaryOperator.StrictEqual => JsOpCode.TestEqualStrict,
            JsBinaryOperator.In => JsOpCode.TestIn,
            JsBinaryOperator.Instanceof => JsOpCode.TestInstanceOf,
            _ => (JsOpCode)byte.MaxValue
        };
        return (byte)jsOp != byte.MaxValue;
    }

    private static bool TryMapCompoundAssignmentOperatorToOkojoOpCode(JsAssignmentOperator op, out JsOpCode jsOp)
    {
        JsBinaryOperator? binaryOp = op switch
        {
            JsAssignmentOperator.AddAssign => JsBinaryOperator.Add,
            JsAssignmentOperator.SubtractAssign => JsBinaryOperator.Subtract,
            JsAssignmentOperator.MultiplyAssign => JsBinaryOperator.Multiply,
            JsAssignmentOperator.ExponentiateAssign => JsBinaryOperator.Exponentiate,
            JsAssignmentOperator.DivideAssign => JsBinaryOperator.Divide,
            JsAssignmentOperator.ModuloAssign => JsBinaryOperator.Modulo,
            JsAssignmentOperator.ShiftLeftAssign => JsBinaryOperator.ShiftLeft,
            JsAssignmentOperator.ShiftRightAssign => JsBinaryOperator.ShiftRight,
            JsAssignmentOperator.ShiftRightLogicalAssign => JsBinaryOperator.ShiftRightLogical,
            JsAssignmentOperator.BitwiseAndAssign => JsBinaryOperator.BitwiseAnd,
            JsAssignmentOperator.BitwiseOrAssign => JsBinaryOperator.BitwiseOr,
            JsAssignmentOperator.BitwiseXorAssign => JsBinaryOperator.BitwiseXor,
            _ => null
        };
        if (binaryOp is null)
        {
            jsOp = default;
            return false;
        }

        return TryMapBinaryOperatorToOkojoOpCode(binaryOp.Value, out jsOp);
    }

    private static bool IsLogicalAssignmentOperator(JsAssignmentOperator op)
    {
        return op is JsAssignmentOperator.LogicalAndAssign or JsAssignmentOperator.LogicalOrAssign
            or JsAssignmentOperator.NullishCoalescingAssign;
    }

    private static int ToInt32ForLiteralFold(double number)
    {
        if (number == 0d || double.IsNaN(number) || double.IsInfinity(number))
            return 0;

        var intPart = Math.Truncate(number);
        var mod = intPart % 4294967296d;
        if (mod < 0d)
            mod += 4294967296d;
        if (mod >= 2147483648d)
            mod -= 4294967296d;
        return (int)mod;
    }

    private static bool TryGetNamedMemberKey(JsMemberExpression member, out string key)
    {
        key = string.Empty;
        if (member.IsComputed || member.IsPrivate) return false;
        switch (member.Property)
        {
            case JsLiteralExpression { Value: string s }:
                key = s;
                return true;
            case JsIdentifierExpression id:
                key = id.Name;
                return true;
            default:
                return false;
        }
    }

    private void ThrowUnexpectedPrivateFieldSyntaxError(int position)
    {
        throw new JsParseException("Unexpected private field", position, CurrentSourceText);
    }

    private void EmitObjectLiteralIndexedKey(uint index)
    {
        if (index <= 127)
        {
            EmitLda((sbyte)index);
            return;
        }

        var numIdx = builder.AddNumericConstant(index);
        EmitLdaNumericConstantByIndex(numIdx);
    }

    private static bool TryGetCanonicalArrayIndexObjectLiteralKey(JsObjectProperty prop, out uint index)
    {
        if (!prop.IsComputed)
            return TryParseCanonicalArrayIndexString(prop.Key, out index);

        if (prop.ComputedKey is JsLiteralExpression { Value: string s })
            return TryParseCanonicalArrayIndexString(s, out index);

        if (prop.ComputedKey is JsLiteralExpression { Value: double d })
            if (d >= 0 && d <= uint.MaxValue && d == Math.Truncate(d))
            {
                index = (uint)d;
                return index != uint.MaxValue;
            }

        if (prop.ComputedKey is JsLiteralExpression { Value: int i } && i >= 0)
        {
            index = (uint)i;
            return true;
        }

        index = 0;
        return false;
    }

    private static bool TryGetCompileTimeObjectLiteralPropertyKeyValue(string key, out JsValue value)
    {
        if (TryParseCanonicalArrayIndexString(key, out var index))
        {
            value = index <= int.MaxValue
                ? JsValue.FromInt32((int)index)
                : new((double)index);
            return true;
        }

        value = JsValue.FromString(key);
        return true;
    }

    private static bool TryParseCanonicalArrayIndexString(string text, out uint index)
    {
        if (string.IsNullOrEmpty(text))
        {
            index = 0;
            return false;
        }

        if (text.Length > 1 && text[0] == '0')
        {
            index = 0;
            return false;
        }

        uint value = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((uint)(c - '0') > 9u)
            {
                index = 0;
                return false;
            }

            var digit = (uint)(c - '0');
            if (value > (uint.MaxValue - digit) / 10u)
            {
                index = 0;
                return false;
            }

            value = value * 10u + digit;
        }

        if (value == uint.MaxValue)
        {
            index = 0;
            return false;
        }

        index = value;
        return true;
    }

    private void EmitForInOfStatement(JsForInOfStatement stmt, IReadOnlyList<string>? labels = null)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            HashSet<int>? perIterationSlots = null;
            PushForInOfHeadLexicalAliases(stmt);
            try
            {
                perIterationSlots = TryCollectForInOfPerIterationContextSlots(stmt);
            }
            finally
            {
                PopForInOfHeadLexicalAliases(stmt);
            }

            perIterationSlots = UnionPerIterationContextSlots(
                perIterationSlots,
                TryCollectDirectLoopBodyPerIterationContextSlots(stmt.Body));
            var needsPerIterationContext = perIterationSlots is not null;
            if (needsPerIterationContext)
            {
                activePerIterationContextSlots.Push(perIterationSlots!);
                EmitPushClonedCurrentContext();
            }

            if (!stmt.IsOf)
            {
                EmitForInStatement(stmt, labels, needsPerIterationContext);
                if (needsPerIterationContext)
                    Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
                return;
            }

            if (stmt.IsAwait)
            {
                EmitForAwaitOfStatement(stmt, labels, needsPerIterationContext);
                if (needsPerIterationContext)
                    Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
                return;
            }

            var completionReg = AllocateSyntheticLocal($"$forof.cpl.{finallyTempUniqueId++}");
            EmitLdaUndefined();
            EmitStarRegister(completionReg);

            PushForInOfHeadTdzLexicalAliases(stmt);
            try
            {
                VisitExpression(stmt.Right);
            }
            finally
            {
                PopForInOfHeadTdzLexicalAliases(stmt);
            }

            var iterableReg = AllocateTemporaryRegister();
            EmitStarRegister(iterableReg);

            var arrayLengthReg = AllocateTemporaryRegister();
            EmitCallRuntime(RuntimeId.ForOfFastPathLength, iterableReg, 1);
            EmitStarRegister(arrayLengthReg);

            var fastArrayLabel = builder.CreateLabel();
            var doneLabel = builder.CreateLabel();
            var fallbackLabel = builder.CreateLabel();

            EmitLdaRegister(arrayLengthReg);
            EmitRaw(JsOpCode.TestLessThanSmi, 0, 0);
            EmitJumpIfToBooleanFalse(fastArrayLabel);
            EmitJump(fallbackLabel);

            PushForInOfHeadLexicalAliases(stmt);
            try
            {
                EmitForOfFastArrayPath(stmt, iterableReg, arrayLengthReg, fastArrayLabel, doneLabel, completionReg,
                    labels,
                    needsPerIterationContext);
                EmitForOfIteratorFallbackPath(stmt, iterableReg, fallbackLabel, doneLabel, completionReg, labels,
                    needsPerIterationContext);
            }
            finally
            {
                PopForInOfHeadLexicalAliases(stmt);
            }

            builder.BindLabel(doneLabel);
            if (needsPerIterationContext)
            {
                EmitRaw(JsOpCode.PopContext);
                Vm.ReturnCompileHashSet(activePerIterationContextSlots.Pop());
            }

            EmitLoadRegisterOrUndefinedIfHole(completionReg);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitForInStatement(JsForInOfStatement stmt, IReadOnlyList<string>? labels = null,
        bool needsPerIterationContext = false)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var completionReg = AllocateLoopCompletionRegisterIfNeeded(stmt.Body, "$forin");
            PushForInOfHeadTdzLexicalAliases(stmt);
            try
            {
                VisitExpression(stmt.Right);
            }
            finally
            {
                PopForInOfHeadTdzLexicalAliases(stmt);
            }

            var enumerableReg = AllocateTemporaryRegister();
            EmitStarRegister(enumerableReg);
            EmitRaw(JsOpCode.ForInEnumerate, (byte)enumerableReg);
            var iteratorReg = AllocateTemporaryRegister();
            EmitStarRegister(iteratorReg);

            PushForInOfHeadLexicalAliases(stmt);
            try
            {
                var loopLabel = builder.CreateLabel();
                var continueLabel = builder.CreateLabel();
                var loopBreakLabel = builder.CreateLabel();
                var doneLabel = builder.CreateLabel();
                builder.BindLabel(loopLabel);

                EmitRaw(JsOpCode.ForInNext, (byte)iteratorReg);
                builder.EmitJump(JsOpCode.JumpIfUndefined, doneLabel);
                EmitForIterationAssignLeft(stmt.Left, true);

                loopTargets.Push(new(loopBreakLabel, continueLabel));
                breakTargets.Push(loopBreakLabel);
                if (labels is not null && labels.Count != 0)
                    PushLabeledTargets(labels, loopBreakLabel, continueLabel, true);
                try
                {
                    VisitLoopBodyWithCompletion(stmt.Body, completionReg);
                }
                finally
                {
                    if (labels is not null && labels.Count != 0)
                        PopLabeledTargets(labels.Count);
                    breakTargets.Pop();
                    loopTargets.Pop();
                }

                builder.BindLabel(continueLabel);
                EmitStoreLoopCompletionValueIfNeeded(completionReg);
                if (needsPerIterationContext)
                    EmitRotatePerIterationContext();
                EmitRaw(JsOpCode.ForInStep, (byte)iteratorReg);
                EmitJump(loopLabel);
                builder.BindLabel(loopBreakLabel);
                EmitStoreLoopCompletionValueIfNeeded(completionReg);
                if (ShouldEmitLoopBreakExitJump(completionReg, needsPerIterationContext))
                    EmitJump(doneLabel);
                builder.BindLabel(doneLabel);
                if (needsPerIterationContext)
                    EmitRaw(JsOpCode.PopContext);
                EmitLoadLoopCompletionValue(completionReg);
            }
            finally
            {
                PopForInOfHeadLexicalAliases(stmt);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitForOfFastArrayPath(
        JsForInOfStatement stmt,
        int iterableReg,
        int arrayLengthReg,
        BytecodeBuilder.Label fastArrayLabel,
        BytecodeBuilder.Label doneLabel,
        int completionReg,
        IReadOnlyList<string>? labels = null,
        bool needsPerIterationContext = false)
    {
        builder.BindLabel(fastArrayLabel);
        var indexReg = AllocateTemporaryRegister();
        EmitLdaZero();
        EmitStarRegister(indexReg);
        var usingLikeLeft = TryGetUsingLikeForInOfLeft(stmt, out var usingLikeDeclaration);
        var iterationValueReg = usingLikeLeft ? AllocateTemporaryRegister() : -1;

        var loopLabel = builder.CreateLabel();
        var continueLabel = builder.CreateLabel();
        var loopBreakLabel = builder.CreateLabel();
        builder.BindLabel(loopLabel);

        EmitCallRuntime(RuntimeId.ForOfFastPathLength, iterableReg, 1);
        EmitStarRegister(arrayLengthReg);
        EmitLdaRegister(arrayLengthReg);
        EmitRaw(JsOpCode.TestLessThan, (byte)indexReg, 0);
        EmitJumpIfToBooleanFalse(doneLabel);

        EmitLdaRegister(indexReg);
        EmitLdaKeyedProperty(iterableReg);
        if (usingLikeLeft)
        {
            EmitStarRegister(iterationValueReg);
            EmitLdaRegister(iterationValueReg);
        }
        EmitForIterationAssignLeft(stmt.Left, true);

        loopTargets.Push(new(loopBreakLabel, continueLabel));
        breakTargets.Push(loopBreakLabel);
        if (labels is not null && labels.Count != 0)
            PushLabeledTargets(labels, loopBreakLabel, continueLabel, true);
        try
        {
            if (usingLikeLeft)
            {
                EmitExplicitResourceScope(
                    () => VisitLoopBodyWithCompletion(stmt.Body, completionReg),
                    usingLikeDeclaration.Kind == JsVariableDeclarationKind.AwaitUsing,
                    _ => EmitRegisterExplicitResource(usingLikeDeclaration.Kind, iterationValueReg));
            }
            else
            {
                VisitLoopBodyWithCompletion(stmt.Body, completionReg);
            }
        }
        finally
        {
            if (labels is not null && labels.Count != 0)
                PopLabeledTargets(labels.Count);
            breakTargets.Pop();
            loopTargets.Pop();
        }

        builder.BindLabel(continueLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        if (needsPerIterationContext)
            EmitRotatePerIterationContext();
        EmitLdaRegister(indexReg);
        EmitRaw(JsOpCode.Inc);
        EmitStarRegister(indexReg);
        EmitJump(loopLabel);
        builder.BindLabel(loopBreakLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        EmitJump(doneLabel);
    }

    private void EmitForOfIteratorFallbackPath(
        JsForInOfStatement stmt,
        int iterableReg,
        BytecodeBuilder.Label fallbackLabel,
        BytecodeBuilder.Label doneLabel,
        int completionReg,
        IReadOnlyList<string>? labels = null,
        bool needsPerIterationContext = false)
    {
        builder.BindLabel(fallbackLabel);

        var iteratorMethodReg = AllocateTemporaryRegister();
        EmitLoadIteratorMethod(iterableReg, iteratorMethodReg);
        EmitCallIteratorMethod(iterableReg, iteratorMethodReg);
        var iterReg = AllocateTemporaryRegister();
        EmitStarRegister(iterReg);

        var nextNameIdx = builder.AddAtomizedStringConstant(AtomTable.IdNext);
        var doneNameIdx = builder.AddAtomizedStringConstant(AtomTable.IdDone);
        var valueNameIdx = builder.AddAtomizedStringConstant(AtomTable.IdValue);
        EmitLdaNamedPropertyByIndex(iterReg, nextNameIdx, builder.AllocateFeedbackSlot());
        var nextMethodReg = AllocateTemporaryRegister();
        EmitStarRegister(nextMethodReg);
        var stepValueReg = AllocateTemporaryRegister();
        var stepResultReg = AllocateTemporaryRegister();

        var loopLabel = builder.CreateLabel();
        var continueLabel = builder.CreateLabel();
        var loopBreakLabel = builder.CreateLabel();
        builder.BindLabel(loopLabel);
        EmitRaw(JsOpCode.CallProperty, (byte)nextMethodReg, (byte)iterReg, 0, 0);
        EmitStarRegister(stepResultReg);
        EmitEnsureIteratorResultObject(stepResultReg);
        EmitLdaNamedPropertyByIndex(stepResultReg, doneNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, doneLabel);
        EmitLdaNamedPropertyByIndex(stepResultReg, valueNameIdx, builder.AllocateFeedbackSlot());
        EmitStarRegister(stepValueReg);
        var assignCatchLabel = builder.CreateLabel();
        var assignDoneLabel = builder.CreateLabel();
        builder.EmitJump(JsOpCode.PushTry, assignCatchLabel);
        EmitLdaRegister(stepValueReg);
        EmitForIterationAssignLeft(stmt.Left, true);
        EmitRaw(JsOpCode.PopTry);
        EmitJump(assignDoneLabel);
        builder.BindLabel(assignCatchLabel);
        EmitPreserveAccumulator(() =>
            EmitCallRuntime(RuntimeId.DestructureIteratorCloseBestEffort, iterReg, 1));
        EmitThrowConsideringFinallyFlow();
        builder.BindLabel(assignDoneLabel);

        loopTargets.Push(new(loopBreakLabel, continueLabel));
        breakTargets.Push(loopBreakLabel);
        activeForOfIteratorLoops.Push(new(
            loopBreakLabel,
            continueLabel,
            iterReg,
            activeCatchableTryDepth));
        if (labels is not null && labels.Count != 0)
            PushLabeledTargets(labels, loopBreakLabel, continueLabel, true);
        try
        {
            if (TryGetUsingLikeForInOfLeft(stmt, out var usingLikeDeclaration))
            {
                EmitExplicitResourceScope(
                    () => VisitLoopBodyWithCompletion(stmt.Body, completionReg),
                    usingLikeDeclaration.Kind == JsVariableDeclarationKind.AwaitUsing,
                    _ => EmitRegisterExplicitResource(usingLikeDeclaration.Kind, stepValueReg));
            }
            else
            {
                VisitLoopBodyWithCompletion(stmt.Body, completionReg);
            }
        }
        finally
        {
            if (labels is not null && labels.Count != 0)
                PopLabeledTargets(labels.Count);
            activeForOfIteratorLoops.Pop();
            breakTargets.Pop();
            loopTargets.Pop();
        }

        builder.BindLabel(continueLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        if (needsPerIterationContext)
            EmitRotatePerIterationContext();
        EmitJump(loopLabel);
        builder.BindLabel(loopBreakLabel);
        EmitStoreLoopCompletionValueIfNeeded(completionReg);
        EmitJump(doneLabel);
    }

    private bool ShouldUsePerIterationContextForForInOfLoop(JsForInOfStatement stmt)
    {
        if (currentContextSlotById.Count == 0)
            return false;
        if (stmt.Left is not JsVariableDeclarationStatement declStmt ||
            !declStmt.Kind.IsLexical())
            return false;

        foreach (var boundName in GetForInOfHeadBoundIdentifiers(stmt))
            if (TryResolveLocalBinding(boundName.Name, out var resolved) &&
                IsCapturedByChildBinding(resolved.SymbolId))
                return true;

        return false;
    }


    private void EmitForIterationAssignLeft(JsNode left, bool isLoopInitialization)
    {
        switch (left)
        {
            case JsVariableDeclarationStatement declStmt:
            {
                if (declStmt.BindingPattern is not null)
                {
                    EmitStoreOrDestructureAssignmentTarget(
                        declStmt.BindingPattern,
                        isLoopInitialization);
                    break;
                }

                if (declStmt.Declarators.Count != 1)
                    throw new NotSupportedException("iteration declaration must have a single declarator.");
                var decl = declStmt.Declarators[0];
                if (decl.Initializer is not null)
                    throw new NotSupportedException("iteration declaration initializer is not supported.");
                var useInitializationStore = isLoopInitialization && declStmt.Kind is not JsVariableDeclarationKind.Var;
                StoreIdentifier(TryResolveLocalBinding(decl.Name, out var resolvedDecl) ? resolvedDecl.Name : decl.Name,
                    useInitializationStore,
                    decl.Name);
            }
                break;
            case JsIdentifierExpression id:
                // Bare identifier in for-in/of/await assignment is a normal PutValue, not declaration initialization.
                StoreIdentifier(TryResolveLocalBinding(id.Name, out var resolvedId) ? resolvedId.Name : id.Name, false,
                    id.Name);
                break;
            case JsExpression expr:
                EmitStoreOrDestructureAssignmentTargetPreservingValue(expr);
                break;
            default:
                throw new NotSupportedException("iteration left side currently supports identifier/declaration only.");
        }
    }

    private void EmitDeleteExpression(JsExpression argument)
    {
        if (TryGetDeletePrivateIdentifier(argument, out var privateName, out var privatePosition))
            throw new JsParseException($"Unexpected identifier '{privateName}'", privatePosition, CurrentSourceText);

        if (argument is not JsMemberExpression member)
        {
            if (argument is JsIdentifierExpression identifier)
            {
                var binding = ResolveIdentifierReadBinding(CompilerIdentifierName.From(identifier));
                if (binding.Kind != IdentifierReadBindingKind.Global)
                {
                    EmitLdaFalse();
                    return;
                }

                var tempScopeIdentifierDelete = BeginTemporaryRegisterScope();
                var deleteTargetReg = AllocateTemporaryRegisterBlock(2);
                var deleteKeyReg = deleteTargetReg + 1;
                try
                {
                    var globalThisNameIdx = builder.AddAtomizedStringConstant(AtomTable.IdGlobalThis);
                    EmitLdaGlobalByIndex(globalThisNameIdx,
                        builder.GetOrAllocateGlobalBindingFeedbackSlot("globalThis"));
                    EmitStarRegister(deleteTargetReg);
                    var nameIdx = builder.AddObjectConstant(identifier.Name);
                    EmitLdaStringConstantByIndex(nameIdx);
                    EmitStarRegister(deleteKeyReg);
                    EmitRaw(
                        JsOpCode.CallRuntime,
                        (byte)(strictDeclared ? RuntimeId.DeleteKeyedPropertyStrict : RuntimeId.DeleteKeyedProperty),
                        (byte)deleteTargetReg,
                        2);
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScopeIdentifierDelete);
                }

                return;
            }

            VisitExpression(argument);
            // Non-reference delete is true after evaluating the operand.
            EmitLdaTrue();
            return;
        }

        if (member.Object is JsSuperExpression)
        {
            if (member.IsPrivate)
                throw new JsParseException("Unexpected private field", member.Position, CurrentSourceText);

            var superDeleteScope = BeginTemporaryRegisterScope();
            var superDeleteReg = AllocateTemporaryRegister();
            try
            {
                // Evaluate GetThisBinding first; this throws before key expression when uninitialized.
                builder.EmitLda(JsOpCode.LdaThis);
                EmitStarRegister(superDeleteReg);

                // delete super[...] evaluates the key expression but must not perform ToPropertyKey.
                if (member.IsComputed)
                    VisitExpression(member.Property);

                EmitCallRuntime(RuntimeId.ThrowDeleteSuperPropertyReference, 0, 0);
            }
            finally
            {
                EndTemporaryRegisterScope(superDeleteScope);
            }

            return;
        }

        var tempScope = BeginTemporaryRegisterScope();
        var targetReg = AllocateTemporaryRegisterBlock(2);
        var keyReg = targetReg + 1;
        try
        {
            VisitExpression(member.Object);
            EmitStarRegister(targetReg);

            if (member.IsComputed)
            {
                VisitExpression(member.Property);
            }
            else
            {
                if (!TryGetNamedMemberKey(member, out var memberName))
                    throw new NotImplementedException("Only non-private member delete is supported in Okojo Phase 1.");
                var nameIdx = builder.AddObjectConstant(memberName);
                EmitLdaStringConstantByIndex(nameIdx);
            }

            EmitStarRegister(keyReg);
            EmitRaw(
                JsOpCode.CallRuntime,
                (byte)(strictDeclared ? RuntimeId.DeleteKeyedPropertyStrict : RuntimeId.DeleteKeyedProperty),
                (byte)targetReg,
                2);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private static bool TryGetDeletePrivateIdentifier(JsExpression expr, out string name, out int position)
    {
        switch (expr)
        {
            case JsPrivateIdentifierExpression p:
                name = p.Name;
                position = p.Position;
                return true;
            case JsBinaryExpression { Operator: JsBinaryOperator.In, Left: JsPrivateIdentifierExpression p }:
                name = p.Name;
                position = p.Position;
                return true;
            case JsMemberExpression { IsPrivate: true, Property: JsLiteralExpression { Value: string s } } m:
                name = s;
                position = m.Position;
                return true;
            default:
                name = string.Empty;
                position = 0;
                return false;
        }
    }

    private void StoreIdentifier(string name, bool isInitialization = false, string? sourceNameForDebug = null)
    {
        if (ShouldUseFunctionArgumentsBinding(sourceNameForDebug ?? name))
        {
            var syntheticArgumentsReg = EnsureSyntheticArgumentsRegister();
            EmitStarRegister(syntheticArgumentsReg);
            return;
        }

        var sourceName = sourceNameForDebug ?? name;
        var resolvedName = ResolveLocalAlias(name);
        if (!isInitialization && IsImmutableFunctionNameBinding(resolvedName))
        {
            if (strictDeclared)
                EmitThrowConstAssignErrorRuntime(sourceName);
            return;
        }

        _ = TryResolveIdentifierStoreBinding(resolvedName, sourceName, out var binding);
        if (binding.Kind == IdentifierStoreBindingKind.ModuleVariable)
        {
            if (!isInitialization && binding.IsModuleReadOnly)
            {
                EmitThrowConstAssignErrorRuntime(sourceName);
                return;
            }

            EmitRaw(JsOpCode.StaModuleVariable, unchecked((byte)binding.Slot), (byte)binding.Depth);
        }
        else if (binding.Kind == IdentifierStoreBindingKind.CurrentLocal)
        {
            if (binding.Slot >= 0)
            {
                if (!isInitialization && binding.IsLexical)
                {
                    var valueReg = AllocateTemporaryRegister();
                    EmitStarRegister(valueReg);
                    var readPc = builder.CodeLength;
                    EmitLdaCurrentContextSlot(binding.Slot);
                    builder.AddTdzReadDebugName(readPc, sourceName);
                    if (binding.IsImmutableFunctionName)
                    {
                        EmitLdaRegister(valueReg);
                        ReleaseTemporaryRegister(valueReg);
                        if (strictDeclared)
                            EmitThrowConstAssignErrorRuntime(sourceName);
                        return;
                    }

                    if (binding.IsConst)
                    {
                        ReleaseTemporaryRegister(valueReg);
                        EmitThrowConstAssignErrorRuntime(sourceName);
                        return;
                    }

                    EmitLdaRegister(valueReg);
                    EmitStaCurrentContextSlot(binding.Slot);
                    _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
                    ReleaseTemporaryRegister(valueReg);
                    return;
                }

                if (!isInitialization && binding.IsImmutableFunctionName)
                {
                    if (strictDeclared)
                        EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                if (!isInitialization && binding.IsConst)
                {
                    EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                EmitStaCurrentContextSlot(binding.Slot);
                _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
            }
            else if (!isInitialization && binding.IsLexicalRegisterLocal)
            {
                if (IsKnownInitializedLexical(resolvedName))
                {
                    if (binding.IsImmutableFunctionName)
                    {
                        if (strictDeclared)
                            EmitThrowConstAssignErrorRuntime(sourceName);
                        return;
                    }

                    if (binding.IsConst)
                    {
                        EmitThrowConstAssignErrorRuntime(sourceName);
                        return;
                    }

                    EmitStarRegister(binding.Register);
                    _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
                }
                else
                {
                    var writePc = builder.CodeLength;
                    EmitStoreLexicalLocal(binding.Register);
                    builder.AddTdzReadDebugName(writePc, sourceName);
                    _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
                }
            }
            else
            {
                if (!isInitialization && binding.IsImmutableFunctionName)
                {
                    if (strictDeclared)
                        EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                if (!isInitialization && binding.IsConst)
                {
                    EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                EmitStarRegister(binding.Register);
                _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
            }

            if (isInitialization && binding.IsLexicalRegisterLocal && ShouldTrackKnownInitializedLexical(resolvedName))
                MarkKnownInitializedLexical(resolvedName);
        }
        else if (binding.Kind == IdentifierStoreBindingKind.CapturedContext)
        {
            if (!isInitialization && binding.IsLexical)
            {
                var valueReg = AllocateTemporaryRegister();
                EmitStarRegister(valueReg);
                var readPc = builder.CodeLength;
                EmitLdaContextSlot(0, binding.Slot, binding.Depth);
                builder.AddTdzReadDebugName(readPc, sourceName);
                if (binding.IsImmutableFunctionName)
                {
                    EmitLdaRegister(valueReg);
                    ReleaseTemporaryRegister(valueReg);
                    if (strictDeclared)
                        EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                if (binding.IsConst)
                {
                    ReleaseTemporaryRegister(valueReg);
                    EmitThrowConstAssignErrorRuntime(sourceName);
                    return;
                }

                EmitLdaRegister(valueReg);
                if (binding.Depth == 0)
                    EmitStaCurrentContextSlot(binding.Slot);
                else
                    EmitStaContextSlot(0, binding.Slot, binding.Depth);
                _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
                ReleaseTemporaryRegister(valueReg);
                requiresClosureBinding = true;
                return;
            }

            if (!isInitialization && binding.IsImmutableFunctionName)
            {
                if (strictDeclared)
                    EmitThrowConstAssignErrorRuntime(sourceName);
                return;
            }

            if (!isInitialization && binding.IsConst)
            {
                EmitThrowConstAssignErrorRuntime(sourceName);
                return;
            }

            if (binding.Depth == 0)
                EmitStaCurrentContextSlot(binding.Slot);
            else
                EmitStaContextSlot(0, binding.Slot, binding.Depth);
            _ = TryEmitMirrorCurrentLocalStoreToModuleExport(resolvedName);
            requiresClosureBinding = true;
        }
        else
        {
            if (!isInitialization && IsReplTopLevelConstName(sourceName))
            {
                EmitThrowConstAssignErrorRuntime(sourceName);
                return;
            }

            var nameIdx = builder.AddAtomizedStringConstant(sourceName);
            EmitStaGlobalByIndex(nameIdx, builder.GetOrAllocateGlobalBindingFeedbackSlot(sourceName), isInitialization);
        }
    }

    private void StoreIdentifier(CompilerIdentifierName identifier, bool isInitialization = false)
    {
        StoreIdentifier(identifier.Name, isInitialization, identifier.Name);
    }

    private bool TryEmitArgumentsIdentifierLoad(string identifierName)
    {
        if (!ShouldUseFunctionArgumentsBinding(identifierName))
            return false;

        var syntheticArgumentsReg = EnsureSyntheticArgumentsRegister();
        EmitLdaRegister(syntheticArgumentsReg);
        return true;
    }

    private bool ShouldUseFunctionArgumentsBinding(string identifierName)
    {
        if (!string.Equals(identifierName, "arguments", StringComparison.Ordinal))
            return false;
        if (parent is null)
            return false;
        if (isArrowFunction)
            return false;
        if (functionHasParameterExpressions && emittingParameterInitializers)
            return requiresArgumentsObject;
        if (TryGetSymbolId(identifierName, out var symbolId) &&
            symbolId != SyntheticArgumentsSymbolId &&
            localBindingInfoById.ContainsKey(symbolId))
            return false;
        return requiresArgumentsObject;
    }

    private bool IsCurrentFunctionLocalVisible(string resolvedName, string sourceName)
    {
        if (!functionHasParameterExpressions || !emittingParameterInitializers)
            return true;
        if (IsParameterLocalBinding(resolvedName))
            return true;
        if (IsImmutableFunctionNameBinding(resolvedName))
            return true;
        return ShouldUseFunctionArgumentsBinding(sourceName);
    }

    private bool IsCurrentFunctionLocalVisible(int resolvedSymbolId, string sourceName)
    {
        if (!functionHasParameterExpressions || !emittingParameterInitializers)
            return true;
        if (IsParameterLocalBinding(resolvedSymbolId))
            return true;
        if (IsImmutableFunctionNameBinding(resolvedSymbolId))
            return true;
        return ShouldUseFunctionArgumentsBinding(sourceName);
    }

    private bool ShouldThrowParameterInitializerTdz(CompilerIdentifierName identifier)
    {
        if (!functionHasParameterExpressions || !emittingParameterInitializers)
            return false;
        if (!TryGetResolvedAliasSymbolId(identifier, out var resolvedSymbolId, out _))
            return false;
        if (!IsParameterLocalBinding(resolvedSymbolId))
            return false;
        return !IsInitializedParameterBinding(resolvedSymbolId);
    }

    private bool ShouldThrowParameterInitializerTdz(string sourceName)
    {
        return ShouldThrowParameterInitializerTdz(new CompilerIdentifierName(sourceName));
    }

    private bool IsCurrentFunctionLocalVisibleForCapture(string resolvedName)
    {
        if (!functionHasParameterExpressions || !emittingParameterInitializers)
            return true;
        return IsParameterLocalBinding(resolvedName) ||
               IsImmutableFunctionNameBinding(resolvedName) ||
               string.Equals(resolvedName, SyntheticArgumentsBindingName, StringComparison.Ordinal);
    }

    private bool IsCurrentFunctionLocalVisibleForCapture(int resolvedSymbolId)
    {
        if (!functionHasParameterExpressions || !emittingParameterInitializers)
            return true;
        return IsParameterLocalBinding(resolvedSymbolId) ||
               IsImmutableFunctionNameBinding(resolvedSymbolId) ||
               resolvedSymbolId == SyntheticArgumentsSymbolId;
    }

    private int EnsureSyntheticArgumentsRegister()
    {
        if (syntheticArgumentsRegister >= 0)
            return syntheticArgumentsRegister;

        syntheticArgumentsRegister = GetOrCreateLocal(SyntheticArgumentsSymbolId);
        return syntheticArgumentsRegister;
    }

    private int GetOrCreateLocal(string name)
    {
        return GetOrCreateLocal(GetOrCreateSymbolId(name));
    }

    private int GetOrCreateLocal(CompilerIdentifierName identifier)
    {
        return GetOrCreateLocal(GetOrCreateSymbolId(identifier));
    }

    private int GetOrCreateLocal(int symbolId)
    {
        if (!TryGetLocalRegister(symbolId, out var reg))
        {
            reg = builder.AllocatePinnedRegister();
            locals[symbolId] = reg;
            SetLocalBindingRegister(symbolId, reg);
        }

        localRegisters.Add(reg);
        return reg;
    }


    private void EmitThrowConstAssignErrorRuntime(string name)
    {
        var callPc = builder.CodeLength;
        EmitCallRuntime(RuntimeId.ThrowConstAssignError, 0, 0);
        builder.AddRuntimeCallDebugName(callPc, name);
    }

    private void PredeclareLocals(IEnumerable<JsStatement> statements, bool insideNestedBlock)
    {
        foreach (var stmt in statements)
            switch (stmt)
            {
                case JsVariableDeclarationStatement varStmt:
                    if (IsReplTopLevelMode() && !insideNestedBlock &&
                        varStmt.Kind is not JsVariableDeclarationKind.Var)
                        break;
                    if (insideNestedBlock &&
                        varStmt.Kind.IsLexical())
                        // Nested-block lexicals are declared via block alias prepass to preserve shadowing.
                        break;

                    foreach (var decl in varStmt.Declarators)
                    {
                        if (varStmt.Kind is JsVariableDeclarationKind.Var &&
                            ShouldUseFunctionArgumentsBinding(decl.Name))
                            continue;

                        if (varStmt.Kind is JsVariableDeclarationKind.Var && UsesGlobalScriptBindingsMode())
                        {
                            MarkVarBinding(decl.Name);
                            continue;
                        }

                        if (varStmt.Kind is JsVariableDeclarationKind.Var)
                            GetOrCreateLocal(new CompilerIdentifierName(decl.Name, decl.NameId));
                        else if (TryResolveLocalBinding(decl.Name, out var resolved))
                            GetOrCreateLocal(resolved.SymbolId);
                        else
                            GetOrCreateLocal(decl.Name);
                        if (varStmt.Kind.IsLexical())
                        {
                            MarkLexicalBinding(decl.Name, varStmt.Kind.IsConstLike());
                            if (UsesPersistentGlobalLexicalBindingsMode() && !insideNestedBlock &&
                                !topLevelLexicalDeclarationPositionByName.ContainsKey(decl.Name))
                                topLevelLexicalDeclarationPositionByName[decl.Name] = decl.Position;
                        }

                        if (varStmt.Kind is JsVariableDeclarationKind.Var)
                            MarkVarBinding(new CompilerIdentifierName(decl.Name, decl.NameId).Name);
                    }

                    break;
                case JsBlockStatement block:
                    PredeclareNestedBlockLexicals(block);
                    PredeclareLocals(block.Statements, true);
                    break;
                case JsIfStatement ifStmt:
                    PredeclareLocals([ifStmt.Consequent], insideNestedBlock);
                    if (ifStmt.Alternate != null) PredeclareLocals([ifStmt.Alternate], insideNestedBlock);
                    break;
                case JsWhileStatement whileStmt:
                    PredeclareLocals([whileStmt.Body], insideNestedBlock);
                    break;
                case JsDoWhileStatement doWhileStmt:
                    PredeclareLocals([doWhileStmt.Body], insideNestedBlock);
                    break;
                case JsForStatement forStmt:
                    if (forStmt.Init is JsVariableDeclarationStatement initDecl)
                    {
                        if (initDecl.Kind.IsLexical())
                            PredeclareForHeadLexicals(forStmt);
                        else
                            PredeclareLocals([initDecl], insideNestedBlock);
                    }

                    PredeclareLocals([forStmt.Body], insideNestedBlock);
                    break;
                case JsForInOfStatement forInOfStmt:
                    if (forInOfStmt.Left is JsVariableDeclarationStatement leftDecl)
                    {
                        if (leftDecl.Kind.IsLexical())
                            PredeclareForInOfHeadLexicals(forInOfStmt);
                        else if (leftDecl.BindingPattern is not null)
                            PredeclareVarPatternBindings(leftDecl.BindingPattern);
                        else
                            PredeclareLocals([leftDecl], insideNestedBlock);
                    }

                    PredeclareLocals([forInOfStmt.Body], insideNestedBlock);
                    break;
                case JsFunctionDeclaration functionDeclaration:
                    if (UsesGlobalScriptBindingsMode() && !insideNestedBlock)
                        break;
                    if (insideNestedBlock)
                        break;
                    // Predeclare function name so capture analysis and context-slot
                    // assignment can see it before hoist emission.
                    GetOrCreateLocal(functionDeclaration.Name);
                    break;
                case JsClassDeclaration classDecl:
                    if (IsReplTopLevelMode() && !insideNestedBlock)
                        break;
                    GetOrCreateLocal(classDecl.Name);
                    MarkLexicalBinding(classDecl.Name, false);
                    if (UsesPersistentGlobalLexicalBindingsMode() && !insideNestedBlock &&
                        !topLevelLexicalDeclarationPositionByName.ContainsKey(classDecl.Name))
                        topLevelLexicalDeclarationPositionByName[classDecl.Name] = classDecl.Position;

                    break;
                case JsTryStatement tryStmt:
                    PredeclareLocals([tryStmt.Block], insideNestedBlock);
                    if (tryStmt.Handler is not null)
                    {
                        PredeclareCatchBindings(tryStmt.Handler);

                        PredeclareLocals([tryStmt.Handler.Body], insideNestedBlock);
                    }

                    if (tryStmt.Finalizer is not null)
                        PredeclareLocals([tryStmt.Finalizer], insideNestedBlock);
                    break;
                case JsSwitchStatement sw:
                    PredeclareSwitchLexicals(sw);
                    foreach (var c in sw.Cases)
                        PredeclareLocals(c.Consequent, true);
                    break;
                case JsBreakStatement:
                case JsContinueStatement:
                    break;
                case JsLabeledStatement labeled:
                    PredeclareLocals([labeled.Statement], insideNestedBlock);
                    break;
            }
    }


    private void AssignCurrentContextSlots()
    {
        EnsureDerivedThisContextSlotIfNeeded();

        if (hasSuperReference) EnsureCurrentContextSlotForLocal(SuperBaseSymbolId);

        if (requiresArgumentsObject &&
            currentFunctionParameterPlan is not null &&
            currentFunctionParameterPlan.HasSimpleParameterList &&
            !strictDeclared &&
            !isArrowFunction)
            for (var i = 0; i < currentFunctionParameterPlan.Bindings.Count; i++)
            {
                var parameter = currentFunctionParameterPlan.Bindings[i];
                if (TryResolveLocalBinding(new CompilerIdentifierName(parameter.Name, parameter.NameId),
                        out var resolvedParameter) &&
                    TryGetLocalRegister(resolvedParameter.SymbolId, out _))
                    EnsureCurrentContextSlotForLocal(resolvedParameter.SymbolId);
            }

        if (functionHasParameterExpressions && currentFunctionParameterPlan is not null)
        {
            foreach (var parameter in currentFunctionParameterPlan.Bindings)
            {
                if (TryResolveLocalBinding(new CompilerIdentifierName(parameter.Name, parameter.NameId),
                        out var resolvedParameter) &&
                    TryGetLocalRegister(resolvedParameter.SymbolId, out _))
                    EnsureCurrentContextSlotForLocal(resolvedParameter.SymbolId);

                for (var i = 0; i < parameter.BoundIdentifiers.Count; i++)
                {
                    var boundIdentifier = parameter.BoundIdentifiers[i];
                    if (TryResolveLocalBinding(new CompilerIdentifierName(boundIdentifier.Name, boundIdentifier.NameId),
                            out var resolvedBound) &&
                        TryGetLocalRegister(resolvedBound.SymbolId, out _))
                        EnsureCurrentContextSlotForLocal(resolvedBound.SymbolId);
                }
            }

            foreach (var local in locals.OrderBy(static kvp => kvp.Value))
                if (IsImmutableFunctionNameBinding(local.Key))
                    EnsureCurrentContextSlotForLocal(local.Key);
        }

        if (IsTopLevelScriptMode())
            foreach (var kvp in locals.OrderBy(static kvp => kvp.Value))
                if (IsLexicalLocalBinding(kvp.Key))
                    EnsureCurrentContextSlotForLocal(kvp.Key);

        if (HasCapturedByChildLocals())
            foreach (var symbolId in localBindingInfoById.Keys.Order())
                if (IsCapturedByChildBinding(symbolId))
                    EnsureCurrentContextSlotForLocal(symbolId);

        foreach (var symbolId in forcedAliasContextSlotSymbolIds.Order())
            EnsureCurrentContextSlotForLocal(symbolId);

        EnsureLoopAliasContextSlots();

        if (hasSuperReference && currentContextSlotById.TryGetValue(SuperBaseSymbolId, out var superSlot))
            superBaseContextSlot = superSlot;

        if (derivedThisContextSlot < 0 &&
            currentContextSlotById.TryGetValue(DerivedThisSymbolId, out var derivedThisSlot))
            derivedThisContextSlot = derivedThisSlot;
    }

    private void EnsureLoopAliasContextSlots()
    {
        foreach (var bindings in forHeadLexicalsByPosition.Values)
            EnsureAliasBindingContextSlots(bindings);

        foreach (var bindings in forInOfHeadLexicalsByPosition.Values)
            EnsureAliasBindingContextSlots(bindings);
    }

    private void EnsureAliasBindingContextSlots(IReadOnlyList<BlockLexicalBinding> bindings)
    {
        for (var i = 0; i < bindings.Count; i++)
            EnsureCurrentContextSlotForLocal(bindings[i].InternalSymbolId);
    }

    private void ComputeArgumentsMappedSlots()
    {
        var parameterPlan = currentFunctionParameterPlan;
        if (!requiresArgumentsObject || parameterPlan is null)
            return;

        if (parameterPlan.Bindings.Count == 0)
            return;

        var mappedSlots = new int[parameterPlan.Bindings.Count];
        Array.Fill(mappedSlots, -1);

        for (var i = 0; i < parameterPlan.Bindings.Count; i++)
        {
            var binding = parameterPlan[i];
            var parameterName = binding.Name;
            if (parameterPlan.GetLastBindingIndex(parameterName, binding.NameId) != i)
                continue;
            if (!TryGetCurrentContextSlot(parameterName, out var slot))
                continue;
            mappedSlots[i] = slot;
        }

        compiledArgumentsMappedSlots = mappedSlots;
    }

    private void EmitFunctionContextPrologueIfNeeded()
    {
        if (currentContextSlotById.Count != 0 || forceModuleFunctionContext)
        {
            EmitCreateFunctionContextWithCells(currentContextSlotById.Count);
            foreach (var kvp in currentContextSlotById.OrderBy(static kvp => kvp.Value))
                if (IsLexicalLocalBinding(kvp.Key))
                {
                    EmitLdaTheHole();
                    EmitStaCurrentContextSlot(kvp.Value);
                }
                else if (kvp.Key == DerivedThisSymbolId)
                {
                    EmitLdaTheHole();
                    EmitStaCurrentContextSlot(kvp.Value);
                }
                else if (kvp.Key == SyntheticArgumentsSymbolId)
                {
                    EmitLdaUndefined();
                    EmitStaCurrentContextSlot(kvp.Value);
                }
                else if (IsVarLocalBinding(kvp.Key) && !IsParameterLocalBinding(kvp.Key))
                {
                    EmitLdaUndefined();
                    EmitStaCurrentContextSlot(kvp.Value);
                }

            EmitParameterBindingCanonicalizationPrologue(true);
        }
        else
        {
            EmitParameterBindingCanonicalizationPrologue(false);
        }
    }

    private void EmitParameterBindingCanonicalizationPrologue(bool copyIntoContextSlots)
    {
        if (currentFunctionParameterPlan is null)
            return;

        for (var i = 0; i < currentFunctionParameterPlan.Bindings.Count; i++)
        {
            var parameter = currentFunctionParameterPlan.Bindings[i];
            if (!TryResolveLocalBinding(new CompilerIdentifierName(parameter.Name, parameter.NameId),
                    out var resolvedParameter))
                continue;
            if (!TryGetLocalRegister(resolvedParameter.SymbolId, out var parameterReg))
                continue;

            var hasContextSlot = TryGetCurrentContextSlot(resolvedParameter.SymbolId, out var slot);
            var needsRegisterCopy = parameterReg != i && (!copyIntoContextSlots || !hasContextSlot);

            if (!needsRegisterCopy && (!copyIntoContextSlots || !hasContextSlot))
                continue;

            EmitLdaRegister(i);
            if (needsRegisterCopy)
                EmitStarRegister(parameterReg);
            if (copyIntoContextSlots && hasContextSlot)
                EmitStaCurrentContextSlot(slot);
        }
    }

    private void EmitRegisterLocalsPrologueInitialization()
    {
        foreach (var kvp in locals.OrderBy(static kvp => kvp.Value))
            if (IsLexicalRegisterLocal(kvp.Key))
            {
                if (ShouldSkipLexicalRegisterHoleInit(kvp.Key))
                    continue;
                EmitLdaTheHole();
                EmitStarRegister(kvp.Value);
            }
            else if (IsVarLocalBinding(kvp.Key) && !IsParameterLocalBinding(kvp.Key))
            {
                EmitLdaUndefined();
                EmitStarRegister(kvp.Value);
            }
    }

    private void EmitSuperBaseContextSlotInitIfNeeded()
    {
        if (superBaseContextSlot < 0)
            return;

        if (useMethodEnvironmentCapture)
        {
            var depth = currentContextSlotById.Count == 0 && !forceModuleFunctionContext ? 0 : 1;
            builder.EmitLda(JsOpCode.LdaContextSlot, 0, (byte)depth);
            EmitCallRuntime(RuntimeId.GetObjectPrototypeForSuper, 0, 0);
        }
        else
        {
            EmitCallRuntime(RuntimeId.GetCurrentFunctionSuperBase, 0, 0);
        }

        EmitStaCurrentContextSlot(superBaseContextSlot);
    }

    private void ComputeSafeLexicalRegisterPrologueHoleInitSkips(IReadOnlyList<JsStatement> statements)
    {
        skipLexicalRegisterPrologueHoleInit.Clear();

        var prefixStatements = Vm.RentCompileList<JsStatement>(8);
        try
        {
            foreach (var stmt in statements)
            {
                if (stmt is JsFunctionDeclaration) continue; // hoisted

                if (!TryGetSafePrefixLexicalDeclBindingToSkip(stmt, out var sourceName, out var resolvedBinding))
                    break;

                var referencedEarlier = false;
                for (var i = 0; i < prefixStatements.Count; i++)
                    if (StatementReferencesIdentifier(prefixStatements[i], sourceName))
                    {
                        referencedEarlier = true;
                        break;
                    }

                if (!referencedEarlier)
                    MarkSkipLexicalRegisterHoleInit(resolvedBinding.SymbolId);
                prefixStatements.Add(stmt);
            }

            foreach (var stmt in statements)
                if (stmt is JsForStatement forStmt &&
                    forStmt.Init is JsVariableDeclarationStatement initDecl &&
                    TryGetSafeSingleLexicalInitDeclarator(initDecl, out var resolvedBinding))
                    MarkSkipLexicalRegisterHoleInit(resolvedBinding.SymbolId);
        }
        finally
        {
            Vm.ReturnCompileList(prefixStatements);
        }
    }

    private bool TryGetSafePrefixLexicalDeclBindingToSkip(JsStatement stmt, out string sourceName,
        out ResolvedLocalBinding resolvedBinding)
    {
        sourceName = string.Empty;
        resolvedBinding = default;

        if (stmt is not JsVariableDeclarationStatement declStmt) return false;
        if (!declStmt.Kind.IsLexical()) return false;
        if (declStmt.Declarators.Count != 1) return false;

        var decl = declStmt.Declarators[0];
        if (decl.Initializer is null) return false;
        if (ExpressionReferencesIdentifier(decl.Initializer, decl.Name)) return false;

        sourceName = decl.Name;
        if (!TryResolveLocalBinding(decl.Name, out resolvedBinding))
            return false;
        if (!IsLexicalRegisterLocal(resolvedBinding.SymbolId)) return false;

        return true;
    }

    private bool TryGetSafeSingleLexicalInitDeclarator(JsVariableDeclarationStatement declStmt,
        out ResolvedLocalBinding resolvedBinding)
    {
        resolvedBinding = default;

        if (!declStmt.Kind.IsLexical()) return false;
        if (declStmt.Declarators.Count != 1) return false;

        var decl = declStmt.Declarators[0];
        if (decl.Initializer is null) return false;
        if (ExpressionReferencesIdentifier(decl.Initializer, decl.Name)) return false;

        if (!TryResolveLocalBinding(decl.Name, out resolvedBinding))
            return false;
        if (!IsLexicalRegisterLocal(resolvedBinding.SymbolId)) return false;
        return true;
    }

    private bool IsLexicalRegisterLocal(string name)
    {
        return IsLexicalLocalBinding(name) && !TryGetCurrentContextSlot(name, out _);
    }

    private bool IsLexicalRegisterLocal(int symbolId)
    {
        return IsLexicalLocalBinding(symbolId) && !TryGetCurrentContextSlot(symbolId, out _);
    }

    private bool ShouldTrackKnownInitializedLexical(string name)
    {
        return IsLexicalRegisterLocal(name) && !IsSwitchLexicalInternal(name);
    }

    private bool ShouldTrackKnownInitializedLexical(int symbolId)
    {
        return IsLexicalRegisterLocal(symbolId) && !IsSwitchLexicalInternal(symbolId);
    }

    private string ResolveLocalAlias(CompilerIdentifierName identifier)
    {
        foreach (var scope in activeBlockLexicalAliases)
        {
            var bindings = scope.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (!binding.Matches(identifier))
                    continue;

                if (scope.Inherited)
                {
                    if (identifier.NameId >= 0)
                    {
                        var sourceSymbolId = CompilerSymbolId.FromSourceIdentifier(identifier.NameId).Value;
                        if (locals.ContainsKey(sourceSymbolId))
                            return identifier.Name;
                    }
                    else if (TryGetSymbolId(identifier.Name, out var directSymbolId) &&
                             locals.ContainsKey(directSymbolId))
                    {
                        return identifier.Name;
                    }
                }

                return binding.InternalName;
            }
        }

        return identifier.Name;
    }

    private string ResolveLocalAlias(string sourceName)
    {
        foreach (var scope in activeBlockLexicalAliases)
        {
            var bindings = scope.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (!binding.Matches(sourceName))
                    continue;

                if (scope.Inherited &&
                    TryGetSymbolId(sourceName, out var sourceSymbolId) &&
                    locals.ContainsKey(sourceSymbolId))
                    return sourceName;

                return binding.InternalName;
            }
        }

        return sourceName;
    }

    private void PredeclareNestedBlockLexicals(JsBlockStatement block)
    {
        if (nestedBlockLexicals.ContainsKey(block.Position)) return;

        var bindings = Vm.RentCompileList<BlockLexicalBinding>(4);
        foreach (var stmt in block.Statements)
            if (stmt is JsVariableDeclarationStatement declStmt)
            {
                if (!declStmt.Kind.IsLexical())
                    continue;

                foreach (var decl in declStmt.Declarators)
                {
                    var internalName = $"{decl.Name}#b{blockLexicalUniqueId++}";
                    var internalSymbolId = GetOrCreateSymbolId(internalName);
                    MarkLexicalBinding(internalSymbolId, declStmt.Kind.IsConstLike());
                    bindings.Add(new(decl.Name, decl.NameId, internalName, internalSymbolId,
                        declStmt.Kind.IsConstLike()));
                }
            }
            else if (stmt is JsFunctionDeclaration functionDecl)
            {
                var internalName = $"{functionDecl.Name}#b{blockLexicalUniqueId++}";
                var internalSymbolId = GetOrCreateSymbolId(internalName);
                MarkLexicalBinding(internalSymbolId, false);
                bindings.Add(new(functionDecl.Name, functionDecl.NameId, internalName, internalSymbolId,
                    false));
            }

        if (bindings.Count > 0)
            nestedBlockLexicals[block.Position] = bindings;
        else
            Vm.ReturnCompileList(bindings);
    }

    private void PredeclareForHeadLexicals(JsForStatement forStmt)
    {
        if (forHeadLexicalsByPosition.ContainsKey(forStmt.Position))
            return;
        if (forStmt.Init is not JsVariableDeclarationStatement declStmt ||
            !declStmt.Kind.IsLexical())
            return;

        var bindings = Vm.RentCompileList<BlockLexicalBinding>(Math.Max(1, declStmt.Declarators.Count));
        foreach (var decl in declStmt.Declarators)
        {
            var internalName = $"{decl.Name}#f{blockLexicalUniqueId++}";
            var internalSymbolId = GetOrCreateSymbolId(internalName);
            var isConst = declStmt.Kind.IsConstLike();
            MarkLexicalBinding(internalSymbolId, isConst);
            bindings.Add(new(decl.Name, decl.NameId, internalName, internalSymbolId, isConst));
        }

        if (bindings.Count > 0)
            forHeadLexicalsByPosition[forStmt.Position] = bindings;
        else
            Vm.ReturnCompileList(bindings);
    }

    private void PredeclareForInOfHeadLexicals(JsForInOfStatement forInOfStmt)
    {
        if (forInOfHeadLexicalsByPosition.ContainsKey(forInOfStmt.Position))
            return;
        if (forInOfStmt.Left is not JsVariableDeclarationStatement declStmt ||
            !declStmt.Kind.IsLexical())
            return;

        var boundIdentifiers = GetForInOfHeadBoundIdentifiers(forInOfStmt);
        var bindings = Vm.RentCompileList<BlockLexicalBinding>(Math.Max(1, boundIdentifiers.Count));
        foreach (var boundIdentifier in boundIdentifiers)
        {
            var internalName = $"{boundIdentifier.Name}#i{blockLexicalUniqueId++}";
            var internalSymbolId = GetOrCreateSymbolId(internalName);
            var isConst = declStmt.Kind.IsConstLike();
            MarkLexicalBinding(internalSymbolId, isConst);
            bindings.Add(new(boundIdentifier.Name, boundIdentifier.NameId, internalName, internalSymbolId, isConst));
        }

        if (bindings.Count > 0)
            forInOfHeadLexicalsByPosition[forInOfStmt.Position] = bindings;
        else
            Vm.ReturnCompileList(bindings);

        var tdzBindings = Vm.RentCompileList<BlockLexicalBinding>(Math.Max(1, boundIdentifiers.Count));
        foreach (var boundIdentifier in boundIdentifiers)
        {
            var internalName = $"{boundIdentifier.Name}#t{blockLexicalUniqueId++}";
            var internalSymbolId = GetOrCreateSymbolId(internalName);
            var isConst = declStmt.Kind.IsConstLike();
            MarkLexicalBinding(internalSymbolId, isConst);
            tdzBindings.Add(new(boundIdentifier.Name, boundIdentifier.NameId, internalName, internalSymbolId, isConst));
        }

        if (tdzBindings.Count > 0)
            forInOfHeadTdzLexicalsByPosition[forInOfStmt.Position] = tdzBindings;
        else
            Vm.ReturnCompileList(tdzBindings);
    }

    private void PredeclareSwitchLexicals(JsSwitchStatement switchStmt)
    {
        if (switchLexicalsByPosition.ContainsKey(switchStmt.Position))
            return;

        var bindings = Vm.RentCompileList<BlockLexicalBinding>(4);
        var seen = Vm.RentCompileHashSet<string>(16, StringComparer.Ordinal);
        try
        {
            foreach (var c in switchStmt.Cases)
            foreach (var stmt in c.Consequent)
                if (stmt is JsVariableDeclarationStatement declStmt &&
                    declStmt.Kind.IsLexical())
                {
                    foreach (var decl in declStmt.Declarators)
                    {
                        if (!seen.Add(decl.Name))
                            throw new JsParseException($"Identifier '{decl.Name}' has already been declared",
                                stmt.Position, CurrentSourceText);

                        var internalName = $"{decl.Name}#s{blockLexicalUniqueId++}";
                        var internalSymbolId = GetOrCreateSymbolId(internalName);
                        MarkLexicalBinding(internalSymbolId, declStmt.Kind.IsConstLike());
                        MarkSwitchLexicalInternal(internalSymbolId);
                        bindings.Add(new(decl.Name, decl.NameId, internalName, internalSymbolId,
                            declStmt.Kind.IsConstLike()));
                    }
                }
                else if (stmt is JsClassDeclaration classDecl)
                {
                    if (!seen.Add(classDecl.Name))
                        throw new JsParseException($"Identifier '{classDecl.Name}' has already been declared",
                            stmt.Position, CurrentSourceText);

                    var internalName = $"{classDecl.Name}#s{blockLexicalUniqueId++}";
                    var internalSymbolId = GetOrCreateSymbolId(internalName);
                    MarkLexicalBinding(internalSymbolId, false);
                    MarkSwitchLexicalInternal(internalSymbolId);
                    bindings.Add(new(classDecl.Name, classDecl.NameId, internalName, internalSymbolId,
                        false));
                }

            if (bindings.Count > 0)
                switchLexicalsByPosition[switchStmt.Position] = bindings;
            else
                Vm.ReturnCompileList(bindings);
        }
        finally
        {
            Vm.ReturnCompileHashSet(seen);
        }
    }

    private void PushBlockLexicalAliases(JsBlockStatement block)
    {
        if (!nestedBlockLexicals.TryGetValue(block.Position, out var bindings) || bindings.Count == 0)
            return;

        var scope = new LexicalAliasScope(bindings, false, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PopBlockLexicalAliases(JsBlockStatement block)
    {
        if (nestedBlockLexicals.TryGetValue(block.Position, out var bindings) && bindings.Count > 0)
        {
            var aliasScope = activeBlockLexicalAliases.Peek();
            var startPc = PopLocalDebugScopeStart();
            EmitLocalDebugInfos(bindings, startPc, builder.CodeLength);
            DeactivateLexicalAliasScope(aliasScope);
            _ = activeBlockLexicalAliases.Pop();
        }
    }

    private void PushSwitchLexicalAliases(JsSwitchStatement switchStmt)
    {
        if (!switchLexicalsByPosition.TryGetValue(switchStmt.Position, out var bindings) || bindings.Count == 0)
            return;

        var scope = new LexicalAliasScope(bindings, false, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PopSwitchLexicalAliases(JsSwitchStatement switchStmt)
    {
        if (switchLexicalsByPosition.TryGetValue(switchStmt.Position, out var bindings) && bindings.Count > 0)
        {
            var aliasScope = activeBlockLexicalAliases.Peek();
            var startPc = PopLocalDebugScopeStart();
            EmitLocalDebugInfos(bindings, startPc, builder.CodeLength);
            DeactivateLexicalAliasScope(aliasScope);
            _ = activeBlockLexicalAliases.Pop();
        }
    }

    private void PushForHeadLexicalAliases(JsForStatement forStmt)
    {
        if (!forHeadLexicalsByPosition.TryGetValue(forStmt.Position, out var bindings) || bindings.Count == 0)
            return;

        var scope = new LexicalAliasScope(bindings, false, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PopForHeadLexicalAliases(JsForStatement forStmt)
    {
        if (forHeadLexicalsByPosition.TryGetValue(forStmt.Position, out var bindings) && bindings.Count > 0)
        {
            var aliasScope = activeBlockLexicalAliases.Peek();
            var startPc = PopLocalDebugScopeStart();
            EmitLocalDebugInfos(bindings, startPc, builder.CodeLength);
            DeactivateLexicalAliasScope(aliasScope);
            _ = activeBlockLexicalAliases.Pop();
        }
    }

    private void PushForInOfHeadLexicalAliases(JsForInOfStatement forInOfStmt)
    {
        if (!forInOfHeadLexicalsByPosition.TryGetValue(forInOfStmt.Position, out var bindings) || bindings.Count == 0)
            return;

        var scope = new LexicalAliasScope(bindings, false, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PopForInOfHeadLexicalAliases(JsForInOfStatement forInOfStmt)
    {
        if (forInOfHeadLexicalsByPosition.TryGetValue(forInOfStmt.Position, out var bindings) && bindings.Count > 0)
        {
            var aliasScope = activeBlockLexicalAliases.Peek();
            var startPc = PopLocalDebugScopeStart();
            EmitLocalDebugInfos(bindings, startPc, builder.CodeLength);
            DeactivateLexicalAliasScope(aliasScope);
            _ = activeBlockLexicalAliases.Pop();
        }
    }

    private void PushForInOfHeadTdzLexicalAliases(JsForInOfStatement forInOfStmt)
    {
        if (!forInOfHeadTdzLexicalsByPosition.TryGetValue(forInOfStmt.Position, out var bindings) ||
            bindings.Count == 0)
            return;

        var scope = new LexicalAliasScope(bindings, false, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PopForInOfHeadTdzLexicalAliases(JsForInOfStatement forInOfStmt)
    {
        if (forInOfHeadTdzLexicalsByPosition.TryGetValue(forInOfStmt.Position, out var bindings) && bindings.Count > 0)
        {
            var aliasScope = activeBlockLexicalAliases.Peek();
            var startPc = PopLocalDebugScopeStart();
            EmitLocalDebugInfos(bindings, startPc, builder.CodeLength);
            DeactivateLexicalAliasScope(aliasScope);
            _ = activeBlockLexicalAliases.Pop();
        }
    }

    private List<BoundIdentifier> GetForInOfHeadBoundIdentifiers(JsForInOfStatement forInOfStmt)
    {
        var identifiers = new List<BoundIdentifier>(4);
        if (forInOfStmt.Left is not JsVariableDeclarationStatement declStmt)
            return identifiers;

        if (declStmt.BindingPattern is not null)
        {
            CollectPatternBoundIdentifiers(declStmt.BindingPattern, identifiers);
            return identifiers;
        }

        if (declStmt.Declarators.Count == 1 &&
            declStmt.Declarators[0].Name.StartsWith("$forpat_", StringComparison.Ordinal) &&
            forInOfStmt.Body is JsBlockStatement bodyBlock)
            foreach (var statement in bodyBlock.Statements)
                switch (statement)
                {
                    case JsVariableDeclarationStatement bodyDecl:
                        foreach (var declarator in bodyDecl.Declarators)
                            identifiers.Add(new(declarator.Name, declarator.NameId));
                        continue;
                    case JsEmptyObjectBindingDeclarationStatement:
                        continue;
                    default:
                        return identifiers;
                }
        else
            for (var i = 0; i < declStmt.Declarators.Count; i++)
            {
                var decl = declStmt.Declarators[i];
                identifiers.Add(new(decl.Name, decl.NameId));
            }

        return identifiers;
    }

    private static void CollectPatternBoundIdentifiers(JsExpression pattern, List<BoundIdentifier> identifiers)
    {
        switch (pattern)
        {
            case JsIdentifierExpression id:
                identifiers.Add(new(id.Name, id.NameId));
                return;
            case JsSpreadExpression spread:
                CollectPatternBoundIdentifiers(spread.Argument, identifiers);
                return;
            case JsArrayExpression arrayPattern:
                for (var i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    var element = arrayPattern.Elements[i];
                    if (element is not null)
                        CollectPatternBoundIdentifiers(element, identifiers);
                }

                return;
            case JsObjectExpression objectPattern:
                for (var i = 0; i < objectPattern.Properties.Count; i++)
                    CollectPatternBoundIdentifiers(objectPattern.Properties[i].Value, identifiers);
                return;
            case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: var left }:
                CollectPatternBoundIdentifiers(left, identifiers);
                return;
        }
    }

    private void PredeclareVarPatternBindings(JsExpression pattern)
    {
        var identifiers = new List<BoundIdentifier>(4);
        CollectPatternBoundIdentifiers(pattern, identifiers);
        foreach (var identifier in identifiers)
            if (TryResolveLocalBinding(new CompilerIdentifierName(identifier.Name, identifier.NameId),
                    out var resolved))
            {
                GetOrCreateLocal(resolved.SymbolId);
                MarkVarBinding(resolved.SymbolId);
            }
            else
            {
                GetOrCreateLocal(new CompilerIdentifierName(identifier.Name, identifier.NameId));
                MarkVarBinding(identifier.Name);
            }
    }

    private string GetCatchBindingInternalName(string sourceName, int catchClausePosition, int ordinal)
    {
        return $"{sourceName}#c{catchClausePosition}_{ordinal}";
    }

    private void PushLocalDebugScope()
    {
        activeLocalDebugScopeStarts.Push(builder.CodeLength);
    }

    private int PopLocalDebugScopeStart()
    {
        return activeLocalDebugScopeStarts.Pop();
    }

    private void ActivateLexicalAliasScope(LexicalAliasScope scope)
    {
        if (!scope.Inherited)
            RecordCurrentContextPerIterationBaseDepth(scope.Bindings);

        if (!enableAliasScopeRegisterAllocation || scope.Inherited || scope.RuntimeAllocatedSymbolIds is not null)
            return;

        var allocated = Vm.RentCompileList<int>(scope.Bindings.Count);
        scope.RuntimeAllocatedSymbolIds = allocated;
        for (var i = 0; i < scope.Bindings.Count; i++)
        {
            var symbolId = scope.Bindings[i].InternalSymbolId;
            if (locals.ContainsKey(symbolId))
                continue;

            var reg = AllocateTemporaryRegister();
            locals[symbolId] = reg;
            SetLocalBindingRegister(symbolId, reg);
            allocated.Add(symbolId);

            if (TryGetCurrentContextSlot(symbolId, out var slot))
            {
                if (IsLexicalLocalBinding(symbolId))
                {
                    EmitLdaTheHole();
                    EmitStarRegister(reg);
                }
                else if (IsVarLocalBinding(symbolId) && !IsParameterLocalBinding(symbolId))
                {
                    EmitLdaUndefined();
                    EmitStarRegister(reg);
                }
            }
            else if (IsLexicalLocalBinding(symbolId))
            {
                EmitLdaTheHole();
                EmitStarRegister(reg);
            }
            else if (IsVarLocalBinding(symbolId) && !IsParameterLocalBinding(symbolId))
            {
                EmitLdaUndefined();
                EmitStarRegister(reg);
            }
        }
    }

    private void DeactivateLexicalAliasScope(LexicalAliasScope scope)
    {
        if (scope.RuntimeAllocatedSymbolIds is not List<int> allocated)
            return;

        for (var i = allocated.Count - 1; i >= 0; i--)
        {
            var symbolId = allocated[i];
            if (!locals.Remove(symbolId, out var reg))
                continue;

            SetLocalBindingRegister(symbolId, -1);
            ReleaseTemporaryRegister(reg);
        }

        allocated.Clear();
        Vm.ReturnCompileList(allocated);
        scope.RuntimeAllocatedSymbolIds = null;
    }

    private void EmitLocalDebugInfos(IReadOnlyList<BlockLexicalBinding> bindings, int startPc, int endPc)
    {
        if (bindings.Count == 0 || endPc <= startPc)
            return;

        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (!TryEmitLocalDebugInfo(binding.SourceName, binding.InternalSymbolId, binding.IsConst, startPc, endPc))
                continue;
        }
    }

    private void EmitRootLocalDebugInfos()
    {
        var startPc = 0;
        var endPc = builder.CodeLength;
        if (endPc <= startPc)
            return;

        foreach (var kvp in locals.OrderBy(static kvp => kvp.Value))
        {
            if (!TryGetDebuggerVisibleLocalName(kvp.Key, out var name))
                continue;
            if (!TryEmitLocalDebugInfo(name, kvp.Key, null, startPc, endPc))
                continue;
        }
    }

    private bool TryEmitLocalDebugInfo(string sourceName, int symbolId, bool? isConstOverride, int startPc, int endPc)
    {
        if (endPc <= startPc)
            return false;
        if (!TryGetLocalDebugStorage(symbolId, out var storageKind, out var storageIndex))
            return false;

        var flags = GetLocalDebugFlags(symbolId, isConstOverride);
        builder.AddLocalDebugInfo(new(sourceName, storageKind, storageIndex, startPc, endPc, flags));
        return true;
    }

    private bool TryGetDebuggerVisibleLocalName(int symbolId, out string name)
    {
        if (CompilerSymbolId.IsSourceIdentifier(symbolId) && identifierTable is not null)
        {
            name = identifierTable.GetIdentifierLiteral(CompilerSymbolId.GetSourceIdentifierId(symbolId));
            return true;
        }

        if (symbolNamesById.TryGetValue(symbolId, out name!))
            if (!name.StartsWith("$", StringComparison.Ordinal) && name.IndexOf('#') < 0)
                return true;

        name = string.Empty;
        return false;
    }

    private bool TryGetLocalDebugStorage(int symbolId, out JsLocalDebugStorageKind storageKind, out int storageIndex)
    {
        if (TryGetCurrentContextSlot(symbolId, out storageIndex))
        {
            storageKind = JsLocalDebugStorageKind.ContextSlot;
            return true;
        }

        if (TryGetLocalRegister(symbolId, out storageIndex))
        {
            storageKind = JsLocalDebugStorageKind.Register;
            return true;
        }

        storageKind = default;
        storageIndex = -1;
        return false;
    }

    private JsLocalDebugFlags GetLocalDebugFlags(int symbolId, bool? isConstOverride)
    {
        var flags = JsLocalDebugFlags.None;
        if (IsParameterLocalBinding(symbolId))
            flags |= JsLocalDebugFlags.Parameter;
        if (IsVarLocalBinding(symbolId))
            flags |= JsLocalDebugFlags.Var;
        if (IsLexicalLocalBinding(symbolId))
            flags |= JsLocalDebugFlags.Lexical;
        if (isConstOverride ?? IsConstLocalBinding(symbolId))
            flags |= JsLocalDebugFlags.Const;
        if (IsCapturedByChildBinding(symbolId))
            flags |= JsLocalDebugFlags.CapturedByChild;
        if (IsImmutableFunctionNameBinding(symbolId))
            flags |= JsLocalDebugFlags.ImmutableFunctionName;
        return flags;
    }

    private void PushAliasScope(CompilerIdentifierName sourceIdentifier, string internalName)
    {
        var bindings = Vm.RentCompileList<BlockLexicalBinding>(1);
        var internalSymbolId = GetOrCreateSymbolId(internalName);
        bindings.Add(new(sourceIdentifier.Name, sourceIdentifier.NameId, internalName, internalSymbolId,
            false));
        var scope = new LexicalAliasScope(bindings, true, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PushInheritedAliasScope(CompilerIdentifierName sourceIdentifier, string internalName)
    {
        var bindings = Vm.RentCompileList<BlockLexicalBinding>(1);
        var internalSymbolId = GetOrCreateSymbolId(internalName);
        bindings.Add(new(sourceIdentifier.Name, sourceIdentifier.NameId, internalName, internalSymbolId,
            false));
        var scope = new LexicalAliasScope(bindings, true, true);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
    }

    private void PushAliasScope(string sourceName, string internalName)
    {
        PushAliasScope(new CompilerIdentifierName(sourceName), internalName);
    }

    private List<BlockLexicalBinding>? PushCatchBindingAliases(JsCatchClause catchClause)
    {
        var bindings = CreateCatchBindingAliases(catchClause);
        if (bindings is null)
            return null;

        var scope = new LexicalAliasScope(bindings, true, false);
        activeBlockLexicalAliases.Push(scope);
        PushLocalDebugScope();
        ActivateLexicalAliasScope(scope);
        return bindings;
    }

    private List<BlockLexicalBinding>? CreateCatchBindingAliases(JsCatchClause catchClause)
    {
        if (!string.IsNullOrEmpty(catchClause.ParamName))
        {
            var bindings = Vm.RentCompileList<BlockLexicalBinding>(1);
            var sourceName = catchClause.ParamName;
            var internalName = GetCatchBindingInternalName(sourceName, catchClause.Position, 0);
            var internalSymbolId = GetOrCreateSymbolId(internalName);
            bindings.Add(new(sourceName, -1, internalName,
                internalSymbolId, false));
            return bindings;
        }

        if (catchClause.Declarators.Count == 0)
            return null;

        var declaratorBindings = Vm.RentCompileList<BlockLexicalBinding>(catchClause.Declarators.Count);
        for (var i = 0; i < catchClause.Declarators.Count; i++)
        {
            var declarator = catchClause.Declarators[i];
            var internalName = GetCatchBindingInternalName(declarator.Name, catchClause.Position, i);
            var internalSymbolId = GetOrCreateSymbolId(internalName);
            declaratorBindings.Add(new(declarator.Name, declarator.NameId, internalName,
                internalSymbolId, false));
        }

        return declaratorBindings;
    }

    private void PredeclareCatchBindings(JsCatchClause catchClause)
    {
        var bindings = CreateCatchBindingAliases(catchClause);
        if (bindings is null)
            return;

        try
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                MarkLexicalBinding(binding.InternalSymbolId, false);
            }
        }
        finally
        {
            Vm.ReturnCompileList(bindings);
        }
    }

    private void PopAliasScope()
    {
        var aliasScope = activeBlockLexicalAliases.Peek();
        var startPc = PopLocalDebugScopeStart();
        EmitLocalDebugInfos(aliasScope.Bindings, startPc, builder.CodeLength);
        DeactivateLexicalAliasScope(aliasScope);
        _ = activeBlockLexicalAliases.Pop();
        if (aliasScope.OwnsBindings && aliasScope.Bindings is List<BlockLexicalBinding> ownedBindings)
            Vm.ReturnCompileList(ownedBindings);
    }


    private static bool StatementAlwaysReturns(JsStatement statement)
    {
        switch (statement)
        {
            case JsReturnStatement:
            case JsThrowStatement:
                return true;
            case JsBlockStatement b:
                foreach (var s in b.Statements)
                    if (StatementAlwaysReturns(s))
                        return true;

                return false;
            case JsIfStatement i:
                return i.Alternate is not null &&
                       StatementAlwaysReturns(i.Consequent) &&
                       StatementAlwaysReturns(i.Alternate);
            case JsForInOfStatement:
                return false;
            case JsLabeledStatement:
                return false;
            default:
                return false;
        }
    }

    private static bool StatementNeverCompletesNormally(JsStatement statement)
    {
        switch (statement)
        {
            case JsReturnStatement:
            case JsThrowStatement:
            case JsBreakStatement:
            case JsContinueStatement:
                return true;
            case JsBlockStatement block:
            {
                var last = GetLastReachableStatement(block.Statements);
                return last is not null && StatementNeverCompletesNormally(last);
            }
            case JsIfStatement conditional:
                return conditional.Alternate is not null
                       && StatementNeverCompletesNormally(conditional.Consequent)
                       && StatementNeverCompletesNormally(conditional.Alternate);
            default:
                return false;
        }
    }

    private static JsStatement? GetLastReachableStatement(IReadOnlyList<JsStatement> statements)
    {
        JsStatement? last = null;
        foreach (var statement in statements)
        {
            last = statement;
            if (StatementAlwaysReturns(statement))
                break;
        }

        return last;
    }

    private static bool StatementListLeavesDirectCompletionValue(IReadOnlyList<JsStatement> statements)
    {
        var last = GetLastReachableStatement(statements);
        return last is not null && StatementLeavesDirectCompletionValue(last);
    }

    private bool StatementLeavesKnownUndefinedValueInCurrentContext(JsStatement statement)
    {
        switch (statement)
        {
            case JsBlockStatement block:
            {
                var last = GetLastReachableStatement(block.Statements);
                return last is not null && StatementLeavesKnownUndefinedValueInCurrentContext(last);
            }
            case JsIfStatement conditional:
                return conditional.Alternate is not null
                       && StatementLeavesKnownUndefinedValueInCurrentContext(conditional.Consequent)
                       && StatementLeavesKnownUndefinedValueInCurrentContext(conditional.Alternate);
            case JsLabeledStatement labeled:
                return StatementLeavesKnownUndefinedValueInCurrentContext(labeled.Statement);
            case JsWhileStatement whileStmt:
                return !LoopBodyNeedsCompletionTracking(whileStmt.Body);
            case JsDoWhileStatement doWhileStmt:
                return !LoopBodyNeedsCompletionTracking(doWhileStmt.Body);
            case JsForStatement forStmt:
                return !LoopBodyNeedsCompletionTracking(forStmt.Body);
            case JsForInOfStatement forInOfStmt:
                return !forInOfStmt.IsOf && !LoopBodyNeedsCompletionTracking(forInOfStmt.Body);
            default:
                return false;
        }
    }

    private static bool StatementLeavesDirectCompletionValue(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement:
                return true;
            case JsBlockStatement block:
                return StatementListLeavesDirectCompletionValue(block.Statements);
            case JsIfStatement conditional:
                return conditional.Alternate is not null
                       && StatementLeavesDirectCompletionValue(conditional.Consequent)
                       && StatementLeavesDirectCompletionValue(conditional.Alternate);
            case JsLabeledStatement labeled:
                return StatementLeavesDirectCompletionValue(labeled.Statement);
            default:
                return false;
        }
    }

    private static bool StatementListCanProduceTrackedCompletion(IReadOnlyList<JsStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementCanProduceTrackedCompletion(statement))
                return true;

            if (StatementAlwaysReturns(statement) || StatementNeverCompletesNormally(statement))
                break;
        }

        return false;
    }

    private static bool StatementListNeedsStructuredCompletionTracking(IReadOnlyList<JsStatement> statements)
    {
        return !StatementListLeavesDirectCompletionValue(statements)
               && (StatementListCanProduceTrackedCompletion(statements)
                   || StatementListCanCompleteAbruptEmpty(statements));
    }

    private static bool StatementCanProduceTrackedCompletion(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement:
                return true;
            case JsBlockStatement block:
                return StatementListCanProduceTrackedCompletion(block.Statements);
            case JsIfStatement conditional:
                return true;
            case JsWhileStatement whileStmt:
                return true;
            case JsDoWhileStatement doWhileStmt:
                return true;
            case JsForStatement forStmt:
                return true;
            case JsForInOfStatement forInOfStmt:
                return true;
            case JsLabeledStatement labeled:
                return StatementCanProduceTrackedCompletion(labeled.Statement);
            case JsSwitchStatement switchStmt:
                return true;
            case JsTryStatement tryStmt:
                return true;
            default:
                return false;
        }
    }

    private static bool StatementTrackedCompletionIsKnownNonHole(JsStatement statement)
    {
        return statement is JsIfStatement;
    }

    private static bool StatementListCanCompleteAbruptEmpty(IReadOnlyList<JsStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementCanCompleteAbruptEmpty(statement))
                return true;

            if (StatementAlwaysReturns(statement) || StatementNeverCompletesNormally(statement))
                break;
        }

        return false;
    }

    private static bool StatementAlwaysProducesTrackedCompletion(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement:
                return true;
            case JsBlockStatement block:
                return StatementListAlwaysProducesTrackedCompletion(block.Statements);
            case JsIfStatement conditional:
                return conditional.Alternate is not null
                       && StatementAlwaysProducesTrackedCompletion(conditional.Consequent)
                       && StatementAlwaysProducesTrackedCompletion(conditional.Alternate);
            default:
                return false;
        }
    }

    private static bool StatementListAlwaysProducesTrackedCompletion(IReadOnlyList<JsStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementCanCompleteAbruptEmpty(statement))
                return false;
            if (StatementAlwaysProducesTrackedCompletion(statement))
                return true;
            if (StatementAlwaysReturns(statement) || StatementNeverCompletesNormally(statement))
                return false;
        }

        return false;
    }

    private static bool StatementListNeedsCompletionIsolationFromParent(IReadOnlyList<JsStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementCanCompleteAbruptEmpty(statement))
                return true;
            if (StatementAlwaysProducesTrackedCompletion(statement))
                return false;
            if (StatementAlwaysReturns(statement) || StatementNeverCompletesNormally(statement))
                return false;
        }

        return false;
    }

    private static bool StatementCanCompleteAbruptEmpty(JsStatement statement)
    {
        switch (statement)
        {
            case JsBreakStatement:
            case JsContinueStatement:
                return true;
            case JsBlockStatement block:
                return StatementListCanCompleteAbruptEmpty(block.Statements);
            case JsIfStatement conditional:
                return StatementCanCompleteAbruptEmpty(conditional.Consequent)
                       || (conditional.Alternate is not null
                           && StatementCanCompleteAbruptEmpty(conditional.Alternate));
            case JsLabeledStatement labeled:
                return StatementCanCompleteAbruptEmpty(labeled.Statement);
            case JsWhileStatement whileStmt:
                return StatementCanCompleteAbruptEmpty(whileStmt.Body);
            case JsDoWhileStatement doWhileStmt:
                return StatementCanCompleteAbruptEmpty(doWhileStmt.Body);
            case JsForStatement forStmt:
                return StatementCanCompleteAbruptEmpty(forStmt.Body);
            case JsForInOfStatement forInOfStmt:
                return StatementCanCompleteAbruptEmpty(forInOfStmt.Body);
            case JsSwitchStatement switchStmt:
            {
                foreach (var switchCase in switchStmt.Cases)
                    if (StatementListCanCompleteAbruptEmpty(switchCase.Consequent))
                        return true;

                return false;
            }
            case JsTryStatement tryStmt:
                return StatementListCanCompleteAbruptEmpty(tryStmt.Block.Statements)
                       || (tryStmt.Handler is not null
                           && StatementListCanCompleteAbruptEmpty(tryStmt.Handler.Body.Statements))
                       || (tryStmt.Finalizer is not null
                           && StatementListCanCompleteAbruptEmpty(tryStmt.Finalizer.Statements));
            default:
                return false;
        }
    }

    private int AllocateSyntheticLocal(string name)
    {
        var symbolId = GetOrCreateSymbolId(name);
        if (TryGetLocalRegister(symbolId, out var reg))
        {
            localRegisters.Add(reg);
            return reg;
        }

        reg = builder.AllocatePinnedRegister();
        locals[symbolId] = reg;
        SetLocalBindingRegister(symbolId, reg);
        localRegisters.Add(reg);
        return reg;
    }

    private int AllocateTemporaryRegister()
    {
        return builder.AllocateTemporaryRegister();
    }

    private int AllocateTemporaryRegisterBlock(int count)
    {
        return builder.AllocateTemporaryRegisterBlock(count);
    }

    private int EmitArgumentsIntoContiguousTemporaryRegisters(IReadOnlyList<JsExpression> arguments)
    {
        if (arguments.Count == 0)
            return -1;

        var argStart = AllocateTemporaryRegisterBlock(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            VisitExpression(arguments[i]);
            EmitStarRegister(argStart + i);
        }

        return argStart;
    }

    private int GetCallArgumentStart(IReadOnlyList<JsExpression> arguments)
    {
        if (TryGetContiguousPlainLocalArgumentRegisters(arguments, out var argStart))
            return arguments.Count == 0 ? -1 : argStart;

        return EmitArgumentsIntoContiguousTemporaryRegisters(arguments);
    }

    private static bool HasSpreadArgument(IReadOnlyList<JsExpression> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
            if (arguments[i] is JsSpreadExpression)
                return true;

        return false;
    }

    private int EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(
        IReadOnlyList<JsExpression> arguments,
        out int flagsReg)
    {
        if (arguments.Count == 0)
        {
            var flagsIdx = builder.AddObjectConstant(Array.Empty<int>());
            EmitLdaTypedConstByIndex(Tag.JsTagObject, flagsIdx);
            flagsReg = AllocateTemporaryRegister();
            EmitStarRegister(flagsReg);
            return -1;
        }

        var argStart = AllocateTemporaryRegisterBlock(arguments.Count);
        var spreadFlags = new int[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument is JsSpreadExpression spread)
            {
                spreadFlags[i] = 1;
                VisitExpression(spread.Argument);
            }
            else
            {
                VisitExpression(argument);
            }

            EmitStarRegister(argStart + i);
        }

        var flagsConstIdx = builder.AddObjectConstant(spreadFlags);
        EmitLdaTypedConstByIndex(Tag.JsTagObject, flagsConstIdx);
        flagsReg = AllocateTemporaryRegister();
        EmitStarRegister(flagsReg);
        return argStart;
    }

    private bool TryEmitExplicitSuperForwardAllArguments(IReadOnlyList<JsExpression> arguments)
    {
        if (arguments.Count != 1 ||
            arguments[0] is not JsSpreadExpression
            {
                Argument: JsIdentifierExpression { Name: "arguments" }
            })
            return false;

        EmitCallRuntime(RuntimeId.CallSuperConstructorForwardAll, 0, 0);
        return true;
    }

    private void ReleaseTemporaryRegister(int register)
    {
        if (register < 0)
            return;
        if (localRegisters.Contains(register))
            return;
        if (suspendPinnedRegisterRefCounts.TryGetValue(register, out var refCount) && refCount > 0)
            return;
        if (register == syntheticArgumentsRegister || register == cachedNewTargetRegister ||
            register == generatorResumeValueTempRegister)
            return;
        builder.ReleaseTemporaryRegister(register);
    }

    private int BeginTemporaryRegisterScope()
    {
        return builder.GetTemporaryRegisterScopeMarker();
    }

    private void EndTemporaryRegisterScope(int marker)
    {
        builder.ReleaseTemporaryRegistersToMarker(marker);
    }

    private static bool ShouldCreateArgumentsObjectForFunction(
        IReadOnlyList<string> parameters,
        IReadOnlyList<JsExpression?> parameterInitializers,
        JsBlockStatement body,
        bool isArrow)
    {
        if (isArrow)
            return false;
        foreach (var p in parameters)
            if (string.Equals(p, "arguments", StringComparison.Ordinal))
                return false;

        foreach (var initializer in parameterInitializers)
            if (initializer is not null && ExpressionReferencesArgumentsInFunctionScope(initializer))
                return true;

        foreach (var statement in body.Statements)
            if (StatementReferencesArgumentsInFunctionScope(statement))
                return true;

        return false;
    }

    private static bool StatementReferencesArgumentsInFunctionScope(JsStatement stmt)
    {
        return stmt switch
        {
            JsExpressionStatement es => ExpressionReferencesArgumentsInFunctionScope(es.Expression),
            JsBlockStatement block => block.Statements.Any(StatementReferencesArgumentsInFunctionScope),
            JsIfStatement i => ExpressionReferencesArgumentsInFunctionScope(i.Test)
                               || StatementReferencesArgumentsInFunctionScope(i.Consequent)
                               || (i.Alternate is not null && StatementReferencesArgumentsInFunctionScope(i.Alternate)),
            JsWhileStatement w => ExpressionReferencesArgumentsInFunctionScope(w.Test)
                                  || StatementReferencesArgumentsInFunctionScope(w.Body),
            JsForStatement f =>
                (f.Init is JsVariableDeclarationStatement initDecl &&
                 initDecl.Declarators.Any(d =>
                     d.Initializer is not null && ExpressionReferencesArgumentsInFunctionScope(d.Initializer))) ||
                (f.Init is JsExpression initExpr && ExpressionReferencesArgumentsInFunctionScope(initExpr)) ||
                (f.Test is not null && ExpressionReferencesArgumentsInFunctionScope(f.Test)) ||
                (f.Update is not null && ExpressionReferencesArgumentsInFunctionScope(f.Update)) ||
                StatementReferencesArgumentsInFunctionScope(f.Body),
            JsForInOfStatement f =>
                (f.Left is JsVariableDeclarationStatement leftDecl &&
                 leftDecl.Declarators.Any(d =>
                     d.Initializer is not null && ExpressionReferencesArgumentsInFunctionScope(d.Initializer))) ||
                (f.Left is JsExpression leftExpr && ExpressionReferencesArgumentsInFunctionScope(leftExpr)) ||
                ExpressionReferencesArgumentsInFunctionScope(f.Right) ||
                StatementReferencesArgumentsInFunctionScope(f.Body),
            JsReturnStatement r => r.Argument is not null && ExpressionReferencesArgumentsInFunctionScope(r.Argument),
            JsThrowStatement t => ExpressionReferencesArgumentsInFunctionScope(t.Argument),
            JsVariableDeclarationStatement v =>
                (v.BindingInitializer is not null &&
                 ExpressionReferencesArgumentsInFunctionScope(v.BindingInitializer)) ||
                (v.BindingPattern is not null && ExpressionReferencesArgumentsInFunctionScope(v.BindingPattern)) ||
                v.Declarators.Any(d =>
                    d.Initializer is not null && ExpressionReferencesArgumentsInFunctionScope(d.Initializer)),
            JsEmptyObjectBindingDeclarationStatement emptyObjectBinding => ExpressionReferencesArgumentsInFunctionScope(
                emptyObjectBinding.Initializer),
            JsFunctionDeclaration => false,
            JsTryStatement t =>
                StatementReferencesArgumentsInFunctionScope(t.Block) ||
                (t.Handler is not null && StatementReferencesArgumentsInFunctionScope(t.Handler.Body)) ||
                (t.Finalizer is not null && StatementReferencesArgumentsInFunctionScope(t.Finalizer)),
            JsSwitchStatement sw =>
                ExpressionReferencesArgumentsInFunctionScope(sw.Discriminant) ||
                sw.Cases.Any(c =>
                    (c.Test is not null && ExpressionReferencesArgumentsInFunctionScope(c.Test)) ||
                    c.Consequent.Any(StatementReferencesArgumentsInFunctionScope)),
            JsLabeledStatement l => StatementReferencesArgumentsInFunctionScope(l.Statement),
            _ => false
        };
    }

    private static bool ExpressionReferencesArgumentsInFunctionScope(JsExpression expr)
    {
        return expr switch
        {
            JsIdentifierExpression id => string.Equals(id.Name, "arguments", StringComparison.Ordinal),
            JsAssignmentExpression a => ExpressionReferencesArgumentsInFunctionScope(a.Left) ||
                                        ExpressionReferencesArgumentsInFunctionScope(a.Right),
            JsBinaryExpression b => ExpressionReferencesArgumentsInFunctionScope(b.Left) ||
                                    ExpressionReferencesArgumentsInFunctionScope(b.Right),
            JsConditionalExpression c => ExpressionReferencesArgumentsInFunctionScope(c.Test) ||
                                         ExpressionReferencesArgumentsInFunctionScope(c.Consequent) ||
                                         ExpressionReferencesArgumentsInFunctionScope(c.Alternate),
            JsCallExpression c => ExpressionReferencesArgumentsInFunctionScope(c.Callee) ||
                                  c.Arguments.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsImportCallExpression importCall =>
                ExpressionReferencesArgumentsInFunctionScope(importCall.Argument) ||
                (importCall.Options is not null && ExpressionReferencesArgumentsInFunctionScope(importCall.Options)),
            JsNewExpression n => ExpressionReferencesArgumentsInFunctionScope(n.Callee) ||
                                 n.Arguments.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsMemberExpression m => ExpressionReferencesArgumentsInFunctionScope(m.Object) ||
                                    (m.IsComputed && ExpressionReferencesArgumentsInFunctionScope(m.Property)),
            JsSequenceExpression s => s.Expressions.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsSpreadExpression s => ExpressionReferencesArgumentsInFunctionScope(s.Argument),
            JsIntrinsicCallExpression i => i.Arguments.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsParameterInitializerExpression p => ExpressionReferencesArgumentsInFunctionScope(p.Expression),
            JsArrayExpression a => a.Elements.Any(e =>
                e is not null && ExpressionReferencesArgumentsInFunctionScope(e)),
            JsTemplateExpression t => t.Expressions.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsTaggedTemplateExpression tt =>
                ExpressionReferencesArgumentsInFunctionScope(tt.Tag) ||
                tt.Template.Expressions.Any(ExpressionReferencesArgumentsInFunctionScope),
            JsObjectExpression o => o.Properties.Any(PropertyReferencesArgumentsInFunctionScope),
            JsClassExpression c => (c.ExtendsExpression is not null &&
                                    ExpressionReferencesArgumentsInFunctionScope(c.ExtendsExpression)) ||
                                   c.Elements.Any(ClassElementReferencesArgumentsInFunctionScope),
            JsUnaryExpression u => ExpressionReferencesArgumentsInFunctionScope(u.Argument),
            JsUpdateExpression u => ExpressionReferencesArgumentsInFunctionScope(u.Argument),
            JsYieldExpression y => y.Argument is not null && ExpressionReferencesArgumentsInFunctionScope(y.Argument),
            JsAwaitExpression a => ExpressionReferencesArgumentsInFunctionScope(a.Argument),
            JsFunctionExpression f when f.IsArrow =>
                f.ParameterInitializers.Any(i => i is not null && ExpressionReferencesArgumentsInFunctionScope(i)) ||
                f.Body.Statements.Any(StatementReferencesArgumentsInFunctionScope),
            JsFunctionExpression => false,
            _ => false
        };
    }

    private static bool PropertyReferencesArgumentsInFunctionScope(JsObjectProperty property)
    {
        if (property.ComputedKey is not null && ExpressionReferencesArgumentsInFunctionScope(property.ComputedKey))
            return true;
        return ExpressionReferencesArgumentsInFunctionScope(property.Value);
    }

    private static bool ClassElementReferencesArgumentsInFunctionScope(JsClassElement element)
    {
        if (element.ComputedKey is not null && ExpressionReferencesArgumentsInFunctionScope(element.ComputedKey))
            return true;
        if (element.StaticBlock is not null &&
            element.StaticBlock.Statements.Any(StatementReferencesArgumentsInFunctionScope))
            return true;
        if (element.FieldInitializer is not null &&
            ExpressionReferencesArgumentsInFunctionScope(element.FieldInitializer))
            return true;
        if (element.Value is not null && element.Value.IsArrow)
        {
            if (element.Value.ParameterInitializers.Any(i =>
                    i is not null && ExpressionReferencesArgumentsInFunctionScope(i)))
                return true;
            if (element.Value.Body.Statements.Any(StatementReferencesArgumentsInFunctionScope))
                return true;
        }

        return false;
    }

    private static int CountNewTargetExpressions(IEnumerable<JsStatement> statements)
    {
        var count = 0;
        foreach (var statement in statements) count += CountNewTargetInStatement(statement);

        return count;
    }

    private static int CountNewTargetInStatement(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement e:
                return CountNewTargetInExpression(e.Expression);
            case JsBlockStatement b:
                return CountNewTargetExpressions(b.Statements);
            case JsIfStatement i:
                return CountNewTargetInExpression(i.Test) +
                       CountNewTargetInStatement(i.Consequent) +
                       (i.Alternate is not null ? CountNewTargetInStatement(i.Alternate) : 0);
            case JsWhileStatement w:
                return CountNewTargetInExpression(w.Test) + CountNewTargetInStatement(w.Body);
            case JsForStatement f:
                return (f.Init is JsExpression initExpr ? CountNewTargetInExpression(initExpr) : 0) +
                       (f.Init is JsVariableDeclarationStatement initDecl
                           ? initDecl.Declarators.Sum(d =>
                               d.Initializer is not null ? CountNewTargetInExpression(d.Initializer) : 0)
                           : 0) +
                       (f.Test is not null ? CountNewTargetInExpression(f.Test) : 0) +
                       (f.Update is not null ? CountNewTargetInExpression(f.Update) : 0) +
                       CountNewTargetInStatement(f.Body);
            case JsForInOfStatement f:
                return (f.Left is JsVariableDeclarationStatement leftDecl
                           ? leftDecl.Declarators.Sum(d =>
                               d.Initializer is not null ? CountNewTargetInExpression(d.Initializer) : 0)
                           : 0) +
                       (f.Left is JsExpression leftExpr ? CountNewTargetInExpression(leftExpr) : 0) +
                       CountNewTargetInExpression(f.Right) +
                       CountNewTargetInStatement(f.Body);
            case JsVariableDeclarationStatement v:
                return v.Declarators.Sum(d =>
                    d.Initializer is not null ? CountNewTargetInExpression(d.Initializer) : 0);
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                return CountNewTargetInExpression(emptyObjectBinding.Initializer);
            case JsReturnStatement r:
                return r.Argument is not null ? CountNewTargetInExpression(r.Argument) : 0;
            case JsThrowStatement t:
                return CountNewTargetInExpression(t.Argument);
            case JsTryStatement t:
                return CountNewTargetInStatement(t.Block) +
                       (t.Handler is not null ? CountNewTargetInStatement(t.Handler.Body) : 0) +
                       (t.Finalizer is not null ? CountNewTargetInStatement(t.Finalizer) : 0);
            case JsSwitchStatement sw:
            {
                var count = CountNewTargetInExpression(sw.Discriminant);
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null)
                        count += CountNewTargetInExpression(c.Test);
                    foreach (var s in c.Consequent)
                        count += CountNewTargetInStatement(s);
                }

                return count;
            }
            case JsLabeledStatement l:
                return CountNewTargetInStatement(l.Statement);
            default:
                return 0;
        }
    }

    private static int CountNewTargetInExpression(JsExpression expression)
    {
        switch (expression)
        {
            case JsNewTargetExpression:
                return 1;
            case JsAssignmentExpression a:
                return CountNewTargetInExpression(a.Left) + CountNewTargetInExpression(a.Right);
            case JsBinaryExpression b:
                return CountNewTargetInExpression(b.Left) + CountNewTargetInExpression(b.Right);
            case JsConditionalExpression c:
                return CountNewTargetInExpression(c.Test) +
                       CountNewTargetInExpression(c.Consequent) +
                       CountNewTargetInExpression(c.Alternate);
            case JsCallExpression c:
                return CountNewTargetInExpression(c.Callee) + c.Arguments.Sum(CountNewTargetInExpression);
            case JsNewExpression n:
                return CountNewTargetInExpression(n.Callee) + n.Arguments.Sum(CountNewTargetInExpression);
            case JsMemberExpression m:
                return CountNewTargetInExpression(m.Object) + CountNewTargetInExpression(m.Property);
            case JsUnaryExpression u:
                return CountNewTargetInExpression(u.Argument);
            case JsUpdateExpression u:
                return CountNewTargetInExpression(u.Argument);
            case JsYieldExpression y:
                return y.Argument is not null ? CountNewTargetInExpression(y.Argument) : 0;
            case JsImportCallExpression importCall:
                return CountNewTargetInExpression(importCall.Argument) +
                       (importCall.Options is not null ? CountNewTargetInExpression(importCall.Options) : 0);
            default:
                return 0;
        }
    }

    private static bool RequiresSelfBinding(JsBlockStatement body, string selfName)
    {
        foreach (var stmt in body.Statements)
            if (StatementReferencesIdentifier(stmt, selfName))
                return true;

        return false;
    }

    private static JsBytecodeFunctionKind GetFunctionKind(bool isGenerator, bool isAsync)
    {
        if (isGenerator && isAsync) return JsBytecodeFunctionKind.AsyncGenerator;
        if (isGenerator) return JsBytecodeFunctionKind.Generator;
        if (isAsync) return JsBytecodeFunctionKind.Async;
        return JsBytecodeFunctionKind.Normal;
    }

    private void EmitSourcePosition(int sourceOffset)
    {
        if (sourceOffset < 0)
            return;
        builder.SetPendingSourceOffset(sourceOffset);
    }

    private static bool StatementReferencesIdentifier(JsStatement stmt, string name)
    {
        switch (stmt)
        {
            case JsExpressionStatement es:
                return ExpressionReferencesIdentifier(es.Expression, name);
            case JsBlockStatement block:
                return block.Statements.Any(s => StatementReferencesIdentifier(s, name));
            case JsIfStatement i:
                return ExpressionReferencesIdentifier(i.Test, name)
                       || StatementReferencesIdentifier(i.Consequent, name)
                       || (i.Alternate != null && StatementReferencesIdentifier(i.Alternate, name));
            case JsWhileStatement w:
                return ExpressionReferencesIdentifier(w.Test, name) || StatementReferencesIdentifier(w.Body, name);
            case JsForStatement f:
                return (f.Init is JsVariableDeclarationStatement initDecl &&
                        initDecl.Declarators.Any(d =>
                            d.Initializer != null && ExpressionReferencesIdentifier(d.Initializer, name)))
                       || (f.Init is JsExpression initExpr && ExpressionReferencesIdentifier(initExpr, name))
                       || (f.Test != null && ExpressionReferencesIdentifier(f.Test, name))
                       || (f.Update != null && ExpressionReferencesIdentifier(f.Update, name))
                       || StatementReferencesIdentifier(f.Body, name);
            case JsForInOfStatement f:
                return (f.Left is JsVariableDeclarationStatement leftDecl &&
                        leftDecl.Declarators.Any(d =>
                            d.Initializer != null && ExpressionReferencesIdentifier(d.Initializer, name)))
                       || (f.Left is JsExpression leftExpr && ExpressionReferencesIdentifier(leftExpr, name))
                       || ExpressionReferencesIdentifier(f.Right, name)
                       || StatementReferencesIdentifier(f.Body, name);
            case JsReturnStatement r:
                return r.Argument != null && ExpressionReferencesIdentifier(r.Argument, name);
            case JsThrowStatement t:
                return ExpressionReferencesIdentifier(t.Argument, name);
            case JsVariableDeclarationStatement v:
                return v.Declarators.Any(d =>
                    d.Initializer != null && ExpressionReferencesIdentifier(d.Initializer, name));
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                return ExpressionReferencesIdentifier(emptyObjectBinding.Initializer, name);
            case JsFunctionDeclaration:
                return false; // nested function has separate self binding scope
            case JsTryStatement t:
                return StatementReferencesIdentifier(t.Block, name)
                       || (t.Handler is not null && StatementReferencesIdentifier(t.Handler.Body, name))
                       || (t.Finalizer is not null && StatementReferencesIdentifier(t.Finalizer, name));
            case JsSwitchStatement sw:
                if (ExpressionReferencesIdentifier(sw.Discriminant, name))
                    return true;
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null && ExpressionReferencesIdentifier(c.Test, name))
                        return true;
                    foreach (var s in c.Consequent)
                        if (StatementReferencesIdentifier(s, name))
                            return true;
                }

                return false;
            case JsBreakStatement:
            case JsContinueStatement:
                return false;
            case JsLabeledStatement labeled:
                return StatementReferencesIdentifier(labeled.Statement, name);
            default:
                return false;
        }
    }

    private static bool ExpressionReferencesIdentifier(JsExpression expr, string name)
    {
        switch (expr)
        {
            case JsIdentifierExpression id:
                return id.Name == name;
            case JsLiteralExpression:
                return false;
            case JsAssignmentExpression a:
                return ExpressionReferencesIdentifier(a.Left, name) || ExpressionReferencesIdentifier(a.Right, name);
            case JsBinaryExpression b:
                return ExpressionReferencesIdentifier(b.Left, name) || ExpressionReferencesIdentifier(b.Right, name);
            case JsConditionalExpression c:
                return ExpressionReferencesIdentifier(c.Test, name) ||
                       ExpressionReferencesIdentifier(c.Consequent, name) ||
                       ExpressionReferencesIdentifier(c.Alternate, name);
            case JsCallExpression c:
                return ExpressionReferencesIdentifier(c.Callee, name) ||
                       c.Arguments.Any(a => ExpressionReferencesIdentifier(a, name));
            case JsNewExpression n:
                return ExpressionReferencesIdentifier(n.Callee, name) ||
                       n.Arguments.Any(a => ExpressionReferencesIdentifier(a, name));
            case JsUpdateExpression u:
                return ExpressionReferencesIdentifier(u.Argument, name);
            case JsYieldExpression y:
                return y.Argument is not null && ExpressionReferencesIdentifier(y.Argument, name);
            case JsImportCallExpression importCall:
                return ExpressionReferencesIdentifier(importCall.Argument, name) ||
                       (importCall.Options is not null && ExpressionReferencesIdentifier(importCall.Options, name));
            case JsFunctionExpression:
                return false; // nested function expression has separate self binding scope
            default:
                return false;
        }
    }

    private static bool ExpressionRequiresCurrentGeneratorExecution(JsExpression expr)
    {
        switch (expr)
        {
            case JsYieldExpression:
            case JsAwaitExpression:
                return true;
            case JsAssignmentExpression a:
                return ExpressionRequiresCurrentGeneratorExecution(a.Left) ||
                       ExpressionRequiresCurrentGeneratorExecution(a.Right);
            case JsBinaryExpression b:
                return ExpressionRequiresCurrentGeneratorExecution(b.Left) ||
                       ExpressionRequiresCurrentGeneratorExecution(b.Right);
            case JsConditionalExpression c:
                return ExpressionRequiresCurrentGeneratorExecution(c.Test) ||
                       ExpressionRequiresCurrentGeneratorExecution(c.Consequent) ||
                       ExpressionRequiresCurrentGeneratorExecution(c.Alternate);
            case JsCallExpression c:
                return ExpressionRequiresCurrentGeneratorExecution(c.Callee) ||
                       c.Arguments.Any(ExpressionRequiresCurrentGeneratorExecution);
            case JsNewExpression n:
                return ExpressionRequiresCurrentGeneratorExecution(n.Callee) ||
                       n.Arguments.Any(ExpressionRequiresCurrentGeneratorExecution);
            case JsMemberExpression m:
                return ExpressionRequiresCurrentGeneratorExecution(m.Object) ||
                       ExpressionRequiresCurrentGeneratorExecution(m.Property);
            case JsUnaryExpression u:
                return ExpressionRequiresCurrentGeneratorExecution(u.Argument);
            case JsUpdateExpression u:
                return ExpressionRequiresCurrentGeneratorExecution(u.Argument);
            case JsArrayExpression a:
                return a.Elements.Any(e => e is not null && ExpressionRequiresCurrentGeneratorExecution(e));
            case JsObjectExpression o:
                return o.Properties.Any(p =>
                    (p.ComputedKey is not null && ExpressionRequiresCurrentGeneratorExecution(p.ComputedKey)) ||
                    ExpressionRequiresCurrentGeneratorExecution(p.Value));
            case JsFunctionExpression:
            case JsClassExpression:
                return false;
            default:
                return false;
        }
    }

    private bool IsReplTopLevelMode()
    {
        return parent is null && compileContext is { IsRepl: true };
    }

    private bool IsTopLevelScriptMode()
    {
        return parent is null && moduleVariableBindings is null;
    }

    private bool UsesGlobalScriptBindingsMode()
    {
        return IsTopLevelScriptMode() && compileContext is not { IsStrictIndirectEval: true };
    }

    private bool UsesPersistentGlobalLexicalBindingsMode()
    {
        return IsTopLevelScriptMode() && compileContext is not { IsIndirectEval: true };
    }

    private void EmitTopLevelVarBindingPrologue()
    {
        if (!UsesGlobalScriptBindingsMode())
            return;

        foreach (var entry in localBindingInfoById)
        {
            var name = GetSymbolName(entry.Key);
            var info = entry.Value;
            if ((info.Flags & LocalBindingFlags.Var) == 0)
                continue;
            if ((info.Flags & LocalBindingFlags.Lexical) != 0)
                continue;

            EmitLdaUndefined();
            StoreIdentifier(name, true, name);
        }
    }

    private void ValidateTopLevelRestrictedLexicalDeclarations()
    {
        if (!UsesPersistentGlobalLexicalBindingsMode() || topLevelLexicalDeclarationPositionByName.Count == 0)
            return;

        foreach (var entry in topLevelLexicalDeclarationPositionByName)
        {
            var name = entry.Key;
            var atom = Vm.Atoms.InternNoCheck(name);
            if (!Vm.GlobalObject.HasRestrictedGlobalPropertyAtom(atom))
                continue;
            throw new JsParseException($"Identifier '{name}' has already been declared", entry.Value,
                CurrentSourceText);
        }
    }


    private bool IsReplTopLevelConstName(string name)
    {
        return compileContext?.ReplTopLevelConstNames?.Contains(name) == true;
    }

    private void EmitHoistedFunctionDeclarations(IReadOnlyList<JsStatement> statements)
    {
        List<JsFunctionDeclaration>? hoisted = null;
        HashSet<string>? seenNames = null;
        for (var i = statements.Count - 1; i >= 0; i--)
        {
            if (statements[i] is not JsFunctionDeclaration declaration)
                continue;

            seenNames ??= new(StringComparer.Ordinal);
            if (!seenNames.Add(declaration.Name))
                continue;

            hoisted ??= new(4);
            hoisted.Insert(0, declaration);
        }

        if (hoisted is null)
            return;

        for (var i = 0; i < hoisted.Count; i++)
            HoistFunction(hoisted[i]);
    }

    private string? TryGetFunctionSourceText(int sourceStartPosition, int sourceEndPosition)
    {
        var sourceText = CurrentSourceText;
        if (sourceText is null || sourceStartPosition < 0 || sourceEndPosition <= sourceStartPosition ||
            sourceEndPosition > sourceText.Length)
            return null;

        if (sourceText[sourceStartPosition] == '(' &&
            sourceStartPosition + 1 < sourceEndPosition &&
            sourceText.AsSpan(sourceStartPosition + 1).StartsWith("function", StringComparison.Ordinal))
            sourceStartPosition++;
        else if (sourceText[sourceStartPosition] == '(' &&
                 sourceStartPosition + 1 < sourceEndPosition &&
                 sourceText.AsSpan(sourceStartPosition + 1).StartsWith("async function", StringComparison.Ordinal))
            sourceStartPosition++;

        if (!LooksLikeSourceTextStart(sourceStartPosition, sourceEndPosition) &&
            TryFindFunctionLikeSourceStartFallback(sourceStartPosition, sourceEndPosition, out var recoveredStart))
        {
            sourceStartPosition = recoveredStart;
            while (sourceStartPosition < sourceEndPosition && char.IsWhiteSpace(sourceText[sourceStartPosition]))
                sourceStartPosition++;
        }

        return sourceText[sourceStartPosition..sourceEndPosition];
    }

    private bool LooksLikeSourceTextStart(int sourceStartPosition, int sourceEndPosition)
    {
        var sourceText = CurrentSourceText;
        if (sourceText is null || sourceStartPosition >= sourceEndPosition)
            return false;

        var span = sourceText.AsSpan(sourceStartPosition, sourceEndPosition - sourceStartPosition);
        return span.StartsWith("function", StringComparison.Ordinal) ||
               span.StartsWith("async", StringComparison.Ordinal) ||
               span.StartsWith("class", StringComparison.Ordinal) ||
               span[0] == '(';
    }

    private bool TryFindFunctionLikeSourceStartFallback(
        int sourceStartPosition,
        int sourceEndPosition,
        out int recoveredStart)
    {
        var sourceText = CurrentSourceText;
        recoveredStart = -1;
        if (sourceText is null)
            return false;

        var searchLength = sourceEndPosition - sourceStartPosition;
        var functionStart =
            sourceText.LastIndexOf("function", sourceEndPosition - 1, searchLength, StringComparison.Ordinal);
        if (functionStart < sourceStartPosition)
            return false;

        recoveredStart = functionStart;
        if (TryFindPrecedingAsyncKeyword(sourceStartPosition, functionStart, out var asyncStart))
            recoveredStart = asyncStart;
        return true;
    }

    private bool TryFindPrecedingAsyncKeyword(int lowerBound, int functionStart, out int asyncStart)
    {
        var sourceText = CurrentSourceText;
        asyncStart = -1;
        if (sourceText is null)
            return false;

        var scan = ExpandBackwardOverTrivia(lowerBound, functionStart);
        var asyncCandidate = scan - "async".Length;
        if (asyncCandidate < lowerBound)
            return false;

        if (!sourceText.AsSpan(asyncCandidate, "async".Length).SequenceEqual("async".AsSpan()))
            return false;

        if (asyncCandidate > lowerBound && IsIdentifierPart(sourceText[asyncCandidate - 1]))
            return false;

        asyncStart = asyncCandidate;
        return true;
    }

    private int ExpandBackwardOverTrivia(int lowerBound, int start)
    {
        var sourceText = CurrentSourceText;
        if (sourceText is null)
            return start;

        var current = start;
        while (current > lowerBound)
        {
            var next = SkipWhitespaceAndBlockCommentsBackward(lowerBound, current);
            if (next == current)
                break;
            current = next;
        }

        return current;
    }

    private int SkipWhitespaceAndBlockCommentsBackward(int lowerBound, int start)
    {
        var sourceText = CurrentSourceText;
        if (sourceText is null)
            return start;

        var current = start;
        while (current > lowerBound)
        {
            var ch = sourceText[current - 1];
            if (char.IsWhiteSpace(ch))
            {
                current--;
                continue;
            }

            if (current - 2 >= lowerBound &&
                sourceText[current - 2] == '*' &&
                sourceText[current - 1] == '/')
            {
                var searchStart = current - 2;
                var searchLength = searchStart - lowerBound + 1;
                var commentStart = sourceText.LastIndexOf("/*", searchStart, searchLength, StringComparison.Ordinal);
                if (commentStart >= lowerBound)
                {
                    current = commentStart;
                    continue;
                }
            }

            break;
        }

        return current;
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';
    }

    private sealed record BlockLexicalBinding(
        string SourceName,
        int SourceNameId,
        string InternalName,
        int InternalSymbolId,
        bool IsConst)
    {
        public bool Matches(CompilerIdentifierName identifier)
        {
            if (identifier.NameId >= 0 && SourceNameId >= 0)
                return identifier.NameId == SourceNameId ||
                       string.Equals(SourceName, identifier.Name, StringComparison.Ordinal);

            return string.Equals(SourceName, identifier.Name, StringComparison.Ordinal);
        }

        public bool Matches(string sourceName)
        {
            return string.Equals(SourceName, sourceName, StringComparison.Ordinal);
        }
    }

    private sealed record LexicalAliasScope(
        IReadOnlyList<BlockLexicalBinding> Bindings,
        bool OwnsBindings,
        bool Inherited)
    {
        public List<int>? RuntimeAllocatedSymbolIds { get; set; }
    }

    private sealed record LoopTargets(BytecodeBuilder.Label BreakTarget, BytecodeBuilder.Label ContinueTarget);

    private sealed record LabeledTargets(
        string Label,
        BytecodeBuilder.Label BreakTarget,
        BytecodeBuilder.Label ContinueTarget,
        bool HasContinueTarget);

    private readonly record struct NamedLiteralPropertyPlan(int Atom, JsShapePropertyFlags InitFlags);

    private readonly record struct CompiledFunctionShape(
        JsBytecodeFunctionKind Kind,
        bool IsArrow = false,
        bool IsMethod = false,
        bool IsImplicitlyStrict = false,
        bool IsClassConstructor = false,
        bool IsDerivedConstructor = false,
        bool EmitImplicitSuperForwardAll = false)
    {
        public bool IsGenerator =>
            Kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator;

        public bool IsAsync => Kind is JsBytecodeFunctionKind.Async or JsBytecodeFunctionKind.AsyncGenerator;
    }

    private enum PrivateMemberKind : byte
    {
        Field = 0,
        Accessor = 1,
        Method = 2
    }

    private readonly record struct PrivateFieldBinding(
        int BrandId,
        int SlotIndex,
        PrivateMemberKind Kind = PrivateMemberKind.Field);

    private readonly record struct PrivateFieldInitPlan(
        string SourceName,
        PrivateFieldBinding Binding,
        JsExpression? Initializer);

    private enum InstanceFieldInitializerKind : byte
    {
        PrivateField,
        PublicField
    }

    private readonly record struct InstanceFieldInitializerPlan(
        InstanceFieldInitializerKind Kind,
        PrivateFieldInitPlan PrivateField,
        PublicFieldInitPlan PublicField);

    private readonly record struct PrivateAccessorInitPlan(
        string SourceName,
        PrivateFieldBinding Binding,
        JsFunctionExpression? Getter,
        JsFunctionExpression? Setter);

    private readonly record struct PrivateMethodInitPlan(
        string SourceName,
        PrivateFieldBinding Binding,
        JsFunctionExpression Function);

    private readonly record struct PrivateBrandSourceMapping(int BrandId, int SourceReg);

    private readonly record struct ActiveClassPrivateSourceScope(
        int InstanceBrandId,
        int InstanceBrandSourceReg,
        int StaticBrandId,
        int StaticBrandSourceReg);

    private readonly record struct PublicFieldInitPlan(
        JsClassElement Element,
        int ComputedKeyIndex = -1,
        string? SourceName = null);

    private readonly record struct ComputedClassElementKeyPlan(
        int KeyRegister = -1,
        int InstanceFieldKeyIndex = -1);

    private enum ContinueLabelError : byte
    {
        None = 0,
        UndefinedLabel = 1,
        NotIterationLabel = 2
    }

    private sealed class PendingClassAccessorState
    {
        public JsBytecodeFunction? Getter;
        public JsBytecodeFunction? Setter;
    }

    private sealed class FinallyJumpRouteMap
    {
        private readonly Dictionary<BytecodeBuilder.Label, int> breakRouteByTarget = new();
        private readonly Dictionary<BytecodeBuilder.Label, int> continueRouteByTarget = new();
        private readonly List<(int RouteId, BytecodeBuilder.Label Target, bool IsContinue)> routes = new();
        private int nextRouteId = 1;

        public IEnumerable<(int RouteId, BytecodeBuilder.Label Target, bool IsContinue)> Routes => routes;
        public bool HasBreakRoutes => breakRouteByTarget.Count != 0;
        public bool HasContinueRoutes => continueRouteByTarget.Count != 0;

        public int GetOrAddRouteId(BytecodeBuilder.Label target, bool isContinue)
        {
            var map = isContinue ? continueRouteByTarget : breakRouteByTarget;
            if (map.TryGetValue(target, out var existing))
                return existing;

            var id = nextRouteId++;
            map[target] = id;
            routes.Add((id, target, isContinue));
            return id;
        }
    }

    private readonly record struct FinallyFlowContext(
        int KindRegister,
        int ValueRegister,
        BytecodeBuilder.Label TargetLabel,
        bool InterceptThrow,
        FinallyJumpRouteMap Routes);

    private readonly record struct ForAwaitLoopContext(
        BytecodeBuilder.Label BreakTarget,
        BytecodeBuilder.Label ContinueTarget,
        int CloseRequestedRegister);

    private readonly record struct ForOfIteratorLoopContext(
        BytecodeBuilder.Label BreakTarget,
        BytecodeBuilder.Label ContinueTarget,
        int IteratorRegister,
        int CatchableTryDepthAtEntry);

    private readonly record struct StatementCompletionState(
        int Register,
        bool KnownNonHole);
}
