namespace Okojo.Runtime;

public partial class Intrinsics
{
    internal static bool TryGetCanonicalNumericIndexString(JsRealm realm, in JsValue key, out double numericIndex)
    {
        if (key.IsSymbol)
        {
            numericIndex = default;
            return false;
        }

        var text = key.IsString ? key.AsString() : realm.ToJsStringSlowPath(key);
        if (text == "-0")
        {
            numericIndex = -0d;
            return true;
        }

        var number = realm.ToNumberSlowPath(JsValue.FromString(text));
        if (double.IsNaN(number))
        {
            if (text == "NaN")
            {
                numericIndex = number;
                return true;
            }

            numericIndex = default;
            return false;
        }

        var roundTrip = realm.ToJsStringSlowPath(new(number));
        if (string.Equals(roundTrip, text, StringComparison.Ordinal))
        {
            numericIndex = number;
            return true;
        }

        numericIndex = default;
        return false;
    }

    private static bool IsNegativeZero(double value)
    {
        return value == 0d && double.IsNegativeInfinity(1d / value);
    }

    internal static bool IsValidTypedArrayCanonicalNumericIndex(JsTypedArrayObject typedArray, double numericIndex)
    {
        if (typedArray.IsOutOfBounds)
            return false;
        if (double.IsNaN(numericIndex) || double.IsInfinity(numericIndex))
            return false;
        if (IsNegativeZero(numericIndex))
            return false;
        if (numericIndex < 0d || numericIndex != Math.Truncate(numericIndex) || numericIndex > uint.MaxValue)
            return false;
        return numericIndex < typedArray.Length;
    }

    internal static bool TryGetTypedArrayIntegerIndexedElement(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, out JsValue value, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            value = JsValue.Undefined;
            return false;
        }

        handled = true;
        if (!IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
        {
            value = JsValue.Undefined;
            return true;
        }

        value = typedArray.GetDirectElementValue((uint)numericIndex);
        return true;
    }

    internal static bool TryHasTypedArrayIntegerIndexedElement(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, out bool hasProperty, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            hasProperty = false;
            return false;
        }

        handled = true;
        hasProperty = IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex);
        return true;
    }

    internal static bool TryDeleteTypedArrayIntegerIndexedElement(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, out bool deleted, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            deleted = false;
            return false;
        }

        handled = true;
        deleted = typedArray.Buffer.IsDetached || !IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex);
        return true;
    }

    internal static bool TrySetTypedArrayIntegerIndexedWithReceiver(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, in JsValue value, in JsValue receiverValue, out bool result, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            result = false;
            return false;
        }

        handled = true;
        if (ReferenceEquals(receiverValue.TryGetObject(out var earlyReceiverObj) ? earlyReceiverObj : null, typedArray))
        {
            result = SetCanonicalNumericIndexOnTypedArrayForSet(typedArray, numericIndex, value);
            return true;
        }

        if (!IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
        {
            result = true;
            return true;
        }

        if (!receiverValue.TryGetObject(out var receiverObj))
        {
            result = false;
            return true;
        }

        var index = (uint)numericIndex;
        if (receiverObj is JsTypedArrayObject receiverTypedArray)
        {
            if (!IsValidTypedArrayCanonicalNumericIndex(receiverTypedArray, numericIndex))
            {
                result = false;
                return true;
            }

            receiverTypedArray.SetValidatedIntegerIndexedValue(index, value);
            result = true;
            return true;
        }

        result = OrdinarySetOwnWritableDataIndex(realm, receiverObj, index, value);
        return true;
    }

    internal static bool SetCanonicalNumericIndexOnTypedArrayForSet(JsTypedArrayObject typedArray,
        double numericIndex,
        in JsValue value)
    {
        var normalized = TypedArrayElementKindInfo.NormalizeValue(typedArray.Realm, typedArray.Kind, value);
        if (typedArray.Buffer.IsDetached || !IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
            return true;

        typedArray.TrySetNormalizedElement((uint)numericIndex, normalized);
        return true;
    }

    internal static bool OrdinarySetOwnWritableDataIndex(JsRealm realm, JsObject receiver, uint index,
        in JsValue value)
    {
        if (receiver.TryDefineOwnDataPropertyForSet(realm, index, value, out _))
            return true;

        if (receiver.TryGetOwnElementDescriptor(index, out var existing))
        {
            if (existing.IsAccessor || !existing.Writable)
                return false;

            receiver.DefineElementDescriptor(index, new(value, null, existing.Flags));
            return true;
        }

        if (!receiver.IsExtensible)
            return false;

        receiver.DefineElementDescriptor(index, PropertyDescriptor.OpenData(value));
        return true;
    }

    private static bool TryDefineTypedArrayIntegerIndexedProperty(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, in DescriptorSnapshot spec, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            return false;
        }

        handled = true;
        if (!IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
            return false;
        if (spec.IsAccessorDescriptor)
            return false;

        if (spec.HasConfigurable &&
            !DescriptorUtilities.IsRequestedTrue(spec.HasConfigurable, spec.ConfigurableValue))
            return false;
        if (spec.HasEnumerable &&
            !DescriptorUtilities.IsRequestedTrue(spec.HasEnumerable, spec.EnumerableValue))
            return false;
        if (spec.HasWritable &&
            !DescriptorUtilities.IsRequestedTrue(spec.HasWritable, spec.WritableValue))
            return false;

        if (!spec.HasValue)
            return true;

        typedArray.SetValidatedIntegerIndexedValue((uint)numericIndex, spec.Value);
        return true;
    }

    private static bool TryGetTypedArrayOwnPropertyDescriptorByKey(JsRealm realm, JsTypedArrayObject typedArray,
        in JsValue key, out JsValue descriptor, out bool handled)
    {
        if (!TryGetCanonicalNumericIndexString(realm, key, out var numericIndex))
        {
            handled = false;
            descriptor = JsValue.Undefined;
            return false;
        }

        handled = true;
        if (!IsValidTypedArrayCanonicalNumericIndex(typedArray, numericIndex))
        {
            descriptor = JsValue.Undefined;
            return true;
        }

        var propertyDescriptor = PropertyDescriptor.Data(
            typedArray.GetDirectElementValue((uint)numericIndex),
            true,
            true,
            true);
        descriptor = CreateDescriptorObjectForTypedArray(realm, propertyDescriptor);
        return true;
    }

    private static JsValue CreateDescriptorObjectForTypedArray(JsRealm realm, in PropertyDescriptor descriptor)
    {
        var result = new JsPlainObject(realm);
        if (descriptor.IsAccessor)
        {
            result.DefineDataPropertyAtom(realm, IdGet,
                descriptor.Getter ?? JsValue.Undefined, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(realm, IdSet,
                descriptor.Setter ?? JsValue.Undefined, JsShapePropertyFlags.Open);
        }
        else
        {
            result.DefineDataPropertyAtom(realm, IdValue, descriptor.Value, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(realm, IdWritable, new(descriptor.Writable),
                JsShapePropertyFlags.Open);
        }

        result.DefineDataPropertyAtom(realm, IdEnumerable, new(descriptor.Enumerable),
            JsShapePropertyFlags.Open);
        result.DefineDataPropertyAtom(realm, IdConfigurable, new(descriptor.Configurable),
            JsShapePropertyFlags.Open);
        return result;
    }
}
