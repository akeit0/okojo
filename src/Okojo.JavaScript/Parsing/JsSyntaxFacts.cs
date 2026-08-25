namespace Okojo.JavaScript.Parsing;

public sealed class JsParseException : Exception
{
    public JsParseException(string message, int position, string? source = null)
        : base(FormatMessage(message, position, source, out var line, out var column))
    {
        Position = position;
        Line = line;
        Column = column;
    }

    public int Position { get; }
    public int Line { get; }
    public int Column { get; }

    private static string FormatMessage(
        string message,
        int position,
        string? source,
        out int line,
        out int column
    )
    {
        if (string.IsNullOrEmpty(source))
        {
            line = 0;
            column = 0;
            return $"{message} at position {position}.";
        }

        (line, column) = SourceLocation.GetLineColumn(source, position);
        return $"{message} at line {line}, column {column} (position {position}).";
    }
}

public enum JsVariableDeclarationKind
{
    Var,
    Let,
    Const,
    Using,
    AwaitUsing,
}

public static class JsVariableDeclarationKindExtensions
{
    public static bool IsLexical(this JsVariableDeclarationKind kind) =>
        kind is not JsVariableDeclarationKind.Var;

    public static bool IsConstLike(this JsVariableDeclarationKind kind) =>
        kind
            is JsVariableDeclarationKind.Const
                or JsVariableDeclarationKind.Using
                or JsVariableDeclarationKind.AwaitUsing;

    public static bool IsUsingLike(this JsVariableDeclarationKind kind) =>
        kind is JsVariableDeclarationKind.Using or JsVariableDeclarationKind.AwaitUsing;
}

public enum JsUnaryOperator
{
    Plus,
    Minus,
    LogicalNot,
    BitwiseNot,
    Typeof,
    Void,
    Delete,
}

public enum JsBinaryOperator
{
    LogicalAnd,
    LogicalOr,
    NullishCoalescing,
    BitwiseOr,
    BitwiseXor,
    BitwiseAnd,
    Equal,
    NotEqual,
    StrictEqual,
    StrictNotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
    Instanceof,
    ShiftLeft,
    ShiftRight,
    ShiftRightLogical,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Exponentiate,
}

public enum JsAssignmentOperator
{
    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    ExponentiateAssign,
    DivideAssign,
    ModuloAssign,
    ShiftLeftAssign,
    ShiftRightAssign,
    ShiftRightLogicalAssign,
    BitwiseAndAssign,
    BitwiseOrAssign,
    BitwiseXorAssign,
    LogicalAndAssign,
    LogicalOrAssign,
    NullishCoalescingAssign,
}

public enum JsUpdateOperator
{
    Increment,
    Decrement,
}

public enum JsClassElementKind
{
    Constructor,
    Method,
    Getter,
    Setter,
    Field,
    StaticBlock,
}
