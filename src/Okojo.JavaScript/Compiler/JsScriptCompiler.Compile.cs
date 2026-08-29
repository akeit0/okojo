using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal sealed partial class JsScriptCompiler
{
    public JsScript Compile(string source, string? sourcePath = null)
    {
        using var ast = JavaScriptParser.ParseScript(source, sourcePath);
        return Compile(ast, sourcePath);
    }

    internal JsScript Compile(JsAst ast, string? sourcePath)
    {
        return Compile(
            ast,
            sourcePath,
            ephemeralTopLevelLocality: false,
            suppressTopLevelLexicalRegistration: false,
            validateGlobalDeclarations: true
        );
    }

    internal JsScript Compile(JsAst ast) => Compile(ast, ast.SourcePath);

    /// <summary>
    ///     Compiles an indirect-eval body. Sloppy eval keeps var/function bindings
    ///     on the global object but its lexicals stay ephemeral; strict eval keeps
    ///     every declaration in the eval's own environment.
    /// </summary>
    internal JsScript CompileIndirectEval(JsAst ast, string? sourcePath)
    {
        var strict = ast.StrictDeclared;
        return Compile(
            ast,
            sourcePath,
            ephemeralTopLevelLocality: strict,
            suppressTopLevelLexicalRegistration: true,
            validateGlobalDeclarations: !strict
        );
    }

    private JsScript Compile(
        JsAst ast,
        string? sourcePath,
        bool ephemeralTopLevelLocality,
        bool suppressTopLevelLexicalRegistration,
        bool validateGlobalDeclarations
    )
    {
        scriptSourceCode =
            string.IsNullOrEmpty(ast.SourceText) && sourcePath is null
                ? null
                : new SourceCode(ast.SourceText, sourcePath);
        builder.SetSourceText(ast.SourceText);
        strictDeclared = ast.StrictDeclared;
        isAsync = ast.HasTopLevelAwait;
        builder.SetStrictDeclared(strictDeclared);
        using var collected = CompilerBindingCollector.Collect(ast);
        if (validateGlobalDeclarations)
            ValidateGlobalDeclarations(
                collected,
                allowEphemeralTopLevelLexicals: suppressTopLevelLexicalRegistration
            );
        using var plan = CompilerStoragePlanner.Plan(
            collected,
            null,
            ephemeralProgramScopeLocality: ephemeralTopLevelLocality
        );
        InitializePlanIndexes(collected, plan);
        InitializeRootBindings();
        PrepareLexicalHoleInitializationSkips(ast, ast.Root);
        EmitFunctionContextSetup();
        EmitScopeLexicalHoleInitialization();
        EmitDeclarationPrologue(ast, ast.Root);

        var rootIndex = ast.Root;
        var bodyOffset = ast[rootIndex].Arg0;
        var bodyCount = ast[rootIndex].Arg1;

        // A script's completion value (read by eval and the embedding Evaluate
        // API) is the value of its last executed expression statement, carried
        // forward through non-producing statements. Mirrors V8's rewriter.cc:
        // iteration/try/if-without-else statements are prefixed with an
        // undefined store (so zero-iteration loops and bare breaks reset the
        // completion), expression statements capture the accumulator, and the
        // finally suppression in the try emitter keeps finalizers out.
        var completionRegister = builder.AllocatePinnedRegister();
        builder.EmitLda(JsOpCode.LdaUndefined);
        EmitStar(completionRegister);
        SetCompletionSink(completionRegister);

        EmitBodyStatementListWithResources(
            ast,
            bodyOffset,
            bodyCount,
            () => EmitScriptRootStatements(ast, bodyOffset, bodyCount)
        );

        ClearCompletionSink();
        EmitLdar(completionRegister);
        builder.Emit(JsOpCode.Return);
        var lexicalMetadata = BuildTopLevelLexicalMetadata();
        var script = builder.ToScript(
            sourceCode: scriptSourceCode,
            topLevelLexicalAtoms: lexicalMetadata?.Atoms,
            topLevelLexicalSlots: lexicalMetadata?.Slots,
            topLevelLexicalConstFlags: lexicalMetadata?.ConstFlags,
            suppressTopLevelLexicalRegistration: suppressTopLevelLexicalRegistration
        );
        script.BindAgent(Vm.Agent);
        builder.Dispose();
        var result = ast.HasTopLevelAwait
            ? new JsModuleCompiler(Vm).WrapAsyncModule(script, ast)
            : script;
        ReleasePlanStorage();
        return result;
    }

    /// <summary>
    ///     Emits the script root statement list with completion-value semantics,
    ///     mirroring V8's rewriter.cc Processor: statements that can complete
    ///     without producing a value (iterations, try, if without else, switch)
    ///     are prefixed with an undefined store so they reset the completion
    ///     instead of carrying a stale value forward; value-producing statements
    ///     capture through the active completion sink; blocks and labels recurse.
    ///     C4: statements before the last sink-killing statement (one that
    ///     guarantees a value or carries a reset) emit with the sink suppressed -
    ///     their completion values are overwritten before the unit end reads it.
    /// </summary>
    private void EmitScriptRootStatements(JsAst ast, int bodyOffset, int bodyCount)
    {
        var statements = ast.ChildRange(bodyOffset, bodyCount);
        var firstLiveIndex = statements.Length;
        for (var i = statements.Length - 1; i >= 0; i--)
            if (
                StatementGuaranteesCompletionValue(ast, statements[i])
                || StatementNeedsCompletionReset(ast, statements[i])
            )
            {
                // Largest kill index: every earlier statement's sink traffic
                // is overwritten here before the unit end reads the sink.
                firstLiveIndex = i;
                break;
            }

        for (var i = 0; i < statements.Length; i++)
        {
            SetSuppressCompletionSink(i < firstLiveIndex);
            EmitStatement(ast, statements[i]);
        }

        SetSuppressCompletionSink(false);
    }

    private void ValidateGlobalDeclarations(
        CompilerBindingCollectionResult collected,
        bool allowEphemeralTopLevelLexicals = false
    )
    {
        // Root binding counts are tiny; a linear list beats a HashSet allocation.
        List<string> seen = [];
        foreach (ref readonly var binding in collected.Bindings)
        {
            if (binding.ScopeId != 0)
                continue;

            // AnnexB B.3.3 function declarations that are direct if-statement
            // consequents/alternates do not participate in global declaration
            // instantiation conflicts: when creating their variable-like binding
            // would produce an early error, the binding is skipped silently.
            if (
                collected.AnnexBIfFunctionNames is { } conditionalNames
                && conditionalNames.Contains(binding.Name)
            )
                continue;

            if (seen.Contains(binding.Name))
                throw GlobalDeclarationError(
                    JsErrorKind.SyntaxError,
                    binding.Name,
                    "SCRIPT_GLOBAL_DUPLICATE_DECLARATION"
                );
            seen.Add(binding.Name);

            var atom = Vm.Atoms.InternNoCheck(binding.Name);
            if (
                binding.Kind
                is CompilerCollectedBindingKind.Lexical
                    or CompilerCollectedBindingKind.ClassDeclaration
            )
            {
                if (allowEphemeralTopLevelLexicals)
                {
                    // Sloppy indirect eval lexicals live in the eval's ephemeral
                    // environment; they never interact with persistent globals.
                    continue;
                }
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
                && bindings[i].Kind
                    is CompilerCollectedBindingKind.Lexical
                        or CompilerCollectedBindingKind.ClassDeclaration
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
            if (binding.StorageKind != CompilerPlannedStorageKind.ContextSlot)
                continue;
            if (
                binding.Kind
                is not (
                    CompilerCollectedBindingKind.Lexical
                    or CompilerCollectedBindingKind.ClassDeclaration
                )
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
