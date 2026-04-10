using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallReflectBuiltins()
    {
        // Installed via CreateReflectObject() in InstallIntrinsics.
    }

    private JsPlainObject CreateReflectObject()
    {
        var reflect = new JsPlainObject(Realm, false)
        {
            Prototype = ObjectPrototype
        };

        var applyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 3)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.apply requires target, thisArgument and argumentsList");
            if (!args[0].TryGetObject(out var targetObj) || targetObj is not JsFunction targetFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.apply target must be callable");

            return realm.InvokeFunctionWithArrayLikeArguments(targetFn, args[1], args[2], 0);
        }, "apply", 3);

        var constructFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.construct requires target and argumentsList");
            if (!args[0].TryGetObject(out var targetObj) || targetObj is not JsFunction targetFn ||
                !targetFn.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.construct target must be a constructor");

            var newTargetFn = targetFn;
            if (args.Length >= 3 && !args[2].IsUndefined)
            {
                if (!args[2].TryGetObject(out var newTargetObj) ||
                    newTargetObj is not JsFunction newTargetCandidate ||
                    !newTargetCandidate.IsConstructor)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Reflect.construct newTarget must be a constructor");
                newTargetFn = newTargetCandidate;
            }

            return realm.ConstructWithArrayLikeArguments(targetFn, args[1], newTargetFn, 0);
        }, "construct", 2);

        var definePropertyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !args[0].TryGetObject(out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.defineProperty target must be object");

            if (args.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.defineProperty requires propertyKey");

            var normalizedKey = JsRealm.NormalizePropertyKey(realm, args[1]);

            if (args.Length < 3)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Property description must be an object");

            var defineArgs = new InlineJsValueArray3
            {
                Item0 = args[0],
                Item1 = normalizedKey,
                Item2 = args[2]
            };

            if (args[0].TryGetObject(out var targetObj) && args[2].TryGetObject(out var descriptorObj))
            {
                var canonicalDescriptor =
                    ProxyDescriptorUtilities.CreateCanonicalDescriptorObject(realm, descriptorObj);
                if (targetObj.TryDefinePropertyFromDescriptorObject(realm, normalizedKey, canonicalDescriptor,
                        out var proxyResult)) return proxyResult ? JsValue.True : JsValue.False;
            }

            try
            {
                _ = realm.InvokeObjectConstructorMethod("defineProperty", defineArgs.AsSpan());
                return JsValue.True;
            }
            catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.TypeError)
            {
                return JsValue.False;
            }
        }, "defineProperty", 3);

        var deletePropertyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.deleteProperty target must be object");

            var normalizedKey = JsRealm.NormalizePropertyKey(realm, args[1]);

            if (target is JsTypedArrayObject typedArray &&
                TryDeleteTypedArrayIntegerIndexedElement(realm, typedArray, normalizedKey, out var typedArrayDeleted,
                    out var typedArrayHandled))
                if (typedArrayHandled)
                    return typedArrayDeleted ? JsValue.True : JsValue.False;

            var isIndex = realm.TryResolvePropertyKey(normalizedKey, out var index, out var atom);
            var ok = isIndex ? target.DeleteElement(index) : target.DeletePropertyAtom(realm, atom);
            return ok ? JsValue.True : JsValue.False;
        }, "deleteProperty", 2);

        var getFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.get target must be object");

            var normalizedKey = JsRealm.NormalizePropertyKey(realm, args[1]);
            var receiverValue = args.Length >= 3 ? args[2] : args[0];
            if (target is JsTypedArrayObject typedArray &&
                TryGetTypedArrayIntegerIndexedElement(realm, typedArray, normalizedKey, out var typedArrayValue,
                    out var typedArrayHandled))
                if (typedArrayHandled)
                    return typedArrayValue;

            var isIndex = realm.TryResolvePropertyKey(normalizedKey, out var index, out var atom);
            if (isIndex)
            {
                _ = target.TryGetElement(index, out var value);
                return value;
            }

            if (receiverValue.TryGetObject(out var receiverObj) &&
                target.TryGetPropertyAtomWithReceiver(realm, receiverObj, atom, out var withReceiver, out _))
                return withReceiver;

            _ = target.TryGetPropertyAtom(realm, atom, out var withoutReceiver, out _);
            return withoutReceiver;
        }, "get", 2);

        var getOwnPropertyDescriptorFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out _))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.getOwnPropertyDescriptor target must be object");

            var descriptorArgs = new InlineJsValueArray2
            {
                Item0 = args[0],
                Item1 = args[1]
            };
            return realm.InvokeObjectConstructorMethod("getOwnPropertyDescriptor", descriptorArgs.AsSpan());
        }, "getOwnPropertyDescriptor", 2);

        var getPrototypeOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length < 1 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.getPrototypeOf target must be object");
            return target.GetPrototypeOf(info.Realm) is { } prototype ? prototype : JsValue.Null;
        }, "getPrototypeOf", 1);

        var hasFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.has target must be object");

            var has = JsRealm.HasPropertySlowPath(realm, target, JsRealm.NormalizePropertyKey(realm, args[1]));
            return has ? JsValue.True : JsValue.False;
        }, "has", 2);

        var isExtensibleFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.isExtensible target must be object");
            return ProxyCore.QueryIsExtensible(realm, target) ? JsValue.True : JsValue.False;
        }, "isExtensible", 1);

        var ownKeysFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.ownKeys target must be object");

            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            var outArr = realm.CreateArrayObject();
            for (uint i = 0; i < (uint)keys.Count; i++)
                FreshArrayOperations.DefineElement(outArr, i, keys[(int)i]);
            return outArr;
        }, "ownKeys", 1);

        var preventExtensionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.preventExtensions target must be object");
            try
            {
                ProxyCore.PreventExtensionsOnTarget(realm, target);
                return target is IProxyObject || !target.IsExtensible ? JsValue.True : JsValue.False;
            }
            catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.TypeError)
            {
                return JsValue.False;
            }
        }, "preventExtensions", 1);

        var setFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.set target must be object");

            var normalizedKey = JsRealm.NormalizePropertyKey(realm, args[1]);
            var value = args.Length >= 3 ? args[2] : JsValue.Undefined;
            var receiverValue = args.Length >= 4 ? args[3] : args[0];
            if (target is JsTypedArrayObject typedArray &&
                TrySetTypedArrayIntegerIndexedWithReceiver(
                    realm,
                    typedArray,
                    normalizedKey,
                    value,
                    receiverValue,
                    out var typedArraySetResult,
                    out var typedArraySetHandled))
                if (typedArraySetHandled)
                    return typedArraySetResult ? JsValue.True : JsValue.False;

            var isIndex = realm.TryResolvePropertyKey(normalizedKey, out var index, out var atom);

            if (isIndex)
            {
                if (!receiverValue.TryGetObject(out var primitiveCheckedReceiver))
                    return ReflectSetWithPrimitiveReceiver(realm, target, normalizedKey, value, receiverValue)
                        ? JsValue.True
                        : JsValue.False;

                if (!ReferenceEquals(target, primitiveCheckedReceiver) &&
                    ReceiverBlocksDataPropertyWrite(realm, primitiveCheckedReceiver, normalizedKey))
                    return JsValue.False;

                if (receiverValue.TryGetObject(out var receiverObj))
                {
                    if (target is JsTypedArrayObject typedArrayTarget && ReferenceEquals(target, receiverObj))
                        return typedArrayTarget.TrySetElement(index, value) ? JsValue.True : JsValue.False;
                    return target.SetElementWithReceiver(realm, receiverObj, index, value)
                        ? JsValue.True
                        : JsValue.False;
                }

                return target.TrySetElement(index, value) ? JsValue.True : JsValue.False;
            }

            if (!receiverValue.TryGetObject(out var receiverObject))
                return ReflectSetWithPrimitiveReceiver(realm, target, normalizedKey, value, receiverValue)
                    ? JsValue.True
                    : JsValue.False;

            if (!ReferenceEquals(target, receiverObject) &&
                ReceiverBlocksDataPropertyWrite(realm, receiverObject, normalizedKey))
                return JsValue.False;

            if (receiverValue.TryGetObject(out var receiver))
                return target.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out _)
                    ? JsValue.True
                    : JsValue.False;

            return target.TrySetPropertyAtom(realm, atom, value, out _) ? JsValue.True : JsValue.False;
        }, "set", 3);

        var setPrototypeOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Reflect.setPrototypeOf target must be object");

            JsObject? proto;
            if (args[1].IsNull)
                proto = null;
            else if (args[1].TryGetObject(out var protoObj))
                proto = protoObj;
            else
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Reflect.setPrototypeOf prototype must be object or null");

            return ProxyCore.SetPrototypeOnTarget(realm, target, proto) ? JsValue.True : JsValue.False;
        }, "setPrototypeOf", 2);

        const int atomApply = IdApply;
        const int atomConstruct = IdConstruct;
        const int atomDefineProperty = IdDefineProperty;
        const int atomDeleteProperty = IdDeleteProperty;
        const int atomGet = IdGet;
        const int atomGetOwnPropertyDescriptor = IdGetOwnPropertyDescriptor;
        const int atomGetPrototypeOf = IdGetPrototypeOf;
        const int atomHas = IdHas;
        const int atomIsExtensible = IdIsExtensible;
        const int atomOwnKeys = IdOwnKeys;
        const int atomPreventExtensions = IdPreventExtensions;
        const int atomSet = IdSet;
        const int atomSetPrototypeOf = IdSetPrototypeOf;

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(atomApply, applyFn),
            PropertyDefinition.Mutable(atomConstruct, constructFn),
            PropertyDefinition.Mutable(atomDefineProperty, definePropertyFn),
            PropertyDefinition.Mutable(atomDeleteProperty, deletePropertyFn),
            PropertyDefinition.Mutable(atomGet, getFn),
            PropertyDefinition.Mutable(atomGetOwnPropertyDescriptor, getOwnPropertyDescriptorFn),
            PropertyDefinition.Mutable(atomGetPrototypeOf, getPrototypeOfFn),
            PropertyDefinition.Mutable(atomHas, hasFn),
            PropertyDefinition.Mutable(atomIsExtensible, isExtensibleFn),
            PropertyDefinition.Mutable(atomOwnKeys, ownKeysFn),
            PropertyDefinition.Mutable(atomPreventExtensions, preventExtensionsFn),
            PropertyDefinition.Mutable(atomSet, setFn),
            PropertyDefinition.Mutable(atomSetPrototypeOf, setPrototypeOfFn),
            PropertyDefinition.Const(IdSymbolToStringTag, "Reflect", configurable: true)
        ];
        reflect.DefineNewPropertiesNoCollision(Realm, defs);
        return reflect;
    }

    private static bool ReflectSetWithPrimitiveReceiver(
        JsRealm realm,
        JsObject target,
        in JsValue normalizedKey,
        JsValue value,
        in JsValue receiverValue)
    {
        var kind = ResolveReflectSetTargetKind(realm, target, normalizedKey, out var setter);
        if (kind != ReflectSetTargetKind.AccessorWithSetter)
            return false;

        var args = new InlineJsValueArray1 { Item0 = value };
        _ = realm.InvokeFunction(setter!, receiverValue, args.AsSpan());
        return true;
    }

    private static bool ReceiverBlocksDataPropertyWrite(JsRealm realm, JsObject receiver, in JsValue normalizedKey)
    {
        if (realm.TryResolvePropertyKey(normalizedKey, out var index, out var atom))
        {
            if (!receiver.TryGetOwnElementDescriptor(index, out var elementDescriptor))
                return false;
            return elementDescriptor.IsAccessor || !elementDescriptor.Writable;
        }

        if (!receiver.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var namedDescriptor))
            return false;
        return namedDescriptor.IsAccessor || !namedDescriptor.Writable;
    }

    private static ReflectSetTargetKind ResolveReflectSetTargetKind(
        JsRealm realm,
        JsObject target,
        in JsValue normalizedKey,
        out JsFunction? setter)
    {
        setter = null;
        var isIndex = realm.TryResolvePropertyKey(normalizedKey, out var index, out var atom);
        for (var cursor = target; cursor is not null; cursor = cursor.Prototype)
        {
            if (isIndex)
            {
                if (!cursor.TryGetOwnElementDescriptor(index, out var elementDescriptor))
                    continue;

                if (elementDescriptor.IsAccessor)
                {
                    setter = elementDescriptor.Setter;
                    return setter is null
                        ? ReflectSetTargetKind.AccessorWithoutSetter
                        : ReflectSetTargetKind.AccessorWithSetter;
                }

                return elementDescriptor.Writable ? ReflectSetTargetKind.Data : ReflectSetTargetKind.ReadOnlyData;
            }

            if (!cursor.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var namedDescriptor))
                continue;

            if (namedDescriptor.IsAccessor)
            {
                setter = namedDescriptor.Setter;
                return setter is null
                    ? ReflectSetTargetKind.AccessorWithoutSetter
                    : ReflectSetTargetKind.AccessorWithSetter;
            }

            return namedDescriptor.Writable ? ReflectSetTargetKind.Data : ReflectSetTargetKind.ReadOnlyData;
        }

        return ReflectSetTargetKind.Missing;
    }


    private enum ReflectSetTargetKind
    {
        Missing,
        Data,
        ReadOnlyData,
        AccessorWithSetter,
        AccessorWithoutSetter
    }
}
