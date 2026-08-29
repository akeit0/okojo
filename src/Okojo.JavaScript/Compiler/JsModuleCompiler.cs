using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal sealed class JsModuleCompiler(JsRealm realm) : JsCompilerBase(realm)
{
    private readonly List<ModuleHoistedFunction> hoistedFunctions = [];
    private bool deferHoistedFunctions;

    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = JavaScriptParser.ParseModule(source, sourcePath);
        return Compile(ast);
    }

    public JsScript Compile(JsAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        if (!ast.IsModule)
            throw new ArgumentException("A module JsAst is required.", nameof(ast));
        builder.SetSourceText(ast.SourceText);
        strictDeclared = true;
        isAsync = ast.HasTopLevelAwait;
        builder.SetStrictDeclared(true);
        using var collected = CompilerBindingCollector.Collect(ast);
        using var plan = CompilerStoragePlanner.Plan(collected, ast);
        InitializePlanIndexes(collected, plan);
        EmitGeneratorPrologue();
        InitializeRootBindings();
        PrepareLexicalHoleInitializationSkips(ast, ast.Root);
        EmitModuleContextSetup();
        EmitScopeLexicalHoleInitialization();
        EmitNamespaceImports(ast);
        EmitDeclarationPrologue(ast, ast.Root);

        var rootIndex = ast.Root;
        hasActiveModuleTopLevelExplicitResourceScope =
            ast.HasTopLevelUsingLike || ast.HasTopLevelAwaitUsingLike;
        moduleTopLevelExplicitResourceScopeIsAsync =
            hasActiveModuleTopLevelExplicitResourceScope && ast.HasTopLevelAwait;
        try
        {
            var statements = ast.ChildRange(ast[rootIndex].Arg0, ast[rootIndex].Arg1);
            for (var i = 0; i < statements.Length; i++)
                EmitStatement(ast, statements[i]);
        }
        finally
        {
            hasActiveModuleTopLevelExplicitResourceScope = false;
            moduleTopLevelExplicitResourceScopeIsAsync = false;
        }

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        PatchGeneratorSwitchTable();
        var bodyScript = builder.ToScript(new SourceCode(ast.SourceText, ast.SourcePath));
        bodyScript.BindAgent(Vm.Agent);
        return ast.HasTopLevelAwait ? WrapAsyncModule(bodyScript, ast) : bodyScript;
    }

    internal ModuleExecutionCompilation CompileForExecution(JsAst ast)
    {
        deferHoistedFunctions = true;
        var script = Compile(ast);
        var initialContextSlots = new JsValue[rootContextSlotCount];
        Array.Fill(initialContextSlots, JsValue.Undefined);
        var bindings = GetPlannedBindings(0);
        for (var i = 0; i < bindings.Length; i++)
            if (
                bindings[i].StorageKind == CompilerPlannedStorageKind.ContextSlot
                && bindings[i].Kind
                    is not (
                        CompilerCollectedBindingKind.Var
                        or CompilerCollectedBindingKind.FunctionDeclaration
                    )
            )
                initialContextSlots[bindings[i].StorageIndex] = JsValue.TheHole;
        return new(script, initialContextSlots, hoistedFunctions.ToArray());
    }

    internal JsScript WrapAsyncModule(JsScript bodyScript, JsAst ast)
    {
        var function = new JsBytecodeFunction(
            Vm,
            bodyScript,
            string.Empty,
            requiresClosureBinding: false,
            isStrict: true,
            kind: JsBytecodeFunctionKind.Async,
            formalParameterCount: 0,
            hasSimpleParameterList: true,
            expectedArgumentCount: 0
        );
        using var wrapper = new BytecodeBuilder(Vm);
        wrapper.SetSourceText(ast.SourceText);
        wrapper.SetStrictDeclared(true);
        wrapper.Emit(JsOpCode.CreateClosure, (byte)wrapper.AddObjectConstant(function), 0);
        var functionRegister = wrapper.AllocateTemporaryRegister();
        wrapper.Emit(JsOpCode.Star, (byte)functionRegister);
        wrapper.EmitCallUndefinedReceiver(functionRegister, 0, 0);
        wrapper.Emit(JsOpCode.Return);
        var script = wrapper.ToScript(new SourceCode(ast.SourceText, ast.SourcePath));
        script.BindAgent(Vm.Agent);
        return script;
    }

    protected override bool DeferHoistedFunction(
        in BindingStorage binding,
        JsBytecodeFunction function
    )
    {
        if (!deferHoistedFunctions || activeScopes.Peek().ScopeId != 0)
            return false;
        var storageKind = binding.Planned.StorageKind switch
        {
            CompilerPlannedStorageKind.ModuleBinding => ModuleHoistedFunctionStorageKind.ModuleCell,
            CompilerPlannedStorageKind.ContextSlot => ModuleHoistedFunctionStorageKind.ContextSlot,
            _ => (ModuleHoistedFunctionStorageKind?)null,
        };
        if (storageKind is null)
            return false;
        hoistedFunctions.Add(new(function, storageKind.Value, binding.Planned.StorageIndex));
        return true;
    }

    private void EmitNamespaceImports(JsAst ast)
    {
        foreach (ref readonly var import in ast.ImportEntries)
        {
            if (import.Kind != JsImportKind.Namespace)
                continue;

            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var arguments = builder.AllocateTemporaryRegisterBlock(2);
                ref readonly var request = ref ast.ModuleRequests[import.ModuleRequestIndex];
                EmitStringLiteral(ast.GetString(request.SpecifierStringIndex));
                EmitStar(arguments);
                var importType = GetImportType(ast, request);
                if (importType is null)
                    builder.EmitLda(JsOpCode.LdaUndefined);
                else
                    EmitStringLiteral(importType);
                EmitStar(arguments + 1);
                builder.EmitCallRuntime((int)RuntimeId.GetCurrentModuleNamespace, arguments, 2);
                var localName = ast.GetString(import.LocalNameStringIndex);
                if (!TryResolveBinding(localName, out var binding))
                    throw new InvalidOperationException(
                        $"No planned binding found for namespace import '{localName}'."
                    );
                EmitStore(binding, isInitialization: true);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
        }
    }

    private static string? GetImportType(JsAst ast, in JsModuleRequest request)
    {
        var attributes = ast.GetImportAttributes(request);
        for (var i = 0; i < attributes.Length; i++)
            if (
                string.Equals(
                    ast.GetString(attributes[i].KeyStringIndex),
                    "type",
                    StringComparison.Ordinal
                )
            )
                return ast.GetString(attributes[i].ValueStringIndex);
        return null;
    }
}
