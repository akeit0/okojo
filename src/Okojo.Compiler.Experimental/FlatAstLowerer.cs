using System.Buffers;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class FlatAstLowerer
{
    public static FlatAst Lower(JsProgram program)
    {
        var lowerer = new Lowerer(
            program.SourceText ?? string.Empty,
            nameof(JsPlannedScriptCompiler)
        );
        try
        {
            lowerer.Ast.Root = lowerer.LowerProgram(program);
            return lowerer.Ast;
        }
        catch
        {
            lowerer.Ast.Dispose();
            throw;
        }
    }

    public static FlatAst Lower(JsBlockStatement body)
    {
        var lowerer = new Lowerer(string.Empty, nameof(JsPlannedFunctionCompiler));
        try
        {
            lowerer.Ast.Root = lowerer.LowerFunctionBody(body);
            return lowerer.Ast;
        }
        catch
        {
            lowerer.Ast.Dispose();
            throw;
        }
    }

    private sealed class Lowerer(string source, string compilerName)
    {
        public FlatAst Ast { get; } = new(source);
        private AstArena Arena => Ast.Arena;

        public int LowerProgram(JsProgram program)
        {
            var children = LowerStatements(program.Statements);
            return Arena.Add(AstKind.Program, children.Offset, children.Count);
        }

        public int LowerFunctionBody(JsBlockStatement body)
        {
            var children = LowerStatements(body.Statements);
            return Arena.Add(AstKind.Program, children.Offset, children.Count);
        }

        private int LowerStatement(JsStatement statement)
        {
            return statement switch
            {
                JsVariableDeclarationStatement declaration => LowerVariableDeclaration(declaration),
                JsFunctionDeclaration function => LowerFunctionDeclaration(function),
                JsBlockStatement block => LowerBlock(block),
                JsIfStatement ifStatement => Arena.Add(
                    AstKind.IfStatement,
                    LowerExpression(ifStatement.Test),
                    LowerStatement(ifStatement.Consequent),
                    ifStatement.Alternate is null ? -1 : LowerStatement(ifStatement.Alternate),
                    ifStatement.Position
                ),
                JsWhileStatement whileStatement => Arena.Add(
                    AstKind.WhileStatement,
                    LowerExpression(whileStatement.Test),
                    LowerStatement(whileStatement.Body),
                    position: whileStatement.Position
                ),
                JsDoWhileStatement doWhileStatement => Arena.Add(
                    AstKind.DoWhileStatement,
                    LowerStatement(doWhileStatement.Body),
                    LowerExpression(doWhileStatement.Test),
                    position: doWhileStatement.Position
                ),
                JsForStatement forStatement => LowerForStatement(forStatement),
                JsBreakStatement { Label: null } => Arena.Add(
                    AstKind.BreakStatement,
                    position: statement.Position
                ),
                JsContinueStatement { Label: null } => Arena.Add(
                    AstKind.ContinueStatement,
                    position: statement.Position
                ),
                JsBreakStatement or JsContinueStatement => throw new NotSupportedException(
                    $"Labeled loop control is not supported by {compilerName}."
                ),
                JsExpressionStatement expression => Arena.Add(
                    AstKind.ExpressionStatement,
                    LowerExpression(expression.Expression),
                    position: expression.Position
                ),
                JsReturnStatement returnStatement => Arena.Add(
                    AstKind.ReturnStatement,
                    returnStatement.Argument is null
                        ? -1
                        : LowerExpression(returnStatement.Argument),
                    position: returnStatement.Position
                ),
                JsEmptyStatement => Arena.Add(AstKind.EmptyStatement, position: statement.Position),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support statement '{statement.GetType().Name}'."
                ),
            };
        }

        private int LowerFunctionDeclaration(JsFunctionDeclaration function)
        {
            var bodyRoot = LowerFunctionBody(function.Body);
            var functionIndex = Ast.AddFunction(
                new FlatFunctionInfo(
                    function.Name,
                    function.NameId,
                    FunctionParameterPlan.FromFunction(function),
                    function.Body.StrictDeclared,
                    function.Position
                )
            );
            return Arena.Add(
                AstKind.FunctionDeclaration,
                functionIndex,
                bodyRoot,
                position: function.Position
            );
        }

        private int LowerForStatement(JsForStatement statement)
        {
            var init = statement.Init switch
            {
                null => -1,
                JsStatement initStatement => LowerStatement(initStatement),
                JsExpression initExpression => LowerExpression(initExpression),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support for initializer '{statement.Init.GetType().Name}'."
                ),
            };
            Span<int> parts =
            [
                init,
                statement.Test is null ? -1 : LowerExpression(statement.Test),
                statement.Update is null ? -1 : LowerExpression(statement.Update),
                LowerStatement(statement.Body),
            ];
            var children = Arena.AddChildren(parts);
            return Arena.Add(
                AstKind.ForStatement,
                children.Offset,
                children.Count,
                position: statement.Position
            );
        }

        private int LowerBlock(JsBlockStatement block)
        {
            var children = LowerStatements(block.Statements);
            return Arena.Add(
                AstKind.BlockStatement,
                children.Offset,
                children.Count,
                position: block.Position
            );
        }

        private int LowerVariableDeclaration(JsVariableDeclarationStatement declaration)
        {
            if (declaration.BindingPattern is not null)
                throw new NotSupportedException(
                    $"Binding patterns are not supported by {compilerName}."
                );

            var declarators = ArrayPool<int>.Shared.Rent(declaration.Declarators.Count);
            try
            {
                for (var i = 0; i < declaration.Declarators.Count; i++)
                {
                    var declarator = declaration.Declarators[i];
                    declarators[i] = Arena.Add(
                        AstKind.VariableDeclarator,
                        Arena.AddString(declarator.Name),
                        declarator.NameId,
                        declarator.Initializer is null
                            ? -1
                            : LowerExpression(declarator.Initializer),
                        declarator.Position
                    );
                }

                var children = Arena.AddChildren(
                    declarators.AsSpan(0, declaration.Declarators.Count)
                );
                return Arena.Add(
                    AstKind.VariableDeclaration,
                    children.Offset,
                    children.Count,
                    (int)declaration.Kind,
                    declaration.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(declarators);
            }
        }

        private (int Offset, int Count) LowerStatements(IReadOnlyList<JsStatement> statements)
        {
            var children = ArrayPool<int>.Shared.Rent(statements.Count);
            try
            {
                for (var i = 0; i < statements.Count; i++)
                    children[i] = LowerStatement(statements[i]);
                return Arena.AddChildren(children.AsSpan(0, statements.Count));
            }
            finally
            {
                ArrayPool<int>.Shared.Return(children);
            }
        }

        private int LowerExpression(JsExpression expression)
        {
            return expression switch
            {
                JsLiteralExpression literal => LowerLiteral(literal),
                JsIdentifierExpression identifier => Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(identifier.Name),
                    identifier.NameId,
                    position: identifier.Position
                ),
                JsAssignmentExpression assignment => Arena.Add(
                    AstKind.AssignmentExpression,
                    LowerExpression(assignment.Left),
                    LowerExpression(assignment.Right),
                    (int)assignment.Operator,
                    assignment.Position
                ),
                JsBinaryExpression binary => Arena.Add(
                    AstKind.BinaryExpression,
                    LowerExpression(binary.Left),
                    LowerExpression(binary.Right),
                    (int)binary.Operator,
                    binary.Position
                ),
                JsUnaryExpression unary => Arena.Add(
                    AstKind.UnaryExpression,
                    LowerExpression(unary.Argument),
                    (int)unary.Operator,
                    position: unary.Position
                ),
                JsUpdateExpression update => Arena.Add(
                    AstKind.UpdateExpression,
                    LowerExpression(update.Argument),
                    (int)update.Operator,
                    update.IsPrefix ? 1 : 0,
                    update.Position
                ),
                JsConditionalExpression conditional => Arena.Add(
                    AstKind.ConditionalExpression,
                    LowerExpression(conditional.Test),
                    LowerExpression(conditional.Consequent),
                    LowerExpression(conditional.Alternate),
                    conditional.Position
                ),
                JsSequenceExpression sequence => LowerSequence(sequence),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support expression '{expression.GetType().Name}'."
                ),
            };
        }

        private int LowerSequence(JsSequenceExpression sequence)
        {
            var expressions = ArrayPool<int>.Shared.Rent(sequence.Expressions.Count);
            try
            {
                for (var i = 0; i < sequence.Expressions.Count; i++)
                    expressions[i] = LowerExpression(sequence.Expressions[i]);
                var children = Arena.AddChildren(expressions.AsSpan(0, sequence.Expressions.Count));
                return Arena.Add(
                    AstKind.SequenceExpression,
                    children.Offset,
                    children.Count,
                    position: sequence.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(expressions);
            }
        }

        private int LowerLiteral(JsLiteralExpression literal)
        {
            return literal.Value switch
            {
                null => Arena.Add(AstKind.NullLiteral, position: literal.Position),
                bool value => Arena.Add(
                    AstKind.BooleanLiteral,
                    value ? 1 : 0,
                    position: literal.Position
                ),
                int value => LowerNumber(value, literal.Position),
                long value => LowerNumber(value, literal.Position),
                double value => LowerNumber(value, literal.Position),
                string value => Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(value),
                    position: literal.Position
                ),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support literal '{literal.Text}'."
                ),
            };
        }

        private int LowerNumber(double value, int position)
        {
            return Arena.Add(AstKind.NumericLiteral, Arena.AddNumber(value), position: position);
        }
    }
}
