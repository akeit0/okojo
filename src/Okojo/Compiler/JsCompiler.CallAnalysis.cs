using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private static bool ArgumentsRequireStableCallState(IReadOnlyList<JsExpression> arguments)
    {
        foreach (var argument in arguments)
            if (ExpressionRequiresStableCallState(argument))
                return true;

        return false;
    }

    private static bool ExpressionRequiresStableCallState(JsExpression expr)
    {
        switch (expr)
        {
            case JsLiteralExpression:
            case JsIdentifierExpression:
            case JsThisExpression:
            case JsSuperExpression:
            case JsNewTargetExpression:
                return false;
            case JsArrayExpression array:
                foreach (var element in array.Elements)
                {
                    if (element is JsSpreadExpression)
                        return true;
                    if (element is not null && ExpressionRequiresStableCallState(element))
                        return true;
                }

                return false;
            case JsObjectExpression obj:
                foreach (var prop in obj.Properties)
                {
                    if (prop.IsComputed &&
                        prop.ComputedKey is not null &&
                        ExpressionRequiresStableCallState(prop.ComputedKey))
                        return true;

                    if (prop.Value is not null && ExpressionRequiresStableCallState(prop.Value))
                        return true;
                }

                return false;
            case JsSequenceExpression sequence:
                foreach (var child in sequence.Expressions)
                    if (ExpressionRequiresStableCallState(child))
                        return true;

                return false;
            case JsConditionalExpression conditional:
                return ExpressionRequiresStableCallState(conditional.Test) ||
                       ExpressionRequiresStableCallState(conditional.Consequent) ||
                       ExpressionRequiresStableCallState(conditional.Alternate);
            default:
                return true;
        }
    }

    private int AllocateCallStateRegister(string prefix, bool preserveAcrossArgumentEvaluation)
    {
        return AllocateTemporaryRegister();
    }

    private int PreserveRegisterForCallState(int sourceRegister, string prefix, bool preserveAcrossArgumentEvaluation)
    {
        if (!preserveAcrossArgumentEvaluation)
            return sourceRegister;

        var preservedRegister = AllocateCallStateRegister(prefix, preserveAcrossArgumentEvaluation);
        EmitMoveRegister(sourceRegister, preservedRegister);
        return preservedRegister;
    }
}
