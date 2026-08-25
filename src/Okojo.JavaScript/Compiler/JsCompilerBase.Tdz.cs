using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
{
    private static readonly HashSet<CompilerPlannedBinding> EmptyLexicalSet = [];
    private HashSet<CompilerPlannedBinding>? knownInitializedLexicals;
    private HashSet<CompilerPlannedBinding>? skippedLexicalHoleInitializations;
    private bool suppressKnownInitializedLexicalTracking;
    private bool emittingLexicalHoleInitialization;

    private bool IsKnownInitializedLexical(in BindingStorage binding)
    {
        return binding.Planned.StorageKind
                is CompilerPlannedStorageKind.LexicalRegister
                    or CompilerPlannedStorageKind.ContextSlot
            && knownInitializedLexicals?.Contains(binding.Planned) == true;
    }

    private void MarkKnownInitializedLexical(in BindingStorage binding)
    {
        if (
            !suppressKnownInitializedLexicalTracking
            && !emittingLexicalHoleInitialization
            && binding.Planned.StorageKind
                is CompilerPlannedStorageKind.LexicalRegister
                    or CompilerPlannedStorageKind.ContextSlot
        )
            (knownInitializedLexicals ??= []).Add(binding.Planned);
    }

    protected void MarkInitializedParameters()
    {
        var bindings = activeScopes.Peek().Bindings;
        for (var i = 0; i < bindings.Count; i++)
            if (bindings[i].Planned.Kind == CompilerCollectedBindingKind.Parameter)
                MarkKnownInitializedLexical(bindings[i]);
    }

    private void RemoveKnownInitializedLexicals(IReadOnlyList<BindingStorage> bindings)
    {
        if (knownInitializedLexicals is null)
            return;
        for (var i = 0; i < bindings.Count; i++)
            knownInitializedLexicals.Remove(bindings[i].Planned);
    }

    private HashSet<CompilerPlannedBinding> CaptureKnownInitializedLexicals()
    {
        return knownInitializedLexicals is { Count: > 0 }
            ? new(knownInitializedLexicals)
            : EmptyLexicalSet;
    }

    private void RestoreKnownInitializedLexicals(HashSet<CompilerPlannedBinding> snapshot)
    {
        if (snapshot.Count == 0)
        {
            knownInitializedLexicals?.Clear();
            return;
        }
        var current = knownInitializedLexicals ??= [];
        current.Clear();
        current.UnionWith(snapshot);
    }

    private void MergeKnownInitializedLexicals(
        HashSet<CompilerPlannedBinding> first,
        HashSet<CompilerPlannedBinding> second
    )
    {
        if (first.Count == 0 || second.Count == 0)
        {
            knownInitializedLexicals?.Clear();
            return;
        }
        var current = knownInitializedLexicals ??= [];
        current.Clear();
        current.UnionWith(first);
        current.IntersectWith(second);
    }

    private void BeginLexicalHoleInitialization()
    {
        emittingLexicalHoleInitialization = true;
    }

    private void EndLexicalHoleInitialization()
    {
        emittingLexicalHoleInitialization = false;
    }

    private bool PushSuppressKnownInitializedLexicalTracking()
    {
        var previous = suppressKnownInitializedLexicalTracking;
        suppressKnownInitializedLexicalTracking = true;
        return previous;
    }

    private void RestoreKnownInitializedLexicalTracking(bool previous)
    {
        suppressKnownInitializedLexicalTracking = previous;
    }

    protected void PrepareLexicalHoleInitializationSkips(JsAst ast, int bodyRoot)
    {
        skippedLexicalHoleInitializations?.Clear();
        var body = ast[bodyRoot];
        var statements = ast.ChildRange(body.Arg0, body.Arg1);
        var prefix = new List<int>(statements.Length);
        for (var i = 0; i < statements.Length; i++)
        {
            var statementIndex = statements[i];
            if (ast[statementIndex].Kind == AstKind.FunctionDeclaration)
                continue;
            if (!TryGetSafeLexicalDeclaration(ast, statementIndex, out var binding))
                break;

            var referencesEarlier = false;
            for (var j = 0; j < prefix.Count; j++)
                if (StatementReferencesIdentifier(ast, prefix[j], binding.Planned.Name))
                {
                    referencesEarlier = true;
                    break;
                }

            if (!referencesEarlier)
                (skippedLexicalHoleInitializations ??= []).Add(binding.Planned);
            prefix.Add(statementIndex);
        }
    }

    protected void PrepareLoopLexicalHoleInitializationSkip(
        JsAst ast,
        int declarationIndex,
        int scopeId
    )
    {
        if (!TryGetSafeLexicalDeclaration(ast, declarationIndex, out var name, out var initializer))
            return;
        if (!CanInitializeWithoutUserCode(ast, initializer))
            return;

        var bindings = GetPlannedBindings(scopeId);
        for (var i = 0; i < bindings.Length; i++)
            if (
                string.Equals(bindings[i].Name, name, StringComparison.Ordinal)
                && bindings[i].StorageKind == CompilerPlannedStorageKind.LexicalRegister
            )
            {
                (skippedLexicalHoleInitializations ??= []).Add(bindings[i]);
                return;
            }
    }

    private bool TryGetSafeLexicalDeclaration(
        JsAst ast,
        int statementIndex,
        out BindingStorage binding
    )
    {
        binding = default;
        if (!TryGetSafeLexicalDeclaration(ast, statementIndex, out var name, out var initializer))
            return false;
        if (!CanInitializeWithoutUserCode(ast, initializer))
            return false;
        return TryResolveBinding(name, out binding)
            && binding.Planned.StorageKind == CompilerPlannedStorageKind.LexicalRegister;
    }

    private static bool TryGetSafeLexicalDeclaration(
        JsAst ast,
        int statementIndex,
        out string name,
        out int initializer
    )
    {
        name = string.Empty;
        initializer = -1;
        ref readonly var statement = ref ast[statementIndex];
        if (statement.Kind != AstKind.VariableDeclaration)
            return false;
        if (!((JsVariableDeclarationKind)statement.Arg2).IsLexical())
            return false;

        var declarators = ast.ChildRange(statement.Arg0, statement.Arg1);
        if (declarators.Length != 1)
            return false;
        ref readonly var declarator = ref ast[declarators[0]];
        if (declarator.Kind != AstKind.VariableDeclarator)
            return false;
        name = ast.GetString(declarator.Arg0);
        initializer = declarator.Arg2;
        return initializer >= 0;
    }

    private static bool CanInitializeWithoutUserCode(JsAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.NumericLiteral:
            case AstKind.BigIntLiteral:
            case AstKind.StringLiteral:
            case AstKind.BooleanLiteral:
            case AstKind.NullLiteral:
            case AstKind.FunctionExpression:
            case AstKind.ArrowFunctionExpression:
                return true;
            case AstKind.ArrayExpression:
            {
                var elements = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                    if (elements[i] >= 0 && !CanInitializeWithoutUserCode(ast, elements[i]))
                        return false;
                return true;
            }
            case AstKind.ObjectExpression:
            {
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                {
                    ref readonly var property = ref properties[i];
                    if (
                        property.IsComputed
                        || property.IsAccessor
                        || property.IsRest
                        || !CanInitializeWithoutUserCode(ast, property.ValueNode)
                    )
                        return false;
                }
                return true;
            }
            default:
                return false;
        }
    }

    private static bool StatementReferencesIdentifier(JsAst ast, int nodeIndex, string name)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.FunctionDeclaration:
                return false;
            case AstKind.ExpressionStatement:
            case AstKind.ReturnStatement:
            case AstKind.ThrowStatement:
                return node.Arg0 >= 0 && ExpressionReferencesIdentifier(ast, node.Arg0, name);
            case AstKind.VariableDeclaration:
            {
                var declarators = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < declarators.Length; i++)
                {
                    ref readonly var declarator = ref ast[declarators[i]];
                    var initializer =
                        declarator.Kind == AstKind.VariableDeclaratorPattern
                            ? declarator.Arg1
                            : declarator.Arg2;
                    if (initializer >= 0 && ExpressionReferencesIdentifier(ast, initializer, name))
                        return true;
                }
                return false;
            }
            case AstKind.BlockStatement:
            case AstKind.Program:
            {
                var statements = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < statements.Length; i++)
                    if (StatementReferencesIdentifier(ast, statements[i], name))
                        return true;
                return false;
            }
            case AstKind.IfStatement:
                return ExpressionReferencesIdentifier(ast, node.Arg0, name)
                    || StatementReferencesIdentifier(ast, node.Arg1, name)
                    || node.Arg2 >= 0 && StatementReferencesIdentifier(ast, node.Arg2, name);
            case AstKind.WhileStatement:
            case AstKind.DoWhileStatement:
                return ExpressionReferencesIdentifier(ast, node.Arg0, name)
                    || StatementReferencesIdentifier(ast, node.Arg1, name);
            case AstKind.ForStatement:
            case AstKind.ForInOfStatement:
            {
                var parts = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < parts.Length; i++)
                    if (
                        parts[i] >= 0
                        && (
                            ast[parts[i]].Kind
                                is AstKind.VariableDeclaration
                                    or AstKind.BlockStatement
                                    or AstKind.ExpressionStatement
                                ? StatementReferencesIdentifier(ast, parts[i], name)
                                : ExpressionReferencesIdentifier(ast, parts[i], name)
                        )
                    )
                        return true;
                return false;
            }
            case AstKind.LabeledStatement:
                return StatementReferencesIdentifier(ast, node.Arg1, name);
            case AstKind.TryStatement:
                return StatementReferencesIdentifier(ast, node.Arg0, name)
                    || node.Arg1 >= 0 && StatementReferencesIdentifier(ast, node.Arg1, name)
                    || node.Arg2 >= 0 && StatementReferencesIdentifier(ast, node.Arg2, name);
            case AstKind.SwitchStatement:
                if (ExpressionReferencesIdentifier(ast, node.Arg0, name))
                    return true;
                var cases = ast.ChildRange(node.Arg1, node.Arg2);
                for (var i = 0; i < cases.Length; i++)
                {
                    ref readonly var switchCase = ref ast[cases[i]];
                    if (
                        switchCase.Arg0 >= 0
                        && ExpressionReferencesIdentifier(ast, switchCase.Arg0, name)
                    )
                        return true;
                    var statements = ast.ChildRange(switchCase.Arg1, switchCase.Arg2);
                    for (var j = 0; j < statements.Length; j++)
                        if (StatementReferencesIdentifier(ast, statements[j], name))
                            return true;
                }
                return false;
            case AstKind.ExportDeclaration:
                return node.Arg0 >= 0 && StatementReferencesIdentifier(ast, node.Arg0, name);
            case AstKind.ClassDeclaration:
                return true;
            default:
                return false;
        }
    }

    private static bool ExpressionReferencesIdentifier(JsAst ast, int nodeIndex, string name)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.Identifier:
                return string.Equals(ast.GetString(node.Arg0), name, StringComparison.Ordinal);
            case AstKind.FunctionExpression:
            case AstKind.ArrowFunctionExpression:
                return false;
            case AstKind.AssignmentExpression:
            case AstKind.BinaryExpression:
            case AstKind.ConditionalExpression:
                return ExpressionReferencesIdentifier(ast, node.Arg0, name)
                    || node.Arg1 >= 0 && ExpressionReferencesIdentifier(ast, node.Arg1, name)
                    || node.Kind == AstKind.ConditionalExpression
                        && node.Arg2 >= 0
                        && ExpressionReferencesIdentifier(ast, node.Arg2, name);
            case AstKind.UnaryExpression:
            case AstKind.UpdateExpression:
            case AstKind.SpreadElement:
            case AstKind.OptionalChainExpression:
            case AstKind.YieldExpression:
            case AstKind.AwaitExpression:
                return node.Arg0 >= 0 && ExpressionReferencesIdentifier(ast, node.Arg0, name);
            case AstKind.ImportCallExpression:
                return ExpressionReferencesIdentifier(ast, node.Arg0, name)
                    || node.Arg1 >= 0 && ExpressionReferencesIdentifier(ast, node.Arg1, name);
            case AstKind.CallExpression:
            case AstKind.OptionalCallExpression:
            case AstKind.NewExpression:
            {
                if (ExpressionReferencesIdentifier(ast, node.Arg0, name))
                    return true;
                var arguments = ast.ChildRange(node.Arg1, node.Arg2);
                for (var i = 0; i < arguments.Length; i++)
                    if (ExpressionReferencesIdentifier(ast, arguments[i], name))
                        return true;
                return false;
            }
            case AstKind.MemberExpression:
                return ExpressionReferencesIdentifier(ast, node.Arg0, name)
                    || (node.Arg2 & (int)AstMemberFlags.Computed) != 0
                        && ExpressionReferencesIdentifier(ast, node.Arg1, name);
            case AstKind.SequenceExpression:
            case AstKind.TemplateExpression:
            {
                var expressions = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < expressions.Length; i++)
                    if (
                        expressions[i] >= 0
                        && ExpressionReferencesIdentifier(ast, expressions[i], name)
                    )
                        return true;
                return false;
            }
            case AstKind.TaggedTemplateExpression:
            {
                if (ExpressionReferencesIdentifier(ast, node.Arg0, name))
                    return true;
                var expressions = ast.ChildRange(node.Arg1, node.Arg2);
                for (var i = 0; i < expressions.Length; i++)
                    if (
                        expressions[i] >= 0
                        && ExpressionReferencesIdentifier(ast, expressions[i], name)
                    )
                        return true;
                return false;
            }
            case AstKind.ArrayExpression:
            {
                var elements = ast.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                    if (elements[i] >= 0 && ExpressionReferencesIdentifier(ast, elements[i], name))
                        return true;
                return false;
            }
            case AstKind.ObjectExpression:
            {
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                {
                    ref readonly var property = ref properties[i];
                    if (
                        property.IsComputed
                            && ExpressionReferencesIdentifier(ast, property.Key, name)
                        || ExpressionReferencesIdentifier(ast, property.ValueNode, name)
                    )
                        return true;
                }
                return false;
            }
            case AstKind.ClassExpression:
                return true;
            default:
                return false;
        }
    }
}
