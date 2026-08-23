using System.Buffers;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class DirectFlatParser
{
    private readonly FlatAst ast;
    private readonly JsLexer lexer;
    private readonly string source;
    private JsToken current;
    private int functionDepth;
    private int loopDepth;
    private bool strictMode;

    private DirectFlatParser(string source, string? sourcePath)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ast = new FlatAst(source, sourcePath);
        lexer = new JsLexer(source);
        current = lexer.NextToken();
    }

    public static FlatAst ParseScript(string source, string? sourcePath = null)
    {
        var parser = new DirectFlatParser(source, sourcePath);
        try
        {
            parser.ast.Root = parser.ParseProgram();
            return parser.ast;
        }
        catch
        {
            parser.ast.Dispose();
            throw;
        }
    }

    private AstArena Arena => ast.Arena;

    private int ParseProgram()
    {
        Span<int> initial = stackalloc int[16];
        var statements = new NodeList(initial);
        var allowsDirectives = true;
        try
        {
            while (current.Kind != JsTokenKind.Eof)
            {
                var statement = ParseStatement();
                statements.Add(statement);
                if (allowsDirectives && IsUseStrictDirective(statement))
                {
                    ast.StrictDeclared = true;
                    strictMode = true;
                }
                else
                    allowsDirectives = false;
            }

            var children = Arena.AddChildren(statements.AsSpan());
            return Arena.Add(AstKind.Program, children.Offset, children.Count);
        }
        finally
        {
            statements.Dispose();
        }
    }

    private int ParseStatement()
    {
        var position = current.Position;
        return current.Kind switch
        {
            JsTokenKind.Semicolon => ParseEmptyStatement(position),
            JsTokenKind.LeftBrace => ParseBlock(out _),
            JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const => ParseVariableDeclaration(
                consumeSemicolon: true
            ),
            JsTokenKind.Function => ParseFunctionDeclaration(),
            JsTokenKind.If => ParseIfStatement(),
            JsTokenKind.While => ParseWhileStatement(),
            JsTokenKind.Do => ParseDoWhileStatement(),
            JsTokenKind.For => ParseForStatement(),
            JsTokenKind.Break => ParseLoopControl(AstKind.BreakStatement),
            JsTokenKind.Continue => ParseLoopControl(AstKind.ContinueStatement),
            JsTokenKind.Return => ParseReturnStatement(),
            JsTokenKind.Throw
            or JsTokenKind.Try
            or JsTokenKind.Switch
            or JsTokenKind.With
            or JsTokenKind.Debugger => throw UnsupportedStatement(current.Kind),
            _ => ParseExpressionStatement(),
        };
    }

    private int ParseEmptyStatement(int position)
    {
        Next();
        return Arena.Add(AstKind.EmptyStatement, position: position);
    }

    private int ParseBlock(
        out bool strictDeclared,
        AstKind rootKind = AstKind.BlockStatement,
        bool allowDirectives = false
    )
    {
        var position = Expect(JsTokenKind.LeftBrace).Position;
        Span<int> initial = stackalloc int[8];
        var statements = new NodeList(initial);
        var allowsDirectives = allowDirectives;
        strictDeclared = false;
        try
        {
            while (current.Kind != JsTokenKind.RightBrace)
            {
                if (current.Kind == JsTokenKind.Eof)
                    throw Error("Unterminated block", position);
                var statement = ParseStatement();
                statements.Add(statement);
                if (allowsDirectives && IsUseStrictDirective(statement))
                {
                    strictDeclared = true;
                    strictMode = true;
                }
                else
                    allowsDirectives = false;
            }

            Next();
            var children = Arena.AddChildren(statements.AsSpan());
            return Arena.Add(rootKind, children.Offset, children.Count, position: position);
        }
        finally
        {
            statements.Dispose();
        }
    }

    private int ParseVariableDeclaration(bool consumeSemicolon)
    {
        var position = current.Position;
        var kind = current.Kind switch
        {
            JsTokenKind.Var => JsVariableDeclarationKind.Var,
            JsTokenKind.Let => JsVariableDeclarationKind.Let,
            JsTokenKind.Const => JsVariableDeclarationKind.Const,
            _ => throw Error("Expected variable declaration", current.Position),
        };
        Next();

        Span<int> initial = stackalloc int[4];
        var declarators = new NodeList(initial);
        try
        {
            do
            {
                var identifier = ExpectIdentifier();
                var initializer = -1;
                if (Match(JsTokenKind.Assign))
                    initializer = ParseAssignment(allowIn: true);
                else if (kind == JsVariableDeclarationKind.Const)
                    throw Error("Const declaration requires initializer", identifier.Position);
                declarators.Add(
                    Arena.Add(
                        AstKind.VariableDeclarator,
                        Arena.AddString(GetIdentifierText(identifier)),
                        identifier.IdentifierId,
                        initializer,
                        identifier.Position
                    )
                );
            } while (Match(JsTokenKind.Comma));

            if (consumeSemicolon)
                ConsumeSemicolon();
            var children = Arena.AddChildren(declarators.AsSpan());
            return Arena.Add(
                AstKind.VariableDeclaration,
                children.Offset,
                children.Count,
                (int)kind,
                position
            );
        }
        finally
        {
            declarators.Dispose();
        }
    }

    private int ParseFunctionDeclaration()
    {
        var position = Expect(JsTokenKind.Function).Position;
        if (Match(JsTokenKind.Star))
            throw Error("Generator functions are not supported by DirectFlatParser", position);
        var nameToken = ExpectIdentifier();
        var name = GetIdentifierText(nameToken);
        Expect(JsTokenKind.LeftParen);
        var names = new List<string>();
        var nameIds = new List<int>();
        if (current.Kind != JsTokenKind.RightParen)
        {
            do
            {
                var parameter = ExpectIdentifier();
                names.Add(GetIdentifierText(parameter));
                nameIds.Add(parameter.IdentifierId);
                if (current.Kind is JsTokenKind.Assign or JsTokenKind.Ellipsis)
                    throw Error(
                        "Advanced parameters are not supported by DirectFlatParser",
                        current.Position
                    );
            } while (Match(JsTokenKind.Comma));
        }
        Expect(JsTokenKind.RightParen);
        var strictBeforeFunction = strictMode;
        var loopDepthBeforeFunction = loopDepth;
        loopDepth = 0;
        functionDepth++;
        int body;
        bool strictDeclared;
        try
        {
            body = ParseBlock(out strictDeclared, AstKind.Program, allowDirectives: true);
        }
        finally
        {
            functionDepth--;
            loopDepth = loopDepthBeforeFunction;
        }
        var effectiveStrict = strictBeforeFunction || strictDeclared;
        strictMode = strictBeforeFunction;
        var parameterPlan = FunctionParameterPlan.FromCompilerInputs(
            names,
            nameIds,
            new JsExpression?[names.Count],
            -1
        );
        var functionIndex = ast.AddFunction(
            new FlatFunctionInfo(
                name,
                nameToken.IdentifierId,
                parameterPlan,
                effectiveStrict,
                position
            )
        );
        return Arena.Add(AstKind.FunctionDeclaration, functionIndex, body, position: position);
    }

    private int ParseIfStatement()
    {
        var position = Expect(JsTokenKind.If).Position;
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        var consequent = ParseStatement();
        var alternate = Match(JsTokenKind.Else) ? ParseStatement() : -1;
        return Arena.Add(AstKind.IfStatement, test, consequent, alternate, position);
    }

    private int ParseWhileStatement()
    {
        var position = Expect(JsTokenKind.While).Position;
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        return Arena.Add(AstKind.WhileStatement, test, ParseLoopBody(), position: position);
    }

    private int ParseDoWhileStatement()
    {
        var position = Expect(JsTokenKind.Do).Position;
        var body = ParseLoopBody();
        Expect(JsTokenKind.While);
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        ConsumeSemicolon();
        return Arena.Add(AstKind.DoWhileStatement, body, test, position: position);
    }

    private int ParseForStatement()
    {
        var position = Expect(JsTokenKind.For).Position;
        Expect(JsTokenKind.LeftParen);
        var init = -1;
        if (current.Kind != JsTokenKind.Semicolon)
        {
            init = current.Kind is JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const
                ? ParseVariableDeclaration(consumeSemicolon: false)
                : ParseExpression(allowIn: false);
        }
        if (current.Kind is JsTokenKind.In or JsTokenKind.Of)
            throw Error("for-in/of is not supported by DirectFlatParser", current.Position);
        Expect(JsTokenKind.Semicolon);
        var test = current.Kind == JsTokenKind.Semicolon ? -1 : ParseExpression();
        Expect(JsTokenKind.Semicolon);
        var update = current.Kind == JsTokenKind.RightParen ? -1 : ParseExpression();
        Expect(JsTokenKind.RightParen);
        Span<int> parts = [init, test, update, ParseLoopBody()];
        var children = Arena.AddChildren(parts);
        return Arena.Add(AstKind.ForStatement, children.Offset, children.Count, position: position);
    }

    private int ParseLoopControl(AstKind kind)
    {
        var position = current.Position;
        if (loopDepth == 0)
            throw Error($"Illegal {kind}", position);
        Next();
        if (
            !current.HasLineTerminatorBefore
            && current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
        )
            throw Error("Labeled loop control is not supported by DirectFlatParser", position);
        ConsumeSemicolon();
        return Arena.Add(kind, position: position);
    }

    private int ParseReturnStatement()
    {
        var position = current.Position;
        if (functionDepth == 0)
            throw Error("Illegal return statement", position);
        Next();
        var argument =
            current.HasLineTerminatorBefore
            || current.Kind is JsTokenKind.Semicolon or JsTokenKind.RightBrace or JsTokenKind.Eof
                ? -1
                : ParseExpression();
        ConsumeSemicolon();
        return Arena.Add(AstKind.ReturnStatement, argument, position: position);
    }

    private int ParseLoopBody()
    {
        loopDepth++;
        try
        {
            return ParseStatement();
        }
        finally
        {
            loopDepth--;
        }
    }

    private int ParseExpressionStatement()
    {
        var position = current.Position;
        var expression = ParseExpression();
        ConsumeSemicolon();
        return Arena.Add(AstKind.ExpressionStatement, expression, position: position);
    }

    private int ParseExpression(bool allowIn = true)
    {
        var position = current.Position;
        var first = ParseAssignment(allowIn);
        if (!Match(JsTokenKind.Comma))
            return first;

        Span<int> initial = stackalloc int[4];
        var expressions = new NodeList(initial);
        try
        {
            expressions.Add(first);
            do
            {
                expressions.Add(ParseAssignment(allowIn));
            } while (Match(JsTokenKind.Comma));
            var children = Arena.AddChildren(expressions.AsSpan());
            return Arena.Add(
                AstKind.SequenceExpression,
                children.Offset,
                children.Count,
                position: position
            );
        }
        finally
        {
            expressions.Dispose();
        }
    }

    private int ParseAssignment(bool allowIn)
    {
        var position = current.Position;
        var left = ParseConditional(allowIn);
        if (!TryGetAssignmentOperator(current.Kind, out var op))
            return left;
        if (Arena[left].Kind != AstKind.Identifier)
            throw Error("DirectFlatParser supports only identifier assignment targets", position);
        Next();
        return Arena.Add(
            AstKind.AssignmentExpression,
            left,
            ParseAssignment(allowIn),
            (int)op,
            position
        );
    }

    private int ParseConditional(bool allowIn)
    {
        var position = current.Position;
        var test = ParseBinary(allowIn, 1);
        if (!Match(JsTokenKind.Question))
            return test;
        var consequent = ParseAssignment(allowIn: true);
        Expect(JsTokenKind.Colon);
        return Arena.Add(
            AstKind.ConditionalExpression,
            test,
            consequent,
            ParseAssignment(allowIn),
            position
        );
    }

    private int ParseBinary(bool allowIn, int minimumPrecedence)
    {
        var left = ParseUnary();
        while (
            TryGetBinaryOperator(
                current.Kind,
                allowIn,
                out var op,
                out var precedence,
                out var rightAssociative
            )
            && precedence >= minimumPrecedence
        )
        {
            var position = Arena.GetPosition(left);
            Next();
            var right = ParseBinary(allowIn, rightAssociative ? precedence : precedence + 1);
            left = Arena.Add(AstKind.BinaryExpression, left, right, (int)op, position);
        }
        return left;
    }

    private int ParseUnary()
    {
        var position = current.Position;
        if (TryGetUnaryOperator(current.Kind, out var unary))
        {
            Next();
            return Arena.Add(AstKind.UnaryExpression, ParseUnary(), (int)unary, position: position);
        }

        if (current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus)
        {
            var op =
                current.Kind == JsTokenKind.PlusPlus
                    ? JsUpdateOperator.Increment
                    : JsUpdateOperator.Decrement;
            Next();
            var argument = ParseUnary();
            EnsureIdentifierUpdateTarget(argument, position);
            return Arena.Add(AstKind.UpdateExpression, argument, (int)op, 1, position);
        }

        var expression = ParsePrimary();
        if (
            !current.HasLineTerminatorBefore
            && current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus
        )
        {
            var op =
                current.Kind == JsTokenKind.PlusPlus
                    ? JsUpdateOperator.Increment
                    : JsUpdateOperator.Decrement;
            Next();
            EnsureIdentifierUpdateTarget(expression, position);
            expression = Arena.Add(AstKind.UpdateExpression, expression, (int)op, 0, position);
        }
        if (current.Kind is JsTokenKind.LeftParen or JsTokenKind.Dot or JsTokenKind.LeftBracket)
            throw Error(
                "Call and member expressions are not supported by DirectFlatParser",
                current.Position
            );
        return expression;
    }

    private int ParsePrimary()
    {
        var token = current;
        switch (token.Kind)
        {
            case JsTokenKind.Identifier:
                Next();
                return Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(GetIdentifierText(token)),
                    token.IdentifierId,
                    position: token.Position
                );
            case JsTokenKind.Number:
                Next();
                return Arena.Add(
                    AstKind.NumericLiteral,
                    Arena.AddNumber(token.NumberLiteral),
                    position: token.Position
                );
            case JsTokenKind.String:
                Next();
                return Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(lexer.GetStringLiteral(token)),
                    position: token.Position
                );
            case JsTokenKind.True:
            case JsTokenKind.False:
                Next();
                return Arena.Add(
                    AstKind.BooleanLiteral,
                    token.Kind == JsTokenKind.True ? 1 : 0,
                    position: token.Position
                );
            case JsTokenKind.Null:
                Next();
                return Arena.Add(AstKind.NullLiteral, position: token.Position);
            case JsTokenKind.LeftParen:
                Next();
                var expression = ParseExpression();
                Expect(JsTokenKind.RightParen);
                return expression;
            default:
                throw Error(
                    $"Expression token '{token.Kind}' is not supported by DirectFlatParser",
                    token.Position
                );
        }
    }

    private bool IsUseStrictDirective(int statement)
    {
        ref readonly var node = ref Arena[statement];
        if (node.Kind != AstKind.ExpressionStatement)
            return false;
        ref readonly var expression = ref Arena[node.Arg0];
        return expression.Kind == AstKind.StringLiteral
            && string.Equals(
                Arena.GetString(expression.Arg0),
                "use strict",
                StringComparison.Ordinal
            );
    }

    private void EnsureIdentifierUpdateTarget(int node, int position)
    {
        if (Arena[node].Kind != AstKind.Identifier)
            throw Error("DirectFlatParser supports only identifier update targets", position);
    }

    private string GetIdentifierText(in JsToken token)
    {
        return token.DataIndex >= 0
            ? lexer.GetIdentifierLiteral(token)
            : source.Substring(token.Position, token.SourceLength);
    }

    private JsToken ExpectIdentifier()
    {
        if (current.Kind != JsTokenKind.Identifier)
            throw Error($"Expected identifier but found {current.Kind}", current.Position);
        var token = current;
        Next();
        return token;
    }

    private JsToken Expect(JsTokenKind kind)
    {
        if (current.Kind != kind)
            throw Error($"Expected {kind} but found {current.Kind}", current.Position);
        var token = current;
        Next();
        return token;
    }

    private bool Match(JsTokenKind kind)
    {
        if (current.Kind != kind)
            return false;
        Next();
        return true;
    }

    private void ConsumeSemicolon()
    {
        if (Match(JsTokenKind.Semicolon))
            return;
        if (
            current.Kind is JsTokenKind.RightBrace or JsTokenKind.Eof
            || current.HasLineTerminatorBefore
        )
            return;
        throw Error("Expected semicolon", current.Position);
    }

    private void Next()
    {
        current = lexer.NextToken();
    }

    private JsParseException UnsupportedStatement(JsTokenKind kind)
    {
        return Error($"Statement '{kind}' is not supported by DirectFlatParser", current.Position);
    }

    private JsParseException Error(string message, int position)
    {
        return new JsParseException(message, position, source);
    }

    private static bool TryGetAssignmentOperator(JsTokenKind kind, out JsAssignmentOperator op)
    {
        op = kind switch
        {
            JsTokenKind.Assign => JsAssignmentOperator.Assign,
            JsTokenKind.PlusAssign => JsAssignmentOperator.AddAssign,
            JsTokenKind.MinusAssign => JsAssignmentOperator.SubtractAssign,
            JsTokenKind.StarAssign => JsAssignmentOperator.MultiplyAssign,
            JsTokenKind.PowAssign => JsAssignmentOperator.ExponentiateAssign,
            JsTokenKind.SlashAssign => JsAssignmentOperator.DivideAssign,
            JsTokenKind.PercentAssign => JsAssignmentOperator.ModuloAssign,
            JsTokenKind.ShlAssign => JsAssignmentOperator.ShiftLeftAssign,
            JsTokenKind.SarAssign => JsAssignmentOperator.ShiftRightAssign,
            JsTokenKind.ShrAssign => JsAssignmentOperator.ShiftRightLogicalAssign,
            JsTokenKind.AmpersandAssign => JsAssignmentOperator.BitwiseAndAssign,
            JsTokenKind.PipeAssign => JsAssignmentOperator.BitwiseOrAssign,
            JsTokenKind.CaretAssign => JsAssignmentOperator.BitwiseXorAssign,
            JsTokenKind.AndAndAssign => JsAssignmentOperator.LogicalAndAssign,
            JsTokenKind.OrOrAssign => JsAssignmentOperator.LogicalOrAssign,
            JsTokenKind.NullishCoalescingAssign => JsAssignmentOperator.NullishCoalescingAssign,
            _ => default,
        };
        return kind
            is JsTokenKind.Assign
                or JsTokenKind.PlusAssign
                or JsTokenKind.MinusAssign
                or JsTokenKind.StarAssign
                or JsTokenKind.PowAssign
                or JsTokenKind.SlashAssign
                or JsTokenKind.PercentAssign
                or JsTokenKind.ShlAssign
                or JsTokenKind.SarAssign
                or JsTokenKind.ShrAssign
                or JsTokenKind.AmpersandAssign
                or JsTokenKind.PipeAssign
                or JsTokenKind.CaretAssign
                or JsTokenKind.AndAndAssign
                or JsTokenKind.OrOrAssign
                or JsTokenKind.NullishCoalescingAssign;
    }

    private static bool TryGetUnaryOperator(JsTokenKind kind, out JsUnaryOperator op)
    {
        op = kind switch
        {
            JsTokenKind.Plus => JsUnaryOperator.Plus,
            JsTokenKind.Minus => JsUnaryOperator.Minus,
            JsTokenKind.Bang => JsUnaryOperator.LogicalNot,
            JsTokenKind.Tilde => JsUnaryOperator.BitwiseNot,
            JsTokenKind.Typeof => JsUnaryOperator.Typeof,
            JsTokenKind.Void => JsUnaryOperator.Void,
            JsTokenKind.Delete => JsUnaryOperator.Delete,
            _ => default,
        };
        return kind
            is JsTokenKind.Plus
                or JsTokenKind.Minus
                or JsTokenKind.Bang
                or JsTokenKind.Tilde
                or JsTokenKind.Typeof
                or JsTokenKind.Void
                or JsTokenKind.Delete;
    }

    private static bool TryGetBinaryOperator(
        JsTokenKind kind,
        bool allowIn,
        out JsBinaryOperator op,
        out int precedence,
        out bool rightAssociative
    )
    {
        (op, precedence, rightAssociative) = kind switch
        {
            JsTokenKind.OrOr => (JsBinaryOperator.LogicalOr, 1, false),
            JsTokenKind.NullishCoalescing => (JsBinaryOperator.NullishCoalescing, 1, false),
            JsTokenKind.AndAnd => (JsBinaryOperator.LogicalAnd, 2, false),
            JsTokenKind.Pipe => (JsBinaryOperator.BitwiseOr, 3, false),
            JsTokenKind.Caret => (JsBinaryOperator.BitwiseXor, 4, false),
            JsTokenKind.Ampersand => (JsBinaryOperator.BitwiseAnd, 5, false),
            JsTokenKind.Eq => (JsBinaryOperator.Equal, 6, false),
            JsTokenKind.Neq => (JsBinaryOperator.NotEqual, 6, false),
            JsTokenKind.StrictEq => (JsBinaryOperator.StrictEqual, 6, false),
            JsTokenKind.StrictNeq => (JsBinaryOperator.StrictNotEqual, 6, false),
            JsTokenKind.Lt => (JsBinaryOperator.LessThan, 7, false),
            JsTokenKind.Lte => (JsBinaryOperator.LessThanOrEqual, 7, false),
            JsTokenKind.Gt => (JsBinaryOperator.GreaterThan, 7, false),
            JsTokenKind.Gte => (JsBinaryOperator.GreaterThanOrEqual, 7, false),
            JsTokenKind.In when allowIn => (JsBinaryOperator.In, 7, false),
            JsTokenKind.Instanceof => (JsBinaryOperator.Instanceof, 7, false),
            JsTokenKind.Shl => (JsBinaryOperator.ShiftLeft, 8, false),
            JsTokenKind.Sar => (JsBinaryOperator.ShiftRight, 8, false),
            JsTokenKind.Shr => (JsBinaryOperator.ShiftRightLogical, 8, false),
            JsTokenKind.Plus => (JsBinaryOperator.Add, 9, false),
            JsTokenKind.Minus => (JsBinaryOperator.Subtract, 9, false),
            JsTokenKind.Star => (JsBinaryOperator.Multiply, 10, false),
            JsTokenKind.Slash => (JsBinaryOperator.Divide, 10, false),
            JsTokenKind.Percent => (JsBinaryOperator.Modulo, 10, false),
            JsTokenKind.Pow => (JsBinaryOperator.Exponentiate, 11, true),
            _ => (default, 0, false),
        };
        return precedence != 0;
    }

    private ref struct NodeList
    {
        private Span<int> buffer;
        private int[]? rented;

        public NodeList(Span<int> initialBuffer)
        {
            buffer = initialBuffer;
        }

        public int Count { get; private set; }

        public void Add(int node)
        {
            if (Count == buffer.Length)
                Grow();
            buffer[Count++] = node;
        }

        public ReadOnlySpan<int> AsSpan() => buffer[..Count];

        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
            rented = null;
            buffer = [];
            Count = 0;
        }

        private void Grow()
        {
            var next = ArrayPool<int>.Shared.Rent(Math.Max(8, buffer.Length * 2));
            buffer.CopyTo(next);
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
            rented = next;
            buffer = next;
        }
    }
}
