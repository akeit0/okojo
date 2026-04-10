namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    internal static JsValue StepIteratorForIterable(
        JsRealm realm,
        JsObject iterator,
        string nextNotFunctionMessage,
        string resultNotObjectMessage,
        out bool done)
    {
        if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextMethod, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, nextNotFunctionMessage);
        if (!nextMethod.TryGetObject(out var nextMethodObj) || nextMethodObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, nextNotFunctionMessage);

        var stepResult = realm.InvokeFunction(nextFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
        if (!stepResult.TryGetObject(out var resultObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, resultNotObjectMessage);

        _ = resultObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
        done = ToBoolean(doneValue);
        if (done)
            return JsValue.Undefined;

        return resultObj.TryGetPropertyAtom(realm, IdValue, out var value, out _)
            ? value
            : JsValue.Undefined;
    }
}
