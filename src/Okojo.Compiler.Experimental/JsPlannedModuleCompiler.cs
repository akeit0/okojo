using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class JsPlannedModuleCompiler(JsRealm realm) : JsPlannedCompilerBase(realm)
{
    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = FlatJavaScriptParser.ParseModule(source, sourcePath);
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
            SourceCode = new SourceCode(ast.SourceText, sourcePath),
            StrictDeclared = true,
        };
        script.BindAgent(Vm.Agent);
        return script;
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
