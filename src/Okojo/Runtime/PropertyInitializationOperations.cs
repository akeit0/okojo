namespace Okojo.Runtime;

internal static class PropertyInitializationOperations
{
    internal static void DefineOwnDataPropertyByKey(JsRealm realm, JsObject target, in JsValue key,
        in JsValue value)
    {
        var normalizedKey = JsRealm.NormalizePropertyKey(realm, key);

        if (normalizedKey.IsSymbol)
        {
            JsRealm.AssignFunctionNameFromResolvedPropertyKey(realm, value,
                normalizedKey.AsSymbol().Description is { Length: > 0 } description
                    ? $"[{description}]"
                    : string.Empty);
            _ = target.DefineOwnDataPropertyExact(realm, normalizedKey.AsSymbol().Atom, value,
                JsShapePropertyFlags.Open);
            return;
        }

        if (normalizedKey.IsString)
        {
            var text = normalizedKey.AsString();
            JsRealm.AssignFunctionNameFromResolvedPropertyKey(realm, value, text);
            if (TryGetArrayIndexFromCanonicalString(text, out var index))
            {
                target.DefineElementDescriptor(index, PropertyDescriptor.OpenData(value));
                return;
            }

            _ = target.DefineOwnDataPropertyExact(realm, realm.Atoms.InternNoCheck(text), value,
                JsShapePropertyFlags.Open);
            return;
        }

        if (normalizedKey.IsNumber)
        {
            var numberText = JsValue.NumberToJsString(normalizedKey.NumberValue);
            JsRealm.AssignFunctionNameFromResolvedPropertyKey(realm, value, numberText);
            if (TryGetArrayIndexFromNumber(normalizedKey.NumberValue, out var index))
            {
                target.DefineElementDescriptor(index, PropertyDescriptor.OpenData(value));
                return;
            }

            _ = target.DefineOwnDataPropertyExact(realm,
                realm.Atoms.InternNoCheck(numberText),
                value,
                JsShapePropertyFlags.Open);
            return;
        }

        var fallbackText = realm.ToJsStringSlowPath(normalizedKey);
        JsRealm.AssignFunctionNameFromResolvedPropertyKey(realm, value, fallbackText);
        if (TryGetArrayIndexFromCanonicalString(fallbackText, out var fallbackIndex))
        {
            target.DefineElementDescriptor(fallbackIndex, PropertyDescriptor.OpenData(value));
            return;
        }

        _ = target.DefineOwnDataPropertyExact(realm, realm.Atoms.InternNoCheck(fallbackText), value,
            JsShapePropertyFlags.Open);
    }

    private static bool TryGetArrayIndexFromNumber(double n, out uint index)
    {
        index = 0;
        if (double.IsNaN(n) || double.IsInfinity(n))
            return false;

        var candidate = (uint)n;
        if (candidate == n && candidate != uint.MaxValue)
        {
            index = candidate;
            return true;
        }

        return false;
    }
}
