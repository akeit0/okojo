namespace Okojo.Runtime;

internal static class JsIteratorHelperOperations
{
    internal static JsPlainObject CreateIteratorResultObject(JsRealm realm, JsValue value, bool done)
    {
        return realm.CreateIteratorResultObject(value, done);
    }

    internal static bool ToBoolean(in JsValue value)
    {
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsNullOrUndefined)
            return false;
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            return number != 0d && !double.IsNaN(number);
        }

        if (value.IsString)
            return value.AsString().Length != 0;
        if (value.IsBigInt)
            return !value.AsBigInt().Value.IsZero;
        return true;
    }
}
