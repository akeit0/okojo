namespace Okojo.JavaScript.Parsing;

internal static class JsOperatorTable
{
    public static bool TryGetAssignment(
        JsTokenKind kind,
        out JsAssignmentOperator assignmentOperator
    )
    {
        assignmentOperator = kind switch
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

    public static bool TryGetUnary(JsTokenKind kind, out JsUnaryOperator unaryOperator)
    {
        unaryOperator = kind switch
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

    public static bool TryGetUpdate(JsTokenKind kind, out JsUpdateOperator updateOperator)
    {
        updateOperator = kind switch
        {
            JsTokenKind.PlusPlus => JsUpdateOperator.Increment,
            JsTokenKind.MinusMinus => JsUpdateOperator.Decrement,
            _ => default,
        };
        return kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus;
    }

    public static bool TryGetBinary(JsTokenKind kind, bool allowIn, out BinaryOperatorInfo info)
    {
        info = kind switch
        {
            JsTokenKind.OrOr => new(JsBinaryOperator.LogicalOr, 1, true),
            JsTokenKind.AndAnd => new(JsBinaryOperator.LogicalAnd, 2, true),
            JsTokenKind.Pipe => new(JsBinaryOperator.BitwiseOr, 3),
            JsTokenKind.Caret => new(JsBinaryOperator.BitwiseXor, 4),
            JsTokenKind.Ampersand => new(JsBinaryOperator.BitwiseAnd, 5),
            JsTokenKind.Eq => new(JsBinaryOperator.Equal, 6),
            JsTokenKind.Neq => new(JsBinaryOperator.NotEqual, 6),
            JsTokenKind.StrictEq => new(JsBinaryOperator.StrictEqual, 6),
            JsTokenKind.StrictNeq => new(JsBinaryOperator.StrictNotEqual, 6),
            JsTokenKind.Lt => new(JsBinaryOperator.LessThan, 7),
            JsTokenKind.Lte => new(JsBinaryOperator.LessThanOrEqual, 7),
            JsTokenKind.Gt => new(JsBinaryOperator.GreaterThan, 7),
            JsTokenKind.Gte => new(JsBinaryOperator.GreaterThanOrEqual, 7),
            JsTokenKind.In when allowIn => new(JsBinaryOperator.In, 7),
            JsTokenKind.Instanceof => new(JsBinaryOperator.Instanceof, 7),
            JsTokenKind.Shl => new(JsBinaryOperator.ShiftLeft, 8),
            JsTokenKind.Sar => new(JsBinaryOperator.ShiftRight, 8),
            JsTokenKind.Shr => new(JsBinaryOperator.ShiftRightLogical, 8),
            JsTokenKind.Plus => new(JsBinaryOperator.Add, 9),
            JsTokenKind.Minus => new(JsBinaryOperator.Subtract, 9),
            JsTokenKind.Star => new(JsBinaryOperator.Multiply, 10),
            JsTokenKind.Slash => new(JsBinaryOperator.Divide, 10),
            JsTokenKind.Percent => new(JsBinaryOperator.Modulo, 10),
            JsTokenKind.Pow => new(JsBinaryOperator.Exponentiate, 11, IsRightAssociative: true),
            _ => default,
        };
        return info.Precedence != 0;
    }

    internal readonly record struct BinaryOperatorInfo(
        JsBinaryOperator Operator,
        int Precedence,
        bool IsLogicalAndOr = false,
        bool IsRightAssociative = false
    );
}
