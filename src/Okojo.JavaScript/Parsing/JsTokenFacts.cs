namespace Okojo.JavaScript.Parsing;

internal static class JsTokenFacts
{
    public static bool IsIdentifierName(JsTokenKind kind) =>
        kind
            is JsTokenKind.Identifier
                or JsTokenKind.True
                or JsTokenKind.False
                or JsTokenKind.Null
                or JsTokenKind.Undefined
                or JsTokenKind.NaN
                or JsTokenKind.Infinity
                or JsTokenKind.Var
                or JsTokenKind.Let
                or JsTokenKind.Const
                or JsTokenKind.If
                or JsTokenKind.Else
                or JsTokenKind.Return
                or JsTokenKind.Function
                or JsTokenKind.For
                or JsTokenKind.While
                or JsTokenKind.Do
                or JsTokenKind.Break
                or JsTokenKind.Continue
                or JsTokenKind.Debugger
                or JsTokenKind.Typeof
                or JsTokenKind.Void
                or JsTokenKind.Delete
                or JsTokenKind.Switch
                or JsTokenKind.Case
                or JsTokenKind.Default
                or JsTokenKind.Throw
                or JsTokenKind.Try
                or JsTokenKind.Catch
                or JsTokenKind.Finally
                or JsTokenKind.With
                or JsTokenKind.In
                or JsTokenKind.Instanceof
                or JsTokenKind.Of
                or JsTokenKind.New
                or JsTokenKind.This
                or JsTokenKind.ReservedWord;
}
