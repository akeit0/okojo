using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static partial class CompilerBindingCollector
{
    internal const string SuperBaseBindingName = "\0super-base";

    public static CompilerBindingCollectionResult Collect(FlatAst ast)
    {
        var collector = new FlatCollector();
        collector.CollectBody(ast, ast.Root, 0);
        collector.AddSyntheticArgumentsBindings();
        return collector.MoveResult();
    }

    public static CompilerBindingCollectionResult CollectFunction(
        string? name,
        int nameId,
        FunctionParameterPlan parameterPlan,
        FlatAst ast,
        int bodyRoot,
        bool hasSelfBinding = false
    )
    {
        var collector = new FlatCollector(CompilerCollectedScopeKind.Function);
        collector.CollectFunctionRoot(name, nameId, parameterPlan, ast, bodyRoot, hasSelfBinding);
        collector.AddSyntheticArgumentsBindings();
        return collector.MoveResult();
    }

    public static CompilerBindingCollectionResult CollectFunction(
        FlatAst ast,
        in FlatFunctionInfo function,
        int bodyRoot,
        bool hasSelfBinding = false
    )
    {
        var collector = new FlatCollector(CompilerCollectedScopeKind.Function, function.IsArrow);
        collector.CollectFlatFunctionRoot(ast, function, bodyRoot, hasSelfBinding);
        collector.AddSyntheticArgumentsBindings();
        return collector.MoveResult();
    }

    private sealed class FlatCollector
    {
        private readonly PooledArrayBuilder<CompilerCollectedScope> scopes = new(16);
        private readonly PooledArrayBuilder<CompilerCollectedBinding> bindings = new(32);
        private readonly PooledArrayBuilder<CompilerCollectedReference> references = new(64);
        private readonly Dictionary<
            (int ScopeId, string Name),
            (CompilerCollectedBindingKind Kind, int Index)
        > mergeableBindings = new();
        private int nextScopeId = 1;
        private int parameterBodyScopeId = -1;

        public FlatCollector(
            CompilerCollectedScopeKind rootKind = CompilerCollectedScopeKind.Program,
            bool rootIsArrow = false
        )
        {
            scopes.Add(new CompilerCollectedScope(0, -1, rootKind, IsArrow: rootIsArrow));
        }

        public void CollectFunctionRoot(
            string? name,
            int nameId,
            FunctionParameterPlan parameterPlan,
            FlatAst ast,
            int bodyRoot,
            bool hasSelfBinding
        )
        {
            if (hasSelfBinding && !string.IsNullOrEmpty(name))
                AddBinding(
                    0,
                    CompilerCollectedBindingKind.FunctionNameSelf,
                    name!,
                    nameId,
                    position: ast.GetPosition(bodyRoot)
                );

            CollectParameters(parameterPlan, 0);
            CollectParameterInitializers(parameterPlan, 0);
            CollectBody(ast, bodyRoot, 0);
        }

        public void CollectFlatFunctionRoot(
            FlatAst ast,
            in FlatFunctionInfo function,
            int bodyRoot,
            bool hasSelfBinding
        )
        {
            var name = ast.GetString(function.NameStringIndex);
            if (hasSelfBinding)
                AddBinding(
                    0,
                    CompilerCollectedBindingKind.FunctionNameSelf,
                    name,
                    function.NameId,
                    position: function.Position
                );
            if (function.HasSuperPropertyReference)
                AddBinding(
                    0,
                    CompilerCollectedBindingKind.SuperBase,
                    SuperBaseBindingName,
                    -1,
                    isConst: true,
                    position: function.Position
                );

            CollectFlatParameters(ast, function, 0);
            CollectBody(ast, bodyRoot, 0);
        }

        public void CollectBody(FlatAst ast, int bodyRoot, int scopeId)
        {
            ref readonly var body = ref ast[bodyRoot];
            if (body.Kind != AstKind.Program)
                throw new InvalidOperationException(
                    $"Expected flat function/program root, found '{body.Kind}'."
                );

            var statements = ast.ChildRange(body.Arg0, body.Arg1);
            for (var i = 0; i < statements.Length; i++)
                VisitStatement(ast, statements[i], scopeId);
        }

        public CompilerBindingCollectionResult MoveResult()
        {
            return new(scopes, bindings, references);
        }

        public void AddSyntheticArgumentsBindings()
        {
            var hasArgumentsReference = false;
            for (var i = 0; i < references.Count; i++)
                if (string.Equals(references[i].Name, "arguments", StringComparison.Ordinal))
                {
                    hasArgumentsReference = true;
                    break;
                }
            if (!hasArgumentsReference)
                return;

            var hasBinding = new bool[scopes.Count];
            var required = new bool[scopes.Count];
            var varBindingIndex = new int[scopes.Count];
            Array.Fill(varBindingIndex, -1);
            for (var i = 0; i < bindings.Count; i++)
                if (string.Equals(bindings[i].Name, "arguments", StringComparison.Ordinal))
                {
                    if (bindings[i].Kind == CompilerCollectedBindingKind.Var)
                        varBindingIndex[bindings[i].ScopeId] = i;
                    else
                        hasBinding[bindings[i].ScopeId] = true;
                }

            for (var i = 0; i < references.Count; i++)
            {
                var reference = references[i];
                if (!string.Equals(reference.Name, "arguments", StringComparison.Ordinal))
                    continue;
                for (
                    var scopeId = reference.ScopeId;
                    scopeId >= 0;
                    scopeId = scopes[scopeId].ParentScopeId
                )
                {
                    if (hasBinding[scopeId])
                        break;
                    if (
                        scopes[scopeId].Kind == CompilerCollectedScopeKind.Function
                        && !scopes[scopeId].IsArrow
                    )
                    {
                        required[scopeId] = true;
                        break;
                    }
                }
            }

            for (var scopeId = 0; scopeId < required.Length; scopeId++)
                if (required[scopeId])
                {
                    var bindingIndex = varBindingIndex[scopeId];
                    if (bindingIndex >= 0)
                        bindings[bindingIndex] = bindings[bindingIndex] with
                        {
                            Kind = CompilerCollectedBindingKind.Arguments,
                        };
                    else
                        AddBinding(
                            scopeId,
                            CompilerCollectedBindingKind.Arguments,
                            "arguments",
                            position: scopes[scopeId].Position
                        );
                }
        }

        private void VisitStatement(FlatAst ast, int nodeIndex, int scopeId)
        {
            ref readonly var node = ref ast[nodeIndex];
            switch (node.Kind)
            {
                case AstKind.VariableDeclaration:
                    VisitVariableDeclaration(ast, node, scopeId);
                    return;
                case AstKind.BlockStatement:
                    VisitBlock(ast, nodeIndex, scopeId);
                    return;
                case AstKind.FunctionDeclaration:
                    VisitFunctionDeclaration(ast, node, scopeId);
                    return;
                case AstKind.ClassDeclaration:
                    VisitClass(ast, node, scopeId, isDeclaration: true);
                    return;
                case AstKind.IfStatement:
                    VisitExpression(ast, node.Arg0, scopeId);
                    VisitStatement(ast, node.Arg1, scopeId);
                    if (node.Arg2 >= 0)
                        VisitStatement(ast, node.Arg2, scopeId);
                    return;
                case AstKind.WhileStatement:
                    VisitExpression(ast, node.Arg0, scopeId);
                    VisitStatement(ast, node.Arg1, scopeId);
                    return;
                case AstKind.DoWhileStatement:
                    VisitStatement(ast, node.Arg0, scopeId);
                    VisitExpression(ast, node.Arg1, scopeId);
                    return;
                case AstKind.ForStatement:
                    VisitForStatement(ast, nodeIndex, scopeId);
                    return;
                case AstKind.ForInOfStatement:
                    VisitForInOfStatement(ast, nodeIndex, scopeId);
                    return;
                case AstKind.LabeledStatement:
                    VisitStatement(ast, node.Arg1, scopeId);
                    return;
                case AstKind.BreakStatement:
                case AstKind.ContinueStatement:
                    return;
                case AstKind.ReturnStatement:
                case AstKind.ExpressionStatement:
                case AstKind.ThrowStatement:
                    if (node.Arg0 >= 0)
                        VisitExpression(ast, node.Arg0, scopeId);
                    return;
                case AstKind.TryStatement:
                    VisitBlock(ast, node.Arg0, scopeId);
                    if (node.Arg1 >= 0)
                        VisitCatchClause(ast, node.Arg1, scopeId);
                    if (node.Arg2 >= 0)
                        VisitBlock(ast, node.Arg2, scopeId);
                    return;
                case AstKind.SwitchStatement:
                    VisitSwitchStatement(ast, nodeIndex, scopeId);
                    return;
                case AstKind.EmptyStatement:
                case AstKind.DebuggerStatement:
                    return;
                default:
                    throw new NotSupportedException(
                        $"Flat binding collection does not support statement '{node.Kind}'."
                    );
            }
        }

        private void VisitSwitchStatement(FlatAst ast, int nodeIndex, int parentScopeId)
        {
            ref readonly var statement = ref ast[nodeIndex];
            VisitExpression(ast, statement.Arg0, parentScopeId);
            var scopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            var cases = ast.ChildRange(statement.Arg1, statement.Arg2);
            for (var i = 0; i < cases.Length; i++)
            {
                ref readonly var switchCase = ref ast[cases[i]];
                if (switchCase.Arg0 >= 0)
                    VisitExpression(ast, switchCase.Arg0, scopeId);
                var statements = ast.ChildRange(switchCase.Arg1, switchCase.Arg2);
                for (var j = 0; j < statements.Length; j++)
                    VisitStatement(ast, statements[j], scopeId);
            }
        }

        private void VisitForStatement(FlatAst ast, int nodeIndex, int parentScopeId)
        {
            ref readonly var node = ref ast[nodeIndex];
            var parts = ast.ChildRange(node.Arg0, node.Arg1);
            var init = parts[0];
            var scopeId = parentScopeId;
            if (init >= 0 && ast[init].Kind == AstKind.VariableDeclaration)
            {
                ref readonly var declaration = ref ast[init];
                var declarationKind = (JsVariableDeclarationKind)declaration.Arg2;
                if (
                    declarationKind
                    is JsVariableDeclarationKind.Let
                        or JsVariableDeclarationKind.Const
                )
                {
                    scopeId = AddScope(
                        parentScopeId,
                        CompilerCollectedScopeKind.Block,
                        ast.GetPosition(nodeIndex)
                    );
                    var declarators = ast.ChildRange(declaration.Arg0, declaration.Arg1);
                    for (var i = 0; i < declarators.Length; i++)
                        VisitVariableDeclarator(
                            ast,
                            declarators[i],
                            scopeId,
                            CompilerCollectedBindingKind.LoopHeadAlias,
                            declarationKind == JsVariableDeclarationKind.Const
                        );
                }
                else
                {
                    VisitStatement(ast, init, scopeId);
                }
            }
            else if (init >= 0)
            {
                VisitExpression(ast, init, scopeId);
            }

            if (parts[1] >= 0)
                VisitExpression(ast, parts[1], scopeId);
            if (parts[2] >= 0)
                VisitExpression(ast, parts[2], scopeId);
            VisitStatement(ast, parts[3], scopeId);
        }

        private void VisitForInOfStatement(FlatAst ast, int nodeIndex, int parentScopeId)
        {
            ref readonly var node = ref ast[nodeIndex];
            var parts = ast.ChildRange(node.Arg0, node.Arg1);
            var left = parts[0];
            var scopeId = parentScopeId;
            if (ast[left].Kind == AstKind.VariableDeclaration)
            {
                ref readonly var declaration = ref ast[left];
                var declarationKind = (JsVariableDeclarationKind)declaration.Arg2;
                if (
                    declarationKind
                    is JsVariableDeclarationKind.Let
                        or JsVariableDeclarationKind.Const
                )
                {
                    scopeId = AddScope(
                        parentScopeId,
                        CompilerCollectedScopeKind.Block,
                        ast.GetPosition(nodeIndex)
                    );
                    var declarators = ast.ChildRange(declaration.Arg0, declaration.Arg1);
                    for (var i = 0; i < declarators.Length; i++)
                    {
                        ref readonly var declarator = ref ast[declarators[i]];
                        if (declarator.Kind == AstKind.VariableDeclaratorPattern)
                            VisitBindingPattern(
                                ast,
                                declarator.Arg0,
                                scopeId,
                                CompilerCollectedBindingKind.LoopHeadAlias,
                                declarationKind == JsVariableDeclarationKind.Const
                            );
                        else
                            AddBinding(
                                scopeId,
                                CompilerCollectedBindingKind.LoopHeadAlias,
                                ast.GetString(declarator.Arg0),
                                declarator.Arg1,
                                declarationKind == JsVariableDeclarationKind.Const,
                                ast.GetPosition(declarators[i])
                            );
                    }
                }
                else
                {
                    var bindingScopeId = FindVariableScope(parentScopeId);
                    var declarators = ast.ChildRange(declaration.Arg0, declaration.Arg1);
                    for (var i = 0; i < declarators.Length; i++)
                    {
                        ref readonly var declarator = ref ast[declarators[i]];
                        if (declarator.Kind == AstKind.VariableDeclaratorPattern)
                            VisitBindingPattern(
                                ast,
                                declarator.Arg0,
                                bindingScopeId,
                                CompilerCollectedBindingKind.Var,
                                false
                            );
                        else
                            AddBinding(
                                bindingScopeId,
                                CompilerCollectedBindingKind.Var,
                                ast.GetString(declarator.Arg0),
                                declarator.Arg1,
                                false,
                                ast.GetPosition(declarators[i])
                            );
                    }
                }
            }
            else
                VisitExpression(ast, left, parentScopeId);

            VisitExpression(ast, parts[1], scopeId);
            VisitStatement(ast, parts[2], scopeId);
        }

        private void VisitVariableDeclaration(FlatAst ast, AstNode declaration, int scopeId)
        {
            var declarationKind = (JsVariableDeclarationKind)declaration.Arg2;
            var bindingKind =
                declarationKind == JsVariableDeclarationKind.Var
                    ? CompilerCollectedBindingKind.Var
                    : CompilerCollectedBindingKind.Lexical;
            var isConst = declarationKind == JsVariableDeclarationKind.Const;
            var bindingScopeId =
                declarationKind == JsVariableDeclarationKind.Var
                    ? FindVariableScope(scopeId)
                    : scopeId;
            var declarators = ast.ChildRange(declaration.Arg0, declaration.Arg1);
            for (var i = 0; i < declarators.Length; i++)
                VisitVariableDeclarator(
                    ast,
                    declarators[i],
                    bindingScopeId,
                    bindingKind,
                    isConst,
                    scopeId
                );
        }

        private int FindVariableScope(int scopeId)
        {
            var allScopes = scopes.AsSpan();
            while (
                allScopes[scopeId].Kind
                    is not (
                        CompilerCollectedScopeKind.Program
                        or CompilerCollectedScopeKind.Function
                    )
            )
                scopeId = allScopes[scopeId].ParentScopeId;
            return scopeId;
        }

        private void VisitVariableDeclarator(
            FlatAst ast,
            int declaratorIndex,
            int scopeId,
            CompilerCollectedBindingKind bindingKind,
            bool isConst,
            int initializerScopeId = -1
        )
        {
            if (initializerScopeId < 0)
                initializerScopeId = scopeId;
            ref readonly var declarator = ref ast[declaratorIndex];
            if (declarator.Kind == AstKind.VariableDeclaratorPattern)
            {
                VisitBindingPattern(
                    ast,
                    declarator.Arg0,
                    scopeId,
                    bindingKind,
                    isConst,
                    initializerScopeId
                );
                VisitExpression(ast, declarator.Arg1, initializerScopeId);
                return;
            }

            AddBinding(
                scopeId,
                bindingKind,
                ast.GetString(declarator.Arg0),
                declarator.Arg1,
                isConst,
                ast.GetPosition(declaratorIndex)
            );
            if (declarator.Arg2 >= 0)
                VisitExpression(ast, declarator.Arg2, initializerScopeId);
        }

        private void VisitBindingPattern(
            FlatAst ast,
            int nodeIndex,
            int scopeId,
            CompilerCollectedBindingKind bindingKind,
            bool isConst,
            int initializerScopeId = -1
        )
        {
            if (initializerScopeId < 0)
                initializerScopeId = scopeId;
            ref readonly var node = ref ast[nodeIndex];
            switch (node.Kind)
            {
                case AstKind.Identifier:
                    AddBinding(
                        scopeId,
                        bindingKind,
                        ast.GetString(node.Arg0),
                        node.Arg1,
                        isConst,
                        ast.GetPosition(nodeIndex)
                    );
                    return;
                case AstKind.ArrayBindingPattern:
                case AstKind.ArrayExpression:
                {
                    var elements = ast.ChildRange(node.Arg0, node.Arg1);
                    for (var i = 0; i < elements.Length; i++)
                        if (elements[i] >= 0)
                            VisitBindingPattern(
                                ast,
                                elements[i],
                                scopeId,
                                bindingKind,
                                isConst,
                                initializerScopeId
                            );
                    return;
                }
                case AstKind.ObjectBindingPattern:
                case AstKind.ObjectExpression:
                {
                    var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                    for (var i = 0; i < properties.Length; i++)
                    {
                        ref readonly var property = ref properties[i];
                        if (property.IsComputed)
                            VisitExpression(ast, property.Key, initializerScopeId);
                        VisitBindingPattern(
                            ast,
                            property.ValueNode,
                            scopeId,
                            bindingKind,
                            isConst,
                            initializerScopeId
                        );
                    }
                    return;
                }
                case AstKind.SpreadElement:
                    VisitBindingPattern(
                        ast,
                        node.Arg0,
                        scopeId,
                        bindingKind,
                        isConst,
                        initializerScopeId
                    );
                    return;
                case AstKind.AssignmentExpression
                    when (JsAssignmentOperator)node.Arg2 == JsAssignmentOperator.Assign:
                    VisitBindingPattern(
                        ast,
                        node.Arg0,
                        scopeId,
                        bindingKind,
                        isConst,
                        initializerScopeId
                    );
                    VisitExpression(ast, node.Arg1, initializerScopeId);
                    return;
                default:
                    throw new NotSupportedException(
                        $"Flat binding collection does not support pattern '{node.Kind}'."
                    );
            }
        }

        private void VisitBlock(FlatAst ast, int nodeIndex, int parentScopeId)
        {
            ref readonly var block = ref ast[nodeIndex];
            var scopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            var statements = ast.ChildRange(block.Arg0, block.Arg1);
            for (var i = 0; i < statements.Length; i++)
                VisitStatement(ast, statements[i], scopeId);
        }

        private void VisitCatchClause(FlatAst ast, int nodeIndex, int parentScopeId)
        {
            ref readonly var clause = ref ast[nodeIndex];
            var scopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Catch,
                ast.GetPosition(nodeIndex)
            );
            if (clause.Arg0 >= 0)
                VisitBindingPattern(
                    ast,
                    clause.Arg0,
                    scopeId,
                    CompilerCollectedBindingKind.CatchAlias,
                    isConst: false
                );
            VisitBlock(ast, clause.Arg1, scopeId);
        }

        private void VisitFunctionDeclaration(FlatAst ast, AstNode node, int parentScopeId)
        {
            var function = ast.GetFunction(node.Arg0);
            var name = ast.GetString(function.NameStringIndex);
            AddBinding(
                parentScopeId,
                CompilerCollectedBindingKind.FunctionDeclaration,
                name,
                function.NameId,
                position: function.Position
            );
            var functionScopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Function,
                function.Position
            );
            CollectFlatParameters(ast, function, functionScopeId);
            CollectBody(ast, node.Arg1, functionScopeId);
        }

        private void VisitExpression(FlatAst ast, int nodeIndex, int scopeId)
        {
            ref readonly var node = ref ast[nodeIndex];
            switch (node.Kind)
            {
                case AstKind.Identifier:
                    references.Add(
                        new CompilerCollectedReference(
                            scopeId,
                            ast.GetString(node.Arg0),
                            ast.GetPosition(nodeIndex),
                            parameterBodyScopeId
                        )
                    );
                    return;
                case AstKind.AssignmentExpression:
                case AstKind.BinaryExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    VisitExpression(ast, node.Arg1, scopeId);
                    return;
                case AstKind.UnaryExpression:
                case AstKind.UpdateExpression:
                case AstKind.SpreadElement:
                case AstKind.AwaitExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    return;
                case AstKind.YieldExpression:
                    if (node.Arg0 >= 0)
                        VisitExpression(ast, node.Arg0, scopeId);
                    return;
                case AstKind.ConditionalExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    VisitExpression(ast, node.Arg1, scopeId);
                    VisitExpression(ast, node.Arg2, scopeId);
                    return;
                case AstKind.SequenceExpression:
                    var expressions = ast.ChildRange(node.Arg0, node.Arg1);
                    for (var i = 0; i < expressions.Length; i++)
                        VisitExpression(ast, expressions[i], scopeId);
                    return;
                case AstKind.CallExpression:
                case AstKind.OptionalCallExpression:
                case AstKind.NewExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    var arguments = ast.ChildRange(node.Arg1, node.Arg2);
                    for (var i = 0; i < arguments.Length; i++)
                        VisitExpression(ast, arguments[i], scopeId);
                    return;
                case AstKind.OptionalChainExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    return;
                case AstKind.MemberExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    if (((AstMemberFlags)node.Arg2 & AstMemberFlags.Computed) != 0)
                        VisitExpression(ast, node.Arg1, scopeId);
                    return;
                case AstKind.ArrayExpression:
                    var elements = ast.ChildRange(node.Arg0, node.Arg1);
                    for (var i = 0; i < elements.Length; i++)
                        if (elements[i] >= 0)
                            VisitExpression(ast, elements[i], scopeId);
                    return;
                case AstKind.ObjectExpression:
                    var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                    for (var i = 0; i < properties.Length; i++)
                    {
                        ref readonly var property = ref properties[i];
                        if (property.IsComputed)
                            VisitExpression(ast, property.Key, scopeId);
                        VisitExpression(ast, property.ValueNode, scopeId);
                    }
                    return;
                case AstKind.TemplateExpression:
                    var templateParts = ast.ChildRange(node.Arg0, node.Arg1);
                    for (var i = 1; i < templateParts.Length; i += 2)
                        VisitExpression(ast, templateParts[i], scopeId);
                    return;
                case AstKind.TaggedTemplateExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    var taggedParts = ast.ChildRange(node.Arg1, node.Arg2);
                    for (var i = 2; i < taggedParts.Length; i += 3)
                        VisitExpression(ast, taggedParts[i], scopeId);
                    return;
                case AstKind.FunctionExpression:
                case AstKind.ArrowFunctionExpression:
                    VisitFunctionExpression(ast, node, scopeId);
                    return;
                case AstKind.ClassExpression:
                    VisitClass(ast, node, scopeId, isDeclaration: false);
                    return;
                case AstKind.NumericLiteral:
                case AstKind.BigIntLiteral:
                case AstKind.StringLiteral:
                case AstKind.BooleanLiteral:
                case AstKind.NullLiteral:
                case AstKind.RegExpLiteral:
                case AstKind.ThisExpression:
                case AstKind.NewTargetExpression:
                case AstKind.SuperExpression:
                    return;
                default:
                    throw new NotSupportedException(
                        $"Flat binding collection does not support expression '{node.Kind}'."
                    );
            }
        }

        private void VisitFunctionExpression(FlatAst ast, AstNode node, int parentScopeId)
        {
            var function = ast.GetFunction(node.Arg0);
            var functionScopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Function,
                function.Position,
                function.IsArrow
            );
            var name = ast.GetString(function.NameStringIndex);
            if (name.Length != 0)
                AddBinding(
                    functionScopeId,
                    CompilerCollectedBindingKind.FunctionNameSelf,
                    name,
                    function.NameId,
                    position: function.Position
                );
            if (function.HasSuperPropertyReference)
                AddBinding(
                    functionScopeId,
                    CompilerCollectedBindingKind.SuperBase,
                    SuperBaseBindingName,
                    -1,
                    isConst: true,
                    position: function.Position
                );
            CollectFlatParameters(ast, function, functionScopeId);
            CollectBody(ast, node.Arg1, functionScopeId);
        }

        private void VisitClass(FlatAst ast, AstNode node, int parentScopeId, bool isDeclaration)
        {
            var info = ast.GetClass(node.Arg0);
            var name = ast.GetString(info.NameStringIndex);
            if (isDeclaration)
                AddBinding(
                    parentScopeId,
                    CompilerCollectedBindingKind.ClassDeclaration,
                    name,
                    info.NameId,
                    isConst: true,
                    position: info.Position
                );
            var classScopeId = AddScope(
                parentScopeId,
                CompilerCollectedScopeKind.Class,
                info.Position
            );
            if (name.Length != 0)
                AddBinding(
                    classScopeId,
                    CompilerCollectedBindingKind.ClassLexicalAlias,
                    name,
                    info.NameId,
                    isConst: true,
                    position: info.Position
                );
            if (info.ExtendsNode >= 0)
                VisitExpression(ast, info.ExtendsNode, classScopeId);

            var visitedConstructor = false;
            var elements = ast.GetClassElements(info);
            for (var i = 0; i < elements.Length; i++)
            {
                ref readonly var element = ref elements[i];
                if (element.IsComputed)
                    VisitExpression(ast, element.Key, classScopeId);
                VisitFunctionExpression(ast, ast[element.ValueNode], classScopeId);
                visitedConstructor |= element.ValueNode == info.ConstructorNode;
            }
            if (!visitedConstructor)
                VisitFunctionExpression(ast, ast[info.ConstructorNode], classScopeId);
        }

        private void CollectParameters(FunctionParameterPlan parameterPlan, int scopeId)
        {
            for (var i = 0; i < parameterPlan.Bindings.Count; i++)
            {
                var binding = parameterPlan.Bindings[i];
                if (binding.IsPattern)
                {
                    for (var j = 0; j < binding.BoundIdentifiers.Count; j++)
                    {
                        var bound = binding.BoundIdentifiers[j];
                        AddBinding(
                            scopeId,
                            CompilerCollectedBindingKind.Parameter,
                            bound.Name,
                            bound.NameId,
                            position: binding.Position
                        );
                    }
                }
                else
                {
                    AddBinding(
                        scopeId,
                        CompilerCollectedBindingKind.Parameter,
                        binding.Name,
                        binding.NameId,
                        position: binding.Position
                    );
                }
            }
        }

        private void CollectFlatParameters(FlatAst ast, in FlatFunctionInfo function, int scopeId)
        {
            var parameters = ast.GetParameters(function);
            var previousParameterBodyScopeId = parameterBodyScopeId;
            parameterBodyScopeId = scopeId;
            try
            {
                for (var i = 0; i < parameters.Length; i++)
                {
                    ref readonly var parameter = ref parameters[i];
                    if (parameter.PatternNode >= 0)
                        VisitBindingPattern(
                            ast,
                            parameter.PatternNode,
                            scopeId,
                            CompilerCollectedBindingKind.Parameter,
                            isConst: false
                        );
                    else
                        AddBinding(
                            scopeId,
                            CompilerCollectedBindingKind.Parameter,
                            ast.GetString(parameter.NameStringIndex),
                            parameter.NameId,
                            position: parameter.Position
                        );
                    if (parameter.InitializerNode >= 0)
                        VisitExpression(ast, parameter.InitializerNode, scopeId);
                }
            }
            finally
            {
                parameterBodyScopeId = previousParameterBodyScopeId;
            }
        }

        private void CollectParameterInitializers(FunctionParameterPlan parameterPlan, int scopeId)
        {
            for (var i = 0; i < parameterPlan.Initializers.Count; i++)
                if (parameterPlan.Initializers[i] is not null)
                    VisitClassExpression(parameterPlan.Initializers[i]!, scopeId);
        }

        private void VisitClassExpression(JsExpression expression, int scopeId)
        {
            switch (expression)
            {
                case JsIdentifierExpression identifier:
                    references.Add(
                        new CompilerCollectedReference(
                            scopeId,
                            identifier.Name,
                            identifier.Position
                        )
                    );
                    return;
                case JsAssignmentExpression assignment:
                    VisitClassExpression(assignment.Left, scopeId);
                    VisitClassExpression(assignment.Right, scopeId);
                    return;
                case JsBinaryExpression binary:
                    VisitClassExpression(binary.Left, scopeId);
                    VisitClassExpression(binary.Right, scopeId);
                    return;
                case JsUnaryExpression unary:
                    VisitClassExpression(unary.Argument, scopeId);
                    return;
                case JsUpdateExpression update:
                    VisitClassExpression(update.Argument, scopeId);
                    return;
                case JsConditionalExpression conditional:
                    VisitClassExpression(conditional.Test, scopeId);
                    VisitClassExpression(conditional.Consequent, scopeId);
                    VisitClassExpression(conditional.Alternate, scopeId);
                    return;
                case JsSequenceExpression sequence:
                    for (var i = 0; i < sequence.Expressions.Count; i++)
                        VisitClassExpression(sequence.Expressions[i], scopeId);
                    return;
                default:
                    return;
            }
        }

        private int AddScope(
            int parentScopeId,
            CompilerCollectedScopeKind kind,
            int position,
            bool isArrow = false
        )
        {
            var scopeId = nextScopeId++;
            scopes.Add(new CompilerCollectedScope(scopeId, parentScopeId, kind, position, isArrow));
            return scopeId;
        }

        private void AddBinding(
            int scopeId,
            CompilerCollectedBindingKind kind,
            string name,
            int nameId = -1,
            bool isConst = false,
            int position = 0
        )
        {
            var key = (scopeId, name);
            if (
                IsVariableEnvironmentBinding(kind)
                && mergeableBindings.TryGetValue(key, out var existing)
                && IsVariableEnvironmentBinding(existing.Kind)
            )
            {
                if (
                    existing.Kind == CompilerCollectedBindingKind.Var
                    && kind == CompilerCollectedBindingKind.FunctionDeclaration
                )
                {
                    bindings[existing.Index] = new CompilerCollectedBinding(
                        scopeId,
                        kind,
                        name,
                        nameId,
                        isConst,
                        position
                    );
                    mergeableBindings[key] = (kind, existing.Index);
                }
                return;
            }
            if (IsVariableEnvironmentBinding(kind))
                mergeableBindings.TryAdd(key, (kind, bindings.Count));
            bindings.Add(
                new CompilerCollectedBinding(scopeId, kind, name, nameId, isConst, position)
            );
        }

        private static bool IsVariableEnvironmentBinding(CompilerCollectedBindingKind kind) =>
            kind
                is CompilerCollectedBindingKind.Parameter
                    or CompilerCollectedBindingKind.Var
                    or CompilerCollectedBindingKind.FunctionDeclaration;
    }
}
