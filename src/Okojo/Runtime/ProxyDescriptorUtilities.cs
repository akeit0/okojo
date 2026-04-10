namespace Okojo.Runtime;

internal static class ProxyDescriptorUtilities
{
    internal static bool HasPropertyOnTarget(JsRealm realm, JsObject target, in JsValue key)
    {
        var current = target;
        while (current is not null)
        {
            if (current.TryHasPropertyViaTrap(realm, key, out var viaTrap))
                return viaTrap;
            if (current.HasOwnPropertyKey(realm, key))
                return true;
            current = current.GetPrototypeOf(realm);
        }

        return false;
    }

    internal static JsValue GetOwnPropertyDescriptorValue(JsRealm realm, JsObject target, in JsValue key)
    {
        if (target.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var trapDescriptor))
            return trapDescriptor;

        if (TryGetOwnPropertyDescriptor(realm, target, key, out var descriptor))
            return CreateDescriptorObject(realm, descriptor);

        return JsValue.Undefined;
    }

    internal static DescriptorRequest ReadDescriptorRequest(JsRealm realm, JsObject descriptorObject)
    {
        var hasValue = descriptorObject.TryGetPropertyAtom(realm, IdValue, out var value, out _);
        var hasWritable = descriptorObject.TryGetPropertyAtom(realm, IdWritable, out var writableValue, out _);
        var hasEnumerable = descriptorObject.TryGetPropertyAtom(realm, IdEnumerable, out var enumerableValue, out _);
        var hasConfigurable =
            descriptorObject.TryGetPropertyAtom(realm, IdConfigurable, out var configurableValue, out _);
        var hasGet = descriptorObject.TryGetPropertyAtom(realm, IdGet, out var getValue, out _);
        var hasSet = descriptorObject.TryGetPropertyAtom(realm, IdSet, out var setValue, out _);

        if ((hasGet || hasSet) && (hasValue || hasWritable))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");

        return new(
            hasValue,
            value,
            hasWritable,
            hasWritable && DescriptorUtilities.ToBooleanForDescriptor(writableValue),
            hasEnumerable,
            hasEnumerable && DescriptorUtilities.ToBooleanForDescriptor(enumerableValue),
            hasConfigurable,
            hasConfigurable && DescriptorUtilities.ToBooleanForDescriptor(configurableValue),
            hasGet,
            getValue,
            hasSet,
            setValue);
    }

    internal static JsPlainObject CreateCanonicalDescriptorObject(JsRealm realm, JsObject descriptorObject)
    {
        var request = ReadDescriptorRequest(realm, descriptorObject);

        if (request.HasGet && !request.GetValue.IsUndefined &&
            (!request.GetValue.TryGetObject(out var getObject) || getObject is not JsFunction))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Getter must be a function or undefined");

        if (request.HasSet && !request.SetValue.IsUndefined &&
            (!request.SetValue.TryGetObject(out var setObject) || setObject is not JsFunction))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Setter must be a function or undefined");

        var canonical = new JsPlainObject(realm);
        if (request.HasValue)
            canonical.DefineDataPropertyAtom(realm, IdValue, request.Value, JsShapePropertyFlags.Open);
        if (request.HasWritable)
            canonical.DefineDataPropertyAtom(realm, IdWritable, request.Writable ? JsValue.True : JsValue.False,
                JsShapePropertyFlags.Open);
        if (request.HasEnumerable)
            canonical.DefineDataPropertyAtom(realm, IdEnumerable,
                request.Enumerable ? JsValue.True : JsValue.False, JsShapePropertyFlags.Open);
        if (request.HasConfigurable)
            canonical.DefineDataPropertyAtom(realm, IdConfigurable,
                request.Configurable ? JsValue.True : JsValue.False, JsShapePropertyFlags.Open);
        if (request.HasGet)
            canonical.DefineDataPropertyAtom(realm, IdGet, request.GetValue, JsShapePropertyFlags.Open);
        if (request.HasSet)
            canonical.DefineDataPropertyAtom(realm, IdSet, request.SetValue, JsShapePropertyFlags.Open);
        return canonical;
    }

    internal static bool IsCompatibleDefineRequest(in DescriptorRequest request, in PropertyDescriptor targetDescriptor)
    {
        if (targetDescriptor.Configurable)
            return true;

        if (request.HasConfigurable && request.Configurable)
            return false;
        if (request.HasEnumerable && request.Enumerable != targetDescriptor.Enumerable)
            return false;

        if (request.IsAccessorDescriptor)
        {
            if (!targetDescriptor.IsAccessor)
                return false;
            if (request.HasGet && !SameAccessorValue(targetDescriptor.Getter, request.GetValue))
                return false;
            if (request.HasSet && !SameAccessorValue(targetDescriptor.Setter, request.SetValue))
                return false;
            return true;
        }

        if (!request.IsDataDescriptor)
            return true;

        if (targetDescriptor.IsAccessor)
            return false;
        if (!targetDescriptor.Writable)
        {
            if (request.HasWritable && request.Writable)
                return false;
            if (request.HasValue && !JsValue.SameValue(targetDescriptor.Value, request.Value))
                return false;
        }

        return true;
    }

    internal static bool SameAccessorValue(JsFunction? current, in JsValue requested)
    {
        if (requested.IsUndefined)
            return current is null;
        return requested.TryGetObject(out var requestedObject) && ReferenceEquals(current, requestedObject);
    }


    internal static bool TryDefineOwnDataPropertyForSet(JsRealm realm, JsObject target, in JsValue key,
        in JsValue value,
        bool hasDescriptor)
    {
        if (hasDescriptor)
        {
            if (!TryGetOwnPropertyDescriptor(realm, target, key, out var existingDescriptor))
                return false;
            if (existingDescriptor.IsAccessor || !existingDescriptor.Writable)
                return false;

            if (realm.TryResolvePropertyKey(key, out var existingIndex, out var existingAtom))
                return target.SetElementWithReceiver(realm, target, existingIndex, value);

            return target.SetPropertyAtomWithReceiver(realm, target, existingAtom, value, out _);
        }

        if (realm.TryResolvePropertyKey(key, out var index, out var atom))
        {
            if (!target.IsExtensible)
                return false;
            target.DefineElementDescriptor(index, PropertyDescriptor.OpenData(value));
            return true;
        }

        return target.DefineOwnDataPropertyExact(realm, atom, value, JsShapePropertyFlags.Open);
    }

    internal static bool TryGetOwnPropertyDescriptor(JsRealm realm, JsObject target, in JsValue key,
        out PropertyDescriptor descriptor)
    {
        if (key.IsSymbol)
        {
            var atom = key.AsSymbol().Atom;
            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor))
                return true;

            if (target is JsGlobalObject symbolGlobal &&
                symbolGlobal.TryGetOwnGlobalDescriptorAtom(atom, out descriptor))
                return true;

            descriptor = default;
            return false;
        }

        var text = key.IsString ? key.AsString() : key.ToString() ?? string.Empty;
        if (TryGetArrayIndexFromCanonicalString(text, out var index))
            return target.TryGetOwnElementDescriptor(index, out descriptor);

        var namedAtom = realm.Atoms.InternNoCheck(text);
        if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, namedAtom, out descriptor))
            return true;

        if (target is JsGlobalObject global && global.TryGetOwnGlobalDescriptorAtom(namedAtom, out descriptor))
            return true;

        descriptor = default;
        return false;
    }

    private static JsValue CreateDescriptorObject(JsRealm realm, in PropertyDescriptor descriptor)
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

    internal readonly struct DescriptorRequest
    {
        internal readonly bool HasValue;
        internal readonly JsValue Value;
        internal readonly bool HasWritable;
        internal readonly bool Writable;
        internal readonly bool HasEnumerable;
        internal readonly bool Enumerable;
        internal readonly bool HasConfigurable;
        internal readonly bool Configurable;
        internal readonly bool HasGet;
        internal readonly JsValue GetValue;
        internal readonly bool HasSet;
        internal readonly JsValue SetValue;

        internal DescriptorRequest(
            bool hasValue,
            in JsValue value,
            bool hasWritable,
            bool writable,
            bool hasEnumerable,
            bool enumerable,
            bool hasConfigurable,
            bool configurable,
            bool hasGet,
            in JsValue getValue,
            bool hasSet,
            in JsValue setValue)
        {
            HasValue = hasValue;
            Value = value;
            HasWritable = hasWritable;
            Writable = writable;
            HasEnumerable = hasEnumerable;
            Enumerable = enumerable;
            HasConfigurable = hasConfigurable;
            Configurable = configurable;
            HasGet = hasGet;
            GetValue = getValue;
            HasSet = hasSet;
            SetValue = setValue;
        }

        internal bool IsAccessorDescriptor => HasGet || HasSet;
        internal bool IsDataDescriptor => HasValue || HasWritable;
    }
}
