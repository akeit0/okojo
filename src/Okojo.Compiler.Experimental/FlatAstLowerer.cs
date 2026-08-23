using System.Buffers;
using Okojo.JavaScript.Parsing;
using Okojo.JavaScript.Values;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class FlatAstLowerer
{
    public static FlatAst Lower(JsProgram program)
    {
        var lowerer = new Lowerer(
            program.SourceText ?? string.Empty,
            program.SourcePath,
            nameof(JsPlannedScriptCompiler)
        );
        try
        {
            lowerer.Ast.StrictDeclared = program.StrictDeclared;
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
        var lowerer = new Lowerer(string.Empty, null, nameof(JsPlannedFunctionCompiler));
        try
        {
            lowerer.Ast.StrictDeclared = body.StrictDeclared;
            lowerer.Ast.Root = lowerer.LowerFunctionBody(body);
            return lowerer.Ast;
        }
        catch
        {
            lowerer.Ast.Dispose();
            throw;
        }
    }

    private sealed class Lowerer(string source, string? sourcePath, string compilerName)
    {
        public FlatAst Ast { get; } = new(source, sourcePath);
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
                JsEmptyObjectBindingDeclarationStatement emptyObjectBinding =>
                    LowerEmptyObjectBindingDeclaration(emptyObjectBinding),
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
                JsThrowStatement throwStatement => Arena.Add(
                    AstKind.ThrowStatement,
                    LowerExpression(throwStatement.Argument),
                    position: throwStatement.Position
                ),
                JsTryStatement tryStatement => LowerTryStatement(tryStatement),
                JsSwitchStatement switchStatement => LowerSwitchStatement(switchStatement),
                JsEmptyStatement => Arena.Add(AstKind.EmptyStatement, position: statement.Position),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support statement '{statement.GetType().Name}'."
                ),
            };
        }

        private int LowerSwitchStatement(JsSwitchStatement statement)
        {
            var cases = ArrayPool<int>.Shared.Rent(statement.Cases.Count);
            try
            {
                for (var i = 0; i < statement.Cases.Count; i++)
                {
                    var switchCase = statement.Cases[i];
                    var test = switchCase.Test is null ? -1 : LowerExpression(switchCase.Test);
                    var consequent = LowerStatements(switchCase.Consequent);
                    cases[i] = Arena.Add(
                        AstKind.SwitchCase,
                        test,
                        consequent.Offset,
                        consequent.Count,
                        switchCase.Position
                    );
                }

                var range = Arena.AddChildren(cases.AsSpan(0, statement.Cases.Count));
                return Arena.Add(
                    AstKind.SwitchStatement,
                    LowerExpression(statement.Discriminant),
                    range.Offset,
                    range.Count,
                    statement.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(cases);
            }
        }

        private int LowerTryStatement(JsTryStatement statement)
        {
            var handler = -1;
            if (statement.Handler is not null)
            {
                var binding =
                    statement.Handler.BindingPattern is not null
                        ? LowerBindingPattern(statement.Handler.BindingPattern)
                    : string.IsNullOrEmpty(statement.Handler.ParamName) ? -1
                    : Arena.Add(
                        AstKind.Identifier,
                        Arena.AddString(statement.Handler.ParamName!),
                        position: statement.Handler.Position
                    );
                handler = Arena.Add(
                    AstKind.CatchClause,
                    binding,
                    LowerBlock(statement.Handler.Body),
                    position: statement.Handler.Position
                );
            }
            return Arena.Add(
                AstKind.TryStatement,
                LowerBlock(statement.Block),
                handler,
                statement.Finalizer is null ? -1 : LowerBlock(statement.Finalizer),
                statement.Position
            );
        }

        private int LowerFunctionDeclaration(JsFunctionDeclaration function)
        {
            var bodyRoot = LowerFunctionBody(function.Body);
            var parameters = LowerParameters(
                function.Parameters,
                function.ParameterIds,
                function.ParameterInitializers,
                function.ParameterPatterns,
                function.ParameterPositions,
                function.ParameterBindingKinds
            );
            var functionIndex = Ast.AddFunction(
                new FlatFunctionInfo(
                    Arena.AddString(function.Name),
                    function.NameId,
                    parameters.Offset,
                    parameters.Count,
                    function.FunctionLength,
                    function.RestParameterIndex,
                    function.Body.StrictDeclared,
                    function.HasSimpleParameterList,
                    function.HasDuplicateParameters,
                    function.Position,
                    false
                )
            );
            return Arena.Add(
                AstKind.FunctionDeclaration,
                functionIndex,
                bodyRoot,
                position: function.Position
            );
        }

        private int LowerFunctionExpression(JsFunctionExpression function)
        {
            if (function.IsGenerator || function.IsAsync)
                throw new NotSupportedException(
                    $"{compilerName} only supports synchronous flat function expressions."
                );

            var bodyRoot = LowerFunctionBody(function.Body);
            var parameters = LowerParameters(
                function.Parameters,
                function.ParameterIds,
                function.ParameterInitializers,
                function.ParameterPatterns,
                function.ParameterPositions,
                function.ParameterBindingKinds
            );
            var functionIndex = Ast.AddFunction(
                new FlatFunctionInfo(
                    Arena.AddString(function.Name ?? string.Empty),
                    function.NameId,
                    parameters.Offset,
                    parameters.Count,
                    function.FunctionLength,
                    function.RestParameterIndex,
                    function.Body.StrictDeclared,
                    function.HasSimpleParameterList,
                    function.HasDuplicateParameters,
                    function.Position,
                    function.HasSuperBindingHint,
                    function.IsArrow
                )
            );
            return Arena.Add(
                function.IsArrow ? AstKind.ArrowFunctionExpression : AstKind.FunctionExpression,
                functionIndex,
                bodyRoot,
                position: function.Position
            );
        }

        private (int Offset, int Count) LowerParameters(
            IReadOnlyList<string> names,
            IReadOnlyList<int> nameIds,
            IReadOnlyList<JsExpression?> initializers,
            IReadOnlyList<JsExpression?> patterns,
            IReadOnlyList<int> positions,
            IReadOnlyList<JsFormalParameterBindingKind> kinds
        )
        {
            var flatParameters = ArrayPool<FlatParameter>.Shared.Rent(names.Count);
            try
            {
                for (var i = 0; i < names.Count; i++)
                    flatParameters[i] = new FlatParameter(
                        Arena.AddString(names[i]),
                        nameIds[i],
                        initializers[i] is null ? -1 : LowerExpression(initializers[i]!),
                        patterns[i] is null ? -1 : LowerBindingPattern(patterns[i]!),
                        positions[i],
                        kinds[i]
                    );
                return Ast.AddParameters(flatParameters.AsSpan(0, names.Count));
            }
            finally
            {
                ArrayPool<FlatParameter>.Shared.Return(flatParameters);
            }
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
            {
                if (declaration.BindingInitializer is null)
                    throw new InvalidOperationException(
                        "Binding declaration is missing its initializer."
                    );
                Span<int> patternDeclarator =
                [
                    Arena.Add(
                        AstKind.VariableDeclaratorPattern,
                        LowerBindingPattern(declaration.BindingPattern),
                        LowerExpression(declaration.BindingInitializer),
                        position: declaration.Position
                    ),
                ];
                var patternChildren = Arena.AddChildren(patternDeclarator);
                return Arena.Add(
                    AstKind.VariableDeclaration,
                    patternChildren.Offset,
                    patternChildren.Count,
                    (int)declaration.Kind,
                    declaration.Position
                );
            }

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

        private int LowerBindingPattern(JsExpression pattern)
        {
            return pattern switch
            {
                JsIdentifierExpression identifier => Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(identifier.Name),
                    identifier.NameId,
                    position: identifier.Position
                ),
                JsSpreadExpression spread => Arena.Add(
                    AstKind.SpreadElement,
                    LowerBindingPattern(spread.Argument),
                    position: spread.Position
                ),
                JsAssignmentExpression { Operator: JsAssignmentOperator.Assign } assignment =>
                    Arena.Add(
                        AstKind.AssignmentExpression,
                        LowerBindingPattern(assignment.Left),
                        LowerExpression(assignment.Right),
                        (int)JsAssignmentOperator.Assign,
                        assignment.Position
                    ),
                JsArrayExpression array => LowerArrayBindingArray(array),
                JsObjectExpression @object => LowerObjectBindingObject(@object),
                _ => throw new NotSupportedException(
                    $"Binding target '{pattern.GetType().Name}' is not supported by {compilerName}."
                ),
            };
        }

        private int LowerArrayBindingArray(JsArrayExpression array)
        {
            var elements = ArrayPool<int>.Shared.Rent(array.Elements.Count);
            try
            {
                for (var i = 0; i < array.Elements.Count; i++)
                    elements[i] = array.Elements[i] is { } element
                        ? LowerBindingPattern(element)
                        : -1;
                var children = Arena.AddChildren(elements.AsSpan(0, array.Elements.Count));
                return Arena.Add(
                    AstKind.ArrayBindingPattern,
                    children.Offset,
                    children.Count,
                    position: array.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(elements);
            }
        }

        private int LowerObjectBindingObject(JsObjectExpression @object)
        {
            var properties = ArrayPool<FlatObjectProperty>.Shared.Rent(@object.Properties.Count);
            try
            {
                for (var i = 0; i < @object.Properties.Count; i++)
                {
                    var property = @object.Properties[i];
                    if (property.Kind == JsObjectPropertyKind.Spread)
                    {
                        if (i != @object.Properties.Count - 1)
                            throw new NotSupportedException(
                                "Object rest binding must be the final property."
                            );
                        properties[i] = new(
                            -1,
                            LowerBindingPattern(property.Value),
                            property.Position,
                            FlatObjectPropertyFlags.Rest
                        );
                        continue;
                    }
                    if (property.Kind != JsObjectPropertyKind.Data)
                        throw new NotSupportedException(
                            $"Object binding property '{property.Kind}' is not supported by {compilerName}."
                        );

                    properties[i] = new(
                        property.IsComputed
                            ? LowerExpression(property.ComputedKey!)
                            : Arena.AddString(property.Key),
                        LowerBindingPattern(property.Value),
                        property.Position,
                        property.IsComputed
                            ? FlatObjectPropertyFlags.Computed
                            : FlatObjectPropertyFlags.None
                    );
                }

                var range = Ast.AddObjectProperties(properties.AsSpan(0, @object.Properties.Count));
                return Arena.Add(
                    AstKind.ObjectBindingPattern,
                    range.Offset,
                    range.Count,
                    position: @object.Position
                );
            }
            finally
            {
                ArrayPool<FlatObjectProperty>.Shared.Return(properties);
            }
        }

        private int LowerEmptyObjectBindingDeclaration(
            JsEmptyObjectBindingDeclarationStatement declaration
        )
        {
            var range = Ast.AddObjectProperties(ReadOnlySpan<FlatObjectProperty>.Empty);
            var pattern = Arena.Add(
                AstKind.ObjectBindingPattern,
                range.Offset,
                range.Count,
                position: declaration.Position
            );
            Span<int> declarator =
            [
                Arena.Add(
                    AstKind.VariableDeclaratorPattern,
                    pattern,
                    LowerExpression(declaration.Initializer),
                    position: declaration.Position
                ),
            ];
            var children = Arena.AddChildren(declarator);
            return Arena.Add(
                AstKind.VariableDeclaration,
                children.Offset,
                children.Count,
                (int)declaration.Kind,
                declaration.Position
            );
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
                JsRegExpLiteralExpression regexp => Arena.Add(
                    AstKind.RegExpLiteral,
                    Arena.AddString(regexp.Pattern),
                    Arena.AddString(regexp.Flags),
                    position: regexp.Position
                ),
                JsThisExpression => Arena.Add(
                    AstKind.ThisExpression,
                    position: expression.Position
                ),
                JsIdentifierExpression identifier => Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(identifier.Name),
                    identifier.NameId,
                    position: identifier.Position
                ),
                JsAssignmentExpression assignment => LowerAssignment(assignment),
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
                JsFunctionExpression function => LowerFunctionExpression(function),
                JsCallExpression call => LowerCall(call),
                JsNewExpression @new => LowerNew(@new),
                JsMemberExpression member => LowerMember(member),
                JsArrayExpression array => LowerArray(array),
                JsObjectExpression obj => LowerObject(obj),
                JsTemplateExpression template => LowerTemplate(template),
                _ => throw new NotSupportedException(
                    $"{compilerName} does not support expression '{expression.GetType().Name}'."
                ),
            };
        }

        private int LowerAssignment(JsAssignmentExpression assignment)
        {
            var left =
                assignment.Operator == JsAssignmentOperator.Assign
                && assignment.Left is JsArrayExpression or JsObjectExpression
                    ? LowerAssignmentTarget(assignment.Left)
                    : LowerExpression(assignment.Left);
            return Arena.Add(
                AstKind.AssignmentExpression,
                left,
                LowerExpression(assignment.Right),
                (int)assignment.Operator,
                assignment.Position
            );
        }

        private int LowerAssignmentTarget(JsExpression target)
        {
            return target switch
            {
                JsIdentifierExpression or JsMemberExpression => LowerExpression(target),
                JsAssignmentExpression { Operator: JsAssignmentOperator.Assign } assignment =>
                    Arena.Add(
                        AstKind.AssignmentExpression,
                        LowerAssignmentTarget(assignment.Left),
                        LowerExpression(assignment.Right),
                        (int)JsAssignmentOperator.Assign,
                        assignment.Position
                    ),
                JsSpreadExpression spread => Arena.Add(
                    AstKind.SpreadElement,
                    LowerAssignmentTarget(spread.Argument),
                    position: spread.Position
                ),
                JsArrayExpression array => LowerArrayAssignmentTarget(array),
                JsObjectExpression obj => LowerObjectAssignmentTarget(obj),
                _ => throw new NotSupportedException(
                    $"Assignment target '{target.GetType().Name}' is not supported by {compilerName}."
                ),
            };
        }

        private int LowerArrayAssignmentTarget(JsArrayExpression array)
        {
            var elements = ArrayPool<int>.Shared.Rent(array.Elements.Count);
            try
            {
                for (var i = 0; i < array.Elements.Count; i++)
                    elements[i] = array.Elements[i] is { } element
                        ? LowerAssignmentTarget(element)
                        : -1;
                var children = Arena.AddChildren(elements.AsSpan(0, array.Elements.Count));
                return Arena.Add(
                    AstKind.ArrayExpression,
                    children.Offset,
                    children.Count,
                    position: array.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(elements);
            }
        }

        private int LowerObjectAssignmentTarget(JsObjectExpression obj)
        {
            var properties = ArrayPool<FlatObjectProperty>.Shared.Rent(obj.Properties.Count);
            try
            {
                for (var i = 0; i < obj.Properties.Count; i++)
                {
                    var property = obj.Properties[i];
                    if (property.Kind == JsObjectPropertyKind.Spread)
                    {
                        properties[i] = new(
                            -1,
                            LowerAssignmentTarget(property.Value),
                            property.Position,
                            FlatObjectPropertyFlags.Rest
                        );
                        continue;
                    }
                    if (property.Kind != JsObjectPropertyKind.Data)
                        throw new NotSupportedException(
                            $"Object assignment property '{property.Kind}' is not supported by {compilerName}."
                        );
                    properties[i] = new(
                        property.IsComputed
                            ? LowerExpression(property.ComputedKey!)
                            : Arena.AddString(property.Key),
                        LowerAssignmentTarget(property.Value),
                        property.Position,
                        property.IsComputed
                            ? FlatObjectPropertyFlags.Computed
                            : FlatObjectPropertyFlags.None
                    );
                }
                var range = Ast.AddObjectProperties(properties.AsSpan(0, obj.Properties.Count));
                return Arena.Add(
                    AstKind.ObjectExpression,
                    range.Offset,
                    range.Count,
                    position: obj.Position
                );
            }
            finally
            {
                ArrayPool<FlatObjectProperty>.Shared.Return(properties);
            }
        }

        private int LowerArray(JsArrayExpression array)
        {
            var elements = ArrayPool<int>.Shared.Rent(array.Elements.Count);
            try
            {
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    elements[i] = array.Elements[i] switch
                    {
                        null => -1,
                        JsSpreadExpression => throw new NotSupportedException(
                            $"Array spread is not supported by {compilerName}."
                        ),
                        var element => LowerExpression(element),
                    };
                }
                var children = Arena.AddChildren(elements.AsSpan(0, array.Elements.Count));
                return Arena.Add(
                    AstKind.ArrayExpression,
                    children.Offset,
                    children.Count,
                    position: array.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(elements);
            }
        }

        private int LowerObject(JsObjectExpression obj)
        {
            var properties = ArrayPool<FlatObjectProperty>.Shared.Rent(obj.Properties.Count);
            try
            {
                for (var i = 0; i < obj.Properties.Count; i++)
                {
                    var property = obj.Properties[i];
                    if (property.Kind == JsObjectPropertyKind.Spread)
                    {
                        properties[i] = new FlatObjectProperty(
                            -1,
                            LowerExpression(property.Value),
                            property.Position,
                            FlatObjectPropertyFlags.Rest
                        );
                        continue;
                    }

                    var flags = property.IsComputed
                        ? FlatObjectPropertyFlags.Computed
                        : FlatObjectPropertyFlags.None;
                    if (property.Kind == JsObjectPropertyKind.Getter)
                        flags |= FlatObjectPropertyFlags.Getter;
                    else if (property.Kind == JsObjectPropertyKind.Setter)
                        flags |= FlatObjectPropertyFlags.Setter;

                    properties[i] = new FlatObjectProperty(
                        property.IsComputed
                            ? LowerExpression(property.ComputedKey!)
                            : Arena.AddString(property.Key),
                        LowerExpression(property.Value),
                        property.Position,
                        flags
                    );
                }
                var range = Ast.AddObjectProperties(properties.AsSpan(0, obj.Properties.Count));
                return Arena.Add(
                    AstKind.ObjectExpression,
                    range.Offset,
                    range.Count,
                    position: obj.Position
                );
            }
            finally
            {
                ArrayPool<FlatObjectProperty>.Shared.Return(properties);
            }
        }

        private int LowerCall(JsCallExpression call)
        {
            if (call.IsOptionalChainSegment)
                throw new NotSupportedException(
                    $"Optional calls are not supported by {compilerName}."
                );

            var arguments = ArrayPool<int>.Shared.Rent(call.Arguments.Count);
            try
            {
                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    arguments[i] = call.Arguments[i] is JsSpreadExpression spread
                        ? Arena.Add(
                            AstKind.SpreadElement,
                            LowerExpression(spread.Argument),
                            position: spread.Position
                        )
                        : LowerExpression(call.Arguments[i]);
                }
                var children = Arena.AddChildren(arguments.AsSpan(0, call.Arguments.Count));
                return Arena.Add(
                    AstKind.CallExpression,
                    LowerExpression(call.Callee),
                    children.Offset,
                    children.Count,
                    call.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(arguments);
            }
        }

        private int LowerNew(JsNewExpression @new)
        {
            var arguments = ArrayPool<int>.Shared.Rent(@new.Arguments.Count);
            try
            {
                for (var i = 0; i < @new.Arguments.Count; i++)
                {
                    arguments[i] = @new.Arguments[i] is JsSpreadExpression spread
                        ? Arena.Add(
                            AstKind.SpreadElement,
                            LowerExpression(spread.Argument),
                            position: spread.Position
                        )
                        : LowerExpression(@new.Arguments[i]);
                }
                var children = Arena.AddChildren(arguments.AsSpan(0, @new.Arguments.Count));
                return Arena.Add(
                    AstKind.NewExpression,
                    LowerExpression(@new.Callee),
                    children.Offset,
                    children.Count,
                    @new.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(arguments);
            }
        }

        private int LowerMember(JsMemberExpression member)
        {
            if (member.IsPrivate || member.IsOptionalChainSegment)
                throw new NotSupportedException(
                    $"Private and optional members are not supported by {compilerName}."
                );

            var property =
                member.IsComputed ? LowerExpression(member.Property)
                : member.Property is JsLiteralExpression { Value: string name }
                    ? Arena.AddString(name)
                : throw new NotSupportedException(
                    $"Named member shape is not supported by {compilerName}."
                );
            return Arena.Add(
                AstKind.MemberExpression,
                LowerExpression(member.Object),
                property,
                (int)(member.IsComputed ? AstMemberFlags.Computed : AstMemberFlags.None),
                member.Position
            );
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

        private int LowerTemplate(JsTemplateExpression template)
        {
            if (template.Expressions.Count == 0)
                return Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(
                        template.Quasis[0]
                            ?? throw new NotSupportedException(
                                "Invalid cooked template quasi requires tagged-template lowering."
                            )
                    ),
                    position: template.Position
                );

            var count = template.Expressions.Count * 2 + 1;
            var parts = ArrayPool<int>.Shared.Rent(count);
            try
            {
                for (var i = 0; i < template.Expressions.Count; i++)
                {
                    parts[i * 2] = Arena.Add(
                        AstKind.StringLiteral,
                        Arena.AddString(
                            template.Quasis[i]
                                ?? throw new NotSupportedException(
                                    "Invalid cooked template quasi requires tagged-template lowering."
                                )
                        ),
                        position: template.Position
                    );
                    parts[i * 2 + 1] = LowerExpression(template.Expressions[i]);
                }

                parts[count - 1] = Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(
                        template.Quasis[^1]
                            ?? throw new NotSupportedException(
                                "Invalid cooked template quasi requires tagged-template lowering."
                            )
                    ),
                    position: template.Position
                );
                var children = Arena.AddChildren(parts.AsSpan(0, count));
                return Arena.Add(
                    AstKind.TemplateExpression,
                    children.Offset,
                    children.Count,
                    position: template.Position
                );
            }
            finally
            {
                ArrayPool<int>.Shared.Return(parts);
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
                JsBigInt value => Arena.Add(
                    AstKind.BigIntLiteral,
                    Arena.AddString(value.Value.ToString()),
                    position: literal.Position
                ),
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
