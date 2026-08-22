using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

internal static class RealmEvaluationTestExtensions
{
    public static JsValue EvaluateInFunctionScope(this JsRealm realm, string functionBody)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(functionBody);
        return realm.Evaluate(
            $$"""
            function __okojo_test_scope__() {
            {{functionBody}}
            }
            __okojo_test_scope__();
            """
        );
    }

    public static ValueTask<T> EvaluateInAsyncFunctionScope<T>(
        this JsRealm realm,
        string functionBody,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(functionBody);
        return realm.ToPumpedValueTask<T>(
            realm.Evaluate(
                $$"""
                async function __okojo_test_async_scope__() {
                {{functionBody}}
                }
                __okojo_test_async_scope__();
                """
            ),
            cancellationToken
        );
    }
}
