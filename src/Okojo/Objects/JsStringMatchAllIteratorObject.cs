namespace Okojo.Objects;

internal sealed class JsStringMatchAllIteratorObject : JsObject
{
    private readonly bool global;
    private readonly string input;
    private readonly JsObject regex;
    private readonly bool unicode;
    private bool done;

    internal JsStringMatchAllIteratorObject(JsRealm realm, string input, JsObject regex, bool global, bool unicode) :
        base(realm)
    {
        this.input = input;
        this.regex = regex;
        this.global = global;
        this.unicode = unicode;
        Prototype = realm.RegExpStringIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (done)
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));

        JsValue matchValue;
        if (regex.TryGetPropertyAtom(Realm, IdExec, out var execValue, out _) &&
            execValue.TryGetObject(out var execObj) && execObj is JsFunction execFn)
        {
            matchValue = Realm.InvokeFunction(execFn, JsValue.FromObject(regex), [JsValue.FromString(input)]);
            if (!matchValue.IsNull && !matchValue.TryGetObject(out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "RegExp exec method must return an object or null");
        }
        else
        {
            if (regex is not JsRegExpObject regexObj)
                throw new JsRuntimeException(JsErrorKind.TypeError, "RegExp exec method must be callable");

            matchValue = JsRegExpRuntime.Exec(Realm, regexObj, input);
            if (regexObj.Global || regexObj.Sticky)
                SetLastIndex(regexObj.GetNamedSlotUnchecked(JsRealm.RegExpOwnLastIndexSlot));
        }

        if (matchValue.IsNull)
        {
            done = true;
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));
        }

        if (!matchValue.TryGetObject(out var matchObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "RegExp match result must be an object");

        if (!global)
            done = true;

        var matched = matchObj.TryGetProperty("0", out var matchedValue)
            ? Realm.ToJsStringSlowPath(matchedValue)
            : string.Empty;
        if (global && matched.Length == 0)
        {
            var lastIndex = regex.TryGetPropertyAtom(Realm, IdLastIndex, out var lastIndexValue, out _)
                ? ToLength(lastIndexValue)
                : 0;
            SetLastIndex(AdvanceStringIndex(lastIndex));
        }

        return JsValue.FromObject(Realm.CreateIteratorResultObject(matchValue, false));
    }

    private int AdvanceStringIndex(int index)
    {
        if (!unicode || index >= input.Length)
            return index + 1;

        if (index + 1 < input.Length &&
            char.IsHighSurrogate(input[index]) &&
            char.IsLowSurrogate(input[index + 1]))
            return index + 2;

        return index + 1;
    }

    private int ToLength(JsValue value)
    {
        var number = Realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || number <= 0d)
            return 0;

        const double maxSafeInteger = 9007199254740991d;
        var clamped = double.IsPositiveInfinity(number)
            ? maxSafeInteger
            : Math.Min(maxSafeInteger, Math.Floor(number));
        return clamped >= int.MaxValue ? int.MaxValue : (int)clamped;
    }

    private void SetLastIndex(int value)
    {
        SetLastIndex(JsValue.FromInt32(value));
    }

    private void SetLastIndex(in JsValue value)
    {
        if (!regex.TrySetPropertyAtom(Realm, IdLastIndex, value, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "RegExp.prototype.[Symbol.matchAll] failed to set required property");
    }
}
