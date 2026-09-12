using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal sealed partial class JsScriptCompiler
{
    public JsScript Compile(string source, string? sourcePath = null) =>
        CompileUnit(source, sourcePath).Link(TargetRealm);

    internal JsScript Compile(JsAst ast, string? sourcePath) =>
        CompileUnit(ast, sourcePath).Link(TargetRealm);

    internal JsScript Compile(JsAst ast) => Compile(ast, ast.SourcePath);

    internal JsCompilationUnit CompileUnit(string source, string? sourcePath = null)
    {
        using var ast = JavaScriptParser.ParseScript(source, sourcePath);
        return CompileUnit(ast, sourcePath);
    }

    internal JsCompilationUnit CompileUnit(JsAst ast, string? sourcePath) =>
        new(CompileCore(ast, sourcePath, false, false, true));

    // Sloppy indirect eval has ephemeral lexicals and global var/function bindings;
    // strict eval keeps all declarations in the eval environment.
    internal JsScript CompileIndirectEval(JsAst ast, string? sourcePath) =>
        new JsCompilationUnit(
            CompileCore(ast, sourcePath, ast.StrictDeclared, true, !ast.StrictDeclared)
        ).Link(TargetRealm);

    private JsFunctionDescriptor CompileCore(
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
        var declarations = validateGlobalDeclarations
            ? BuildGlobalDeclarationPlan(collected, suppressTopLevelLexicalRegistration)
            : null;
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
        var code = builder.ToCode(
            sourceCode: scriptSourceCode,
            topLevelLexicalNames: lexicalMetadata?.Names,
            topLevelLexicalSlots: lexicalMetadata?.Slots,
            topLevelLexicalConstFlags: lexicalMetadata?.ConstFlags,
            suppressTopLevelLexicalRegistration: suppressTopLevelLexicalRegistration,
            declarations: ast.HasTopLevelAwait ? null : declarations
        );
        builder.Dispose();
        var result = ast.HasTopLevelAwait
            ? JsModuleCompiler.WrapAsyncModule(Pool, code, ast, declarations)
            : new JsFunctionDescriptor(
                code,
                suppressTopLevelLexicalRegistration ? "eval" : "root",
                isStrict: strictDeclared
            );
        ReleaseCompilerStorage();
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

    private JsGlobalDeclarationPlan? BuildGlobalDeclarationPlan(
        CompilerBindingCollectionResult collected,
        bool allowEphemeralTopLevelLexicals
    )
    {
        var seen = Pool.RentCompileHashSet<string>(comparer: StringComparer.Ordinal);
        List<JsGlobalDeclaration> declarations = [];
        try
        {
            foreach (ref readonly var binding in collected.Bindings)
            {
                if (binding.ScopeId != 0)
                    continue;
                if (
                    collected.AnnexBIfFunctionNames is { } conditionalNames
                    && conditionalNames.Contains(binding.Name)
                )
                    continue;
                if (!seen.Add(binding.Name))
                    throw new JsRuntimeException(
                        JsErrorKind.SyntaxError,
                        $"Identifier '{binding.Name}' has already been declared",
                        "SCRIPT_GLOBAL_DUPLICATE_DECLARATION"
                    );
                switch (binding.Kind)
                {
                    case CompilerCollectedBindingKind.Lexical:
                    case CompilerCollectedBindingKind.ClassDeclaration:
                        if (!allowEphemeralTopLevelLexicals)
                            declarations.Add(new(binding.Name, JsGlobalDeclarationKind.Lexical));
                        break;
                    case CompilerCollectedBindingKind.Var:
                        declarations.Add(new(binding.Name, JsGlobalDeclarationKind.Var));
                        break;
                    case CompilerCollectedBindingKind.FunctionDeclaration:
                        declarations.Add(new(binding.Name, JsGlobalDeclarationKind.Function));
                        break;
                }
            }
            return declarations.Count == 0 ? null : new(declarations.ToArray());
        }
        finally
        {
            Pool.ReturnCompileHashSet(seen);
        }
    }

    private (string[] Names, int[] Slots, bool[] ConstFlags)? BuildTopLevelLexicalMetadata()
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

        var names = new string[count];
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
            names[index] = binding.Name;
            slots[index] = binding.StorageIndex;
            constFlags[index] = binding.IsConst;
            index++;
        }
        return (names, slots, constFlags);
    }
}
