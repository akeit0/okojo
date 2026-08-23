using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class JsPlannedModuleCompiler(JsRealm realm) : JsPlannedCompilerBase(realm)
{
    private readonly List<ModuleHoistedFunction> hoistedFunctions = [];
    private bool deferHoistedFunctions;

    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = FlatJavaScriptParser.ParseModule(source, sourcePath);
        return Compile(ast);
    }

    public JsScript Compile(FlatAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        if (!ast.IsModule)
            throw new ArgumentException("A module FlatAst is required.", nameof(ast));
        builder.SetSourceText(ast.SourceText);
        strictDeclared = true;
        builder.SetStrictDeclared(true);
        using var collected = CompilerBindingCollector.Collect(ast);
        using var plan = CompilerStoragePlanner.Plan(collected, ast);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitModuleContextSetup();
        EmitScopeLexicalHoleInitialization();
        EmitNamespaceImports(ast);
        EmitDeclarationPrologue(ast, ast.Root);

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.Emit(JsOpCode.Return);
        var script = builder.ToScript() with
        {
            SourceCode = new SourceCode(ast.SourceText, ast.SourcePath),
            StrictDeclared = true,
        };
        script.BindAgent(Vm.Agent);
        return script;
    }

    internal ModuleExecutionCompilation CompileForExecution(FlatAst ast)
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

    protected override bool DeferHoistedFunction(
        in BindingStorage binding,
        JsBytecodeFunction function
    )
    {
        if (!deferHoistedFunctions)
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

    private void EmitNamespaceImports(FlatAst ast)
    {
        foreach (ref readonly var import in ast.ImportEntries)
        {
            if (import.Kind != FlatImportKind.Namespace)
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

    private static string? GetImportType(FlatAst ast, in FlatModuleRequest request)
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
