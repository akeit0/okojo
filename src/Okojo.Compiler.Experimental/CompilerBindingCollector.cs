using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal static class CompilerBindingCollector
{
    public static CompilerBindingCollectionResult Collect(JsProgram program)
    {
        var collector = new Collector();
        collector.CollectProgram(program);
        return collector.MoveResult();
    }

    public static CompilerBindingCollectionResult CollectFunction(
        string? name,
        int nameId,
        FunctionParameterPlan parameterPlan,
        JsBlockStatement body,
        bool hasSelfBinding = false)
    {
        var collector = new Collector(CompilerCollectedScopeKind.Function);
        collector.CollectFunctionRoot(name, nameId, parameterPlan, body, hasSelfBinding);
        return collector.MoveResult();
    }

    private sealed class Collector
    {
        private readonly PooledArrayBuilder<CompilerCollectedScope> scopes = new(16);
        private readonly PooledArrayBuilder<CompilerCollectedBinding> bindings = new(32);
        private readonly PooledArrayBuilder<CompilerCollectedReference> references = new(64);
        private int nextScopeId;

        public Collector(CompilerCollectedScopeKind rootKind = CompilerCollectedScopeKind.Program)
        {
            scopes.Add(new CompilerCollectedScope(0, -1, rootKind));
            nextScopeId = 1;
        }

        public void CollectProgram(JsProgram program)
        {
            for (var i = 0; i < program.Statements.Count; i++)
                VisitStatement(program.Statements[i], 0);
        }

        public void CollectFunctionRoot(
            string? name,
            int nameId,
            FunctionParameterPlan parameterPlan,
            JsBlockStatement body,
            bool hasSelfBinding)
        {
            if (hasSelfBinding && !string.IsNullOrEmpty(name))
                AddBinding(0, CompilerCollectedBindingKind.FunctionNameSelf, name!, nameId, position: body.Position);

            for (var i = 0; i < parameterPlan.Bindings.Count; i++)
            {
                var binding = parameterPlan.Bindings[i];
                AddBinding(0, CompilerCollectedBindingKind.Parameter, binding.Name, binding.NameId, position: binding.Position);
                for (var j = 0; j < binding.BoundIdentifiers.Count; j++)
                {
                    var bound = binding.BoundIdentifiers[j];
                    AddBinding(0, CompilerCollectedBindingKind.Parameter, bound.Name, bound.NameId, position: binding.Position);
                }
            }

            for (var i = 0; i < parameterPlan.Initializers.Count; i++)
                if (parameterPlan.Initializers[i] is not null)
                    VisitExpression(parameterPlan.Initializers[i]!, 0);
            for (var i = 0; i < body.Statements.Count; i++)
                VisitStatement(body.Statements[i], 0);
        }

        public CompilerBindingCollectionResult MoveResult()
        {
            return new(scopes, bindings, references);
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
            int position = 0)
        {
            bindings.Add(new CompilerCollectedBinding(scopeId, kind, name, nameId, isConst, position));
        }

        private void AddReference(int scopeId, string name, int position = 0)
        {
            references.Add(new CompilerCollectedReference(scopeId, name, position));
        }

        private void VisitStatement(JsStatement statement, int scopeId)
        {
            switch (statement)
            {
                case JsExportDeclarationStatement exportDeclaration:
                    VisitStatement(exportDeclaration.Declaration, scopeId);
                    return;
                case JsImportDeclaration importDeclaration:
                    VisitImportDeclaration(importDeclaration, scopeId);
                    return;
                case JsVariableDeclarationStatement declaration:
                    VisitVariableDeclarationStatement(declaration, scopeId);
                    return;
                case JsBlockStatement block:
                    VisitBlockStatement(block, scopeId);
                    return;
                case JsFunctionDeclaration function:
                    AddBinding(scopeId, CompilerCollectedBindingKind.FunctionDeclaration, function.Name, function.NameId,
                        position: function.Position);
                    VisitFunction(function.Name, function.NameId, FunctionParameterPlan.FromFunction(function),
                        function.Body, scopeId, hasSelfBinding: false);
                    return;
                case JsClassDeclaration classDeclaration:
                    AddBinding(scopeId, CompilerCollectedBindingKind.ClassDeclaration, classDeclaration.Name,
                        classDeclaration.NameId, position: classDeclaration.Position);
                    VisitClassExpression(classDeclaration.ClassExpression, scopeId, classDeclaration.Name,
                        classDeclaration.NameId, isDeclaration: true);
                    return;
                case JsIfStatement conditional:
                    VisitExpression(conditional.Test, scopeId);
                    VisitStatement(conditional.Consequent, scopeId);
                    if (conditional.Alternate is not null)
                        VisitStatement(conditional.Alternate, scopeId);
                    return;
                case JsWhileStatement whileStatement:
                    VisitExpression(whileStatement.Test, scopeId);
                    VisitStatement(whileStatement.Body, scopeId);
                    return;
                case JsDoWhileStatement doWhileStatement:
                    VisitStatement(doWhileStatement.Body, scopeId);
                    VisitExpression(doWhileStatement.Test, scopeId);
                    return;
                case JsForStatement forStatement:
                    VisitForStatement(forStatement, scopeId);
                    return;
                case JsForInOfStatement forInOfStatement:
                    VisitForInOfStatement(forInOfStatement, scopeId);
                    return;
                case JsReturnStatement returnStatement:
                    if (returnStatement.Argument is not null)
                        VisitExpression(returnStatement.Argument, scopeId);
                    return;
                case JsThrowStatement throwStatement:
                    VisitExpression(throwStatement.Argument, scopeId);
                    return;
                case JsExpressionStatement expressionStatement:
                    VisitExpression(expressionStatement.Expression, scopeId);
                    return;
                case JsTryStatement tryStatement:
                    VisitBlockStatement(tryStatement.Block, scopeId);
                    if (tryStatement.Handler is not null)
                        VisitCatchClause(tryStatement.Handler, scopeId);
                    if (tryStatement.Finalizer is not null)
                        VisitBlockStatement(tryStatement.Finalizer, scopeId);
                    return;
                case JsSwitchStatement switchStatement:
                    VisitExpression(switchStatement.Discriminant, scopeId);
                    for (var i = 0; i < switchStatement.Cases.Count; i++)
                    {
                        var switchCase = switchStatement.Cases[i];
                        if (switchCase.Test is not null)
                            VisitExpression(switchCase.Test, scopeId);
                        for (var j = 0; j < switchCase.Consequent.Count; j++)
                            VisitStatement(switchCase.Consequent[j], scopeId);
                    }
                    return;
                case JsLabeledStatement labeled:
                    VisitStatement(labeled.Statement, scopeId);
                    return;
                case JsEmptyObjectBindingDeclarationStatement emptyBinding:
                    VisitExpression(emptyBinding.Initializer, scopeId);
                    return;
                default:
                    return;
            }
        }

        private void VisitImportDeclaration(JsImportDeclaration declaration, int scopeId)
        {
            if (!string.IsNullOrEmpty(declaration.DefaultBinding))
                AddBinding(scopeId, CompilerCollectedBindingKind.Import, declaration.DefaultBinding!,
                    position: declaration.Position);
            if (!string.IsNullOrEmpty(declaration.NamespaceBinding))
                AddBinding(scopeId, CompilerCollectedBindingKind.Import, declaration.NamespaceBinding!,
                    position: declaration.Position);
            for (var i = 0; i < declaration.NamedBindings.Count; i++)
                AddBinding(scopeId, CompilerCollectedBindingKind.Import, declaration.NamedBindings[i].LocalName,
                    position: declaration.NamedBindings[i].Position);
        }

        private void VisitVariableDeclarationStatement(JsVariableDeclarationStatement declaration, int scopeId)
        {
            if (declaration.BindingPattern is not null)
            {
                CollectPatternBindings(scopeId, declaration.BindingPattern, declaration.Kind);
                if (declaration.BindingInitializer is not null)
                    VisitExpression(declaration.BindingInitializer, scopeId);
                return;
            }

            var bindingKind = declaration.Kind == JsVariableDeclarationKind.Var
                ? CompilerCollectedBindingKind.Var
                : CompilerCollectedBindingKind.Lexical;
            var isConst = declaration.Kind == JsVariableDeclarationKind.Const;
            for (var i = 0; i < declaration.Declarators.Count; i++)
            {
                var declarator = declaration.Declarators[i];
                AddBinding(scopeId, bindingKind, declarator.Name, declarator.NameId, isConst, declarator.Position);
                if (declarator.Initializer is not null)
                    VisitExpression(declarator.Initializer, scopeId);
            }
        }

        private void VisitBlockStatement(JsBlockStatement block, int parentScopeId)
        {
            var scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Block, block.Position);
            for (var i = 0; i < block.Statements.Count; i++)
                VisitStatement(block.Statements[i], scopeId);
        }

        private void VisitCatchClause(JsCatchClause catchClause, int parentScopeId)
        {
            var scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Catch, catchClause.Position);
            if (!string.IsNullOrEmpty(catchClause.ParamName))
                AddBinding(scopeId, CompilerCollectedBindingKind.CatchAlias, catchClause.ParamName,
                    position: catchClause.Position);
            if (catchClause.BindingPattern is not null)
                CollectPatternBindings(scopeId, catchClause.BindingPattern, JsVariableDeclarationKind.Let,
                    CompilerCollectedBindingKind.CatchAlias);
            for (var i = 0; i < catchClause.Declarators.Count; i++)
            {
                var declarator = catchClause.Declarators[i];
                AddBinding(scopeId, CompilerCollectedBindingKind.CatchAlias, declarator.Name, declarator.NameId,
                    position: declarator.Position);
            }

            VisitBlockStatement(catchClause.Body, scopeId);
        }

        private void VisitForStatement(JsForStatement statement, int parentScopeId)
        {
            var scopeId = parentScopeId;
            if (statement.Init is JsVariableDeclarationStatement initDeclaration &&
                initDeclaration.Kind is JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const)
            {
                scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Block, statement.Position);
                CollectLoopHeadBindings(scopeId, initDeclaration);
                for (var i = 0; i < initDeclaration.Declarators.Count; i++)
                    if (initDeclaration.Declarators[i].Initializer is not null)
                        VisitExpression(initDeclaration.Declarators[i].Initializer!, scopeId);
            }
            else if (statement.Init is JsExpression initExpression)
            {
                VisitExpression(initExpression, parentScopeId);
            }

            if (statement.Test is not null)
                VisitExpression(statement.Test, scopeId);
            if (statement.Update is not null)
                VisitExpression(statement.Update, scopeId);
            VisitStatement(statement.Body, scopeId);
        }

        private void VisitForInOfStatement(JsForInOfStatement statement, int parentScopeId)
        {
            var scopeId = parentScopeId;
            if (statement.Left is JsVariableDeclarationStatement declaration &&
                declaration.Kind is JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const)
            {
                scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Block, statement.Position);
                CollectLoopHeadBindings(scopeId, declaration);
                for (var i = 0; i < declaration.Declarators.Count; i++)
                    if (declaration.Declarators[i].Initializer is not null)
                        VisitExpression(declaration.Declarators[i].Initializer!, scopeId);
            }
            else if (statement.Left is JsExpression leftExpression)
            {
                VisitExpression(leftExpression, parentScopeId);
            }

            VisitExpression(statement.Right, scopeId);
            VisitStatement(statement.Body, scopeId);
        }

        private void CollectLoopHeadBindings(int scopeId, JsVariableDeclarationStatement declaration)
        {
            if (declaration.BindingPattern is not null)
            {
                CollectPatternBindings(scopeId, declaration.BindingPattern, declaration.Kind,
                    CompilerCollectedBindingKind.LoopHeadAlias);
                return;
            }

            var isConst = declaration.Kind == JsVariableDeclarationKind.Const;
            for (var i = 0; i < declaration.Declarators.Count; i++)
            {
                var declarator = declaration.Declarators[i];
                AddBinding(scopeId, CompilerCollectedBindingKind.LoopHeadAlias, declarator.Name, declarator.NameId,
                    isConst, declarator.Position);
            }
        }

        private void VisitFunction(
            string? name,
            int nameId,
            FunctionParameterPlan parameterPlan,
            JsBlockStatement body,
            int parentScopeId,
            bool hasSelfBinding)
        {
            var scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Function, body.Position);
            if (hasSelfBinding && !string.IsNullOrEmpty(name))
                AddBinding(scopeId, CompilerCollectedBindingKind.FunctionNameSelf, name!, nameId, position: body.Position);

            for (var i = 0; i < parameterPlan.Bindings.Count; i++)
            {
                var binding = parameterPlan.Bindings[i];
                AddBinding(scopeId, CompilerCollectedBindingKind.Parameter, binding.Name, binding.NameId,
                    position: binding.Position);
                for (var j = 0; j < binding.BoundIdentifiers.Count; j++)
                {
                    var bound = binding.BoundIdentifiers[j];
                    AddBinding(scopeId, CompilerCollectedBindingKind.Parameter, bound.Name, bound.NameId,
                        position: binding.Position);
                }
            }

            for (var i = 0; i < parameterPlan.Initializers.Count; i++)
                if (parameterPlan.Initializers[i] is not null)
                    VisitExpression(parameterPlan.Initializers[i]!, scopeId);
            for (var i = 0; i < body.Statements.Count; i++)
                VisitStatement(body.Statements[i], scopeId);
        }

        private void VisitClassExpression(
            JsClassExpression classExpression,
            int parentScopeId,
            string? declarationName = null,
            int declarationNameId = -1,
            bool isDeclaration = false)
        {
            var scopeId = AddScope(parentScopeId, CompilerCollectedScopeKind.Class, classExpression.Position);
            if (!string.IsNullOrEmpty(classExpression.Name))
                AddBinding(scopeId, CompilerCollectedBindingKind.ClassLexicalAlias, classExpression.Name!,
                    classExpression.NameId, isConst: true, position: classExpression.Position);
            else if (isDeclaration && !string.IsNullOrEmpty(declarationName))
                AddBinding(scopeId, CompilerCollectedBindingKind.ClassLexicalAlias, declarationName!,
                    declarationNameId, isConst: true, position: classExpression.Position);

            if (classExpression.ExtendsExpression is not null)
                VisitExpression(classExpression.ExtendsExpression, scopeId);

            for (var i = 0; i < classExpression.Elements.Count; i++)
            {
                var element = classExpression.Elements[i];
                if (element.ComputedKey is not null)
                    VisitExpression(element.ComputedKey, scopeId);
                if (element.FieldInitializer is not null)
                    VisitExpression(element.FieldInitializer, scopeId);
                if (element.StaticBlock is not null)
                {
                    var staticBlockScopeId =
                        AddScope(scopeId, CompilerCollectedScopeKind.StaticBlock, element.StaticBlock.Position);
                    for (var j = 0; j < element.StaticBlock.Statements.Count; j++)
                        VisitStatement(element.StaticBlock.Statements[j], staticBlockScopeId);
                }

                if (element.Value is not null)
                    VisitFunction(element.Value.Name, element.Value.NameId, FunctionParameterPlan.FromFunction(element.Value),
                        element.Value.Body, scopeId,
                        hasSelfBinding: !string.IsNullOrEmpty(element.Value.Name));
            }
        }

        private void VisitExpression(JsExpression expression, int scopeId)
        {
            switch (expression)
            {
                case JsIdentifierExpression identifier:
                    AddReference(scopeId, identifier.Name, identifier.Position);
                    return;
                case JsFunctionExpression function:
                    VisitFunction(function.Name, function.NameId, FunctionParameterPlan.FromFunction(function),
                        function.Body, scopeId,
                        hasSelfBinding: !string.IsNullOrEmpty(function.Name));
                    return;
                case JsClassExpression classExpression:
                    VisitClassExpression(classExpression, scopeId);
                    return;
                case JsAssignmentExpression assignment:
                    VisitExpression(assignment.Left, scopeId);
                    VisitExpression(assignment.Right, scopeId);
                    return;
                case JsBinaryExpression binary:
                    VisitExpression(binary.Left, scopeId);
                    VisitExpression(binary.Right, scopeId);
                    return;
                case JsConditionalExpression conditional:
                    VisitExpression(conditional.Test, scopeId);
                    VisitExpression(conditional.Consequent, scopeId);
                    VisitExpression(conditional.Alternate, scopeId);
                    return;
                case JsCallExpression call:
                    VisitExpression(call.Callee, scopeId);
                    for (var i = 0; i < call.Arguments.Count; i++)
                        VisitExpression(call.Arguments[i], scopeId);
                    return;
                case JsNewExpression @new:
                    VisitExpression(@new.Callee, scopeId);
                    for (var i = 0; i < @new.Arguments.Count; i++)
                        VisitExpression(@new.Arguments[i], scopeId);
                    return;
                case JsMemberExpression member:
                    VisitExpression(member.Object, scopeId);
                    if (member.IsComputed)
                        VisitExpression(member.Property, scopeId);
                    return;
                case JsObjectExpression obj:
                    for (var i = 0; i < obj.Properties.Count; i++)
                    {
                        var property = obj.Properties[i];
                        if (property.ComputedKey is not null)
                            VisitExpression(property.ComputedKey, scopeId);
                        VisitExpression(property.Value, scopeId);
                    }
                    return;
                case JsArrayExpression array:
                    for (var i = 0; i < array.Elements.Count; i++)
                        if (array.Elements[i] is not null)
                            VisitExpression(array.Elements[i]!, scopeId);
                    return;
                case JsSpreadExpression spread:
                    VisitExpression(spread.Argument, scopeId);
                    return;
                case JsUnaryExpression unary:
                    VisitExpression(unary.Argument, scopeId);
                    return;
                case JsUpdateExpression update:
                    VisitExpression(update.Argument, scopeId);
                    return;
                case JsYieldExpression yield:
                    if (yield.Argument is not null)
                        VisitExpression(yield.Argument, scopeId);
                    return;
                case JsAwaitExpression awaitExpression:
                    VisitExpression(awaitExpression.Argument, scopeId);
                    return;
                case JsTaggedTemplateExpression taggedTemplate:
                    VisitExpression(taggedTemplate.Tag, scopeId);
                    for (var i = 0; i < taggedTemplate.Template.Expressions.Count; i++)
                        VisitExpression(taggedTemplate.Template.Expressions[i], scopeId);
                    return;
                case JsTemplateExpression template:
                    for (var i = 0; i < template.Expressions.Count; i++)
                        VisitExpression(template.Expressions[i], scopeId);
                    return;
                case JsSequenceExpression sequence:
                    for (var i = 0; i < sequence.Expressions.Count; i++)
                        VisitExpression(sequence.Expressions[i], scopeId);
                    return;
                case JsImportCallExpression importCall:
                    VisitExpression(importCall.Argument, scopeId);
                    if (importCall.Options is not null)
                        VisitExpression(importCall.Options, scopeId);
                    return;
                default:
                    return;
            }
        }

        private void CollectPatternBindings(
            int scopeId,
            JsExpression pattern,
            JsVariableDeclarationKind declarationKind,
            CompilerCollectedBindingKind? explicitKind = null)
        {
            switch (pattern)
            {
                case JsIdentifierExpression id:
                    AddBinding(scopeId,
                        explicitKind ?? (declarationKind == JsVariableDeclarationKind.Var
                            ? CompilerCollectedBindingKind.Var
                            : CompilerCollectedBindingKind.Lexical),
                        id.Name,
                        id.NameId,
                        declarationKind == JsVariableDeclarationKind.Const,
                        id.Position);
                    return;
                case JsSpreadExpression spread:
                    CollectPatternBindings(scopeId, spread.Argument, declarationKind, explicitKind);
                    return;
                case JsArrayExpression arrayPattern:
                    for (var i = 0; i < arrayPattern.Elements.Count; i++)
                        if (arrayPattern.Elements[i] is not null)
                            CollectPatternBindings(scopeId, arrayPattern.Elements[i]!, declarationKind, explicitKind);
                    return;
                case JsObjectExpression objectPattern:
                    for (var i = 0; i < objectPattern.Properties.Count; i++)
                        CollectPatternBindings(scopeId, objectPattern.Properties[i].Value, declarationKind, explicitKind);
                    return;
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: var left, Right: var right }:
                    CollectPatternBindings(scopeId, left, declarationKind, explicitKind);
                    VisitExpression(right, scopeId);
                    return;
                default:
                    return;
            }
        }
    }
}
