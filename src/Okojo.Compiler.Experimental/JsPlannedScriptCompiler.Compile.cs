using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    public JsScript Compile(JsProgram program)
    {
        using var ast = FlatAstLowerer.Lower(program);
        ast.StrictDeclared = program.StrictDeclared;
        return Compile(ast, program.SourcePath);
    }

    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = FlatJavaScriptParser.ParseScript(source, sourcePath);
        return Compile(ast, sourcePath);
    }

    private JsScript Compile(FlatAst ast, string? sourcePath)
    {
        builder.SetSourceText(ast.SourceText);
        builder.SetStrictDeclared(ast.StrictDeclared);
        using var collected = CompilerBindingCollector.Collect(ast);
        ValidateGlobalDeclarations(collected);
        using var plan = CompilerStoragePlanner.Plan(collected);
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        EmitFunctionContextSetup();
        EmitScopeLexicalHoleInitialization();
        EmitDeclarationPrologue(ast, ast.Root);

        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);

        if (statements.Length == 0)
            builder.EmitLda(JsOpCode.LdaUndefined);

        builder.Emit(JsOpCode.Return);
        var lexicalMetadata = BuildTopLevelLexicalMetadata();
        var script = builder.ToScript() with
        {
            SourceCode =
                string.IsNullOrEmpty(ast.SourceText) && sourcePath is null
                    ? null
                    : new SourceCode(ast.SourceText, sourcePath),
            StrictDeclared = ast.StrictDeclared,
            TopLevelLexicalAtoms = lexicalMetadata?.Atoms,
            TopLevelLexicalSlots = lexicalMetadata?.Slots,
            TopLevelLexicalConstFlags = lexicalMetadata?.ConstFlags,
        };
        script.BindAgent(Vm.Agent);
        return script;
    }

    private void ValidateGlobalDeclarations(CompilerBindingCollectionResult collected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ref readonly var binding in collected.Bindings)
        {
            if (binding.ScopeId != 0)
                continue;
            if (!seen.Add(binding.Name))
                throw GlobalDeclarationError(
                    JsErrorKind.SyntaxError,
                    binding.Name,
                    "SCRIPT_GLOBAL_DUPLICATE_DECLARATION"
                );

            var atom = Vm.Atoms.InternNoCheck(binding.Name);
            if (binding.Kind is CompilerCollectedBindingKind.Lexical)
            {
                if (
                    Vm.HasGlobalLexicalBindingAtom(atom)
                    || Vm.GlobalObject.HasRestrictedGlobalPropertyAtom(atom)
                )
                    throw GlobalDeclarationError(
                        JsErrorKind.SyntaxError,
                        binding.Name,
                        "SCRIPT_GLOBAL_LEXICAL_CONFLICT"
                    );
                continue;
            }

            if (
                binding.Kind
                is not (
                    CompilerCollectedBindingKind.Var
                    or CompilerCollectedBindingKind.FunctionDeclaration
                )
            )
                continue;
            if (Vm.HasGlobalLexicalBindingAtom(atom))
                throw GlobalDeclarationError(
                    JsErrorKind.SyntaxError,
                    binding.Name,
                    "SCRIPT_GLOBAL_VAR_LEXICAL_CONFLICT"
                );
            var canDeclare =
                binding.Kind == CompilerCollectedBindingKind.FunctionDeclaration
                    ? Vm.GlobalObject.CanDeclareGlobalFunctionAtom(atom)
                    : Vm.GlobalObject.CanDeclareGlobalVarAtom(atom);
            if (!canDeclare)
                throw GlobalDeclarationError(
                    JsErrorKind.TypeError,
                    binding.Name,
                    binding.Kind == CompilerCollectedBindingKind.FunctionDeclaration
                        ? "SCRIPT_GLOBAL_FUNCTION_NOT_DEFINABLE"
                        : "SCRIPT_GLOBAL_VAR_NOT_DEFINABLE"
                );
        }

        JsRuntimeException GlobalDeclarationError(JsErrorKind kind, string name, string code) =>
            new(kind, $"Identifier '{name}' has already been declared", code);
    }

    private (int[] Atoms, int[] Slots, bool[] ConstFlags)? BuildTopLevelLexicalMetadata()
    {
        var bindings = GetPlannedBindings(0);
        var count = 0;
        for (var i = 0; i < bindings.Length; i++)
            if (
                bindings[i].StorageKind == CompilerPlannedStorageKind.ContextSlot
                && bindings[i].Kind == CompilerCollectedBindingKind.Lexical
            )
                count++;
        if (count == 0)
            return null;

        var atoms = new int[count];
        var slots = new int[count];
        var constFlags = new bool[count];
        var index = 0;
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            if (
                binding.StorageKind != CompilerPlannedStorageKind.ContextSlot
                || binding.Kind != CompilerCollectedBindingKind.Lexical
            )
                continue;
            atoms[index] = Vm.Atoms.InternNoCheck(binding.Name);
            slots[index] = binding.StorageIndex;
            constFlags[index] = binding.IsConst;
            index++;
        }
        return (atoms, slots, constFlags);
    }
}
