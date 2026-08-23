using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static partial class CompilerBindingCollector
{
    public static CompilerBindingCollectionResult Collect(FlatAst ast)
    {
        var collector = new FlatCollector();
        collector.CollectBody(ast, ast.Root, 0);
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
        return collector.MoveResult();
    }

    public static CompilerBindingCollectionResult CollectFunction(
        FlatAst ast,
        in FlatFunctionInfo function,
        int bodyRoot,
        bool hasSelfBinding = false
    )
    {
        var collector = new FlatCollector(CompilerCollectedScopeKind.Function);
        collector.CollectFlatFunctionRoot(ast, function, bodyRoot, hasSelfBinding);
        return collector.MoveResult();
    }

    private sealed class FlatCollector
    {
        private readonly PooledArrayBuilder<CompilerCollectedScope> scopes = new(16);
        private readonly PooledArrayBuilder<CompilerCollectedBinding> bindings = new(32);
        private readonly PooledArrayBuilder<CompilerCollectedReference> references = new(64);
        private int nextScopeId = 1;
        private int parameterBodyScopeId = -1;

        public FlatCollector(
            CompilerCollectedScopeKind rootKind = CompilerCollectedScopeKind.Program
        )
        {
            scopes.Add(new CompilerCollectedScope(0, -1, rootKind));
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
                case AstKind.EmptyStatement:
                    return;
                default:
                    throw new NotSupportedException(
                        $"Flat binding collection does not support statement '{node.Kind}'."
                    );
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

        private void VisitVariableDeclaration(FlatAst ast, AstNode declaration, int scopeId)
        {
            var declarationKind = (JsVariableDeclarationKind)declaration.Arg2;
            var bindingKind =
                declarationKind == JsVariableDeclarationKind.Var
                    ? CompilerCollectedBindingKind.Var
                    : CompilerCollectedBindingKind.Lexical;
            var isConst = declarationKind == JsVariableDeclarationKind.Const;
            var declarators = ast.ChildRange(declaration.Arg0, declaration.Arg1);
            for (var i = 0; i < declarators.Length; i++)
                VisitVariableDeclarator(ast, declarators[i], scopeId, bindingKind, isConst);
        }

        private void VisitVariableDeclarator(
            FlatAst ast,
            int declaratorIndex,
            int scopeId,
            CompilerCollectedBindingKind bindingKind,
            bool isConst
        )
        {
            ref readonly var declarator = ref ast[declaratorIndex];
            if (declarator.Kind == AstKind.VariableDeclaratorPattern)
            {
                VisitBindingPattern(ast, declarator.Arg0, scopeId, bindingKind, isConst);
                VisitExpression(ast, declarator.Arg1, scopeId);
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
                VisitExpression(ast, declarator.Arg2, scopeId);
        }

        private void VisitBindingPattern(
            FlatAst ast,
            int nodeIndex,
            int scopeId,
            CompilerCollectedBindingKind bindingKind,
            bool isConst
        )
        {
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
                {
                    var elements = ast.ChildRange(node.Arg0, node.Arg1);
                    for (var i = 0; i < elements.Length; i++)
                        if (elements[i] >= 0)
                            VisitBindingPattern(ast, elements[i], scopeId, bindingKind, isConst);
                    return;
                }
                case AstKind.ObjectBindingPattern:
                {
                    var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                    for (var i = 0; i < properties.Length; i++)
                    {
                        ref readonly var property = ref properties[i];
                        if (property.IsComputed)
                            VisitExpression(ast, property.Key, scopeId);
                        VisitBindingPattern(ast, property.ValueNode, scopeId, bindingKind, isConst);
                    }
                    return;
                }
                case AstKind.SpreadElement:
                    VisitBindingPattern(ast, node.Arg0, scopeId, bindingKind, isConst);
                    return;
                case AstKind.AssignmentExpression
                    when (JsAssignmentOperator)node.Arg2 == JsAssignmentOperator.Assign:
                    VisitBindingPattern(ast, node.Arg0, scopeId, bindingKind, isConst);
                    VisitExpression(ast, node.Arg1, scopeId);
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
                case AstKind.NewExpression:
                    VisitExpression(ast, node.Arg0, scopeId);
                    var arguments = ast.ChildRange(node.Arg1, node.Arg2);
                    for (var i = 0; i < arguments.Length; i++)
                        VisitExpression(ast, arguments[i], scopeId);
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
                case AstKind.FunctionExpression:
                    VisitFunctionExpression(ast, node, scopeId);
                    return;
                case AstKind.NumericLiteral:
                case AstKind.StringLiteral:
                case AstKind.BooleanLiteral:
                case AstKind.NullLiteral:
                case AstKind.ThisExpression:
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
                function.Position
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
            CollectFlatParameters(ast, function, functionScopeId);
            CollectBody(ast, node.Arg1, functionScopeId);
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

        private int AddScope(int parentScopeId, CompilerCollectedScopeKind kind, int position)
        {
            var scopeId = nextScopeId++;
            scopes.Add(new CompilerCollectedScope(scopeId, parentScopeId, kind, position));
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
            bindings.Add(
                new CompilerCollectedBinding(scopeId, kind, name, nameId, isConst, position)
            );
        }
    }
}
