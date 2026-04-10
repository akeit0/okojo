namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallObjectPrototypeBuiltins()
    {
        const int atomPropertyIsEnumerable = IdPropertyIsEnumerable;
        const int atomIsPrototypeOf = IdIsPrototypeOf;
        const int atomToLocaleString = IdToLocaleString;
        var hasOwnPropertyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.False;
            var key = JsRealm.NormalizePropertyKey(realm, args[0]);
            if (!realm.TryToObject(thisValue, out var obj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.prototype.hasOwnProperty called on null or undefined");
            if (obj is JsTypedArrayObject typedArray &&
                TryGetTypedArrayOwnPropertyDescriptorByKey(realm, typedArray, key, out var typedArrayDescriptor,
                    out var typedArrayHandled))
                return typedArrayHandled && !typedArrayDescriptor.IsUndefined ? JsValue.True : JsValue.False;

            return TryGetOwnPropertyEnumerable(realm, obj, key, out var hasOwn, out _) && hasOwn
                ? JsValue.True
                : JsValue.False;
        }, "hasOwnProperty", 1);
        var propertyIsEnumerableFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.False;
            var key = JsRealm.NormalizePropertyKey(realm, args[0]);
            if (!realm.TryToObject(thisValue, out var obj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.prototype.propertyIsEnumerable called on null or undefined");

            if (obj is JsTypedArrayObject typedArray &&
                TryGetTypedArrayOwnPropertyDescriptorByKey(realm, typedArray, key, out var typedArrayDescriptor,
                    out var typedArrayHandled))
            {
                if (!typedArrayHandled || typedArrayDescriptor.IsUndefined)
                    return JsValue.False;
                if (!typedArrayDescriptor.TryGetObject(out var typedArrayDescriptorObj))
                    return JsValue.False;
                if (!typedArrayDescriptorObj.TryGetPropertyAtom(realm, IdEnumerable, out var enumerableValue,
                        out _))
                    return JsValue.False;
                return new(JsRealm.ToBoolean(enumerableValue));
            }

            return TryGetOwnPropertyEnumerable(realm, obj, key, out var hasOwn, out var enumerable) && hasOwn &&
                   enumerable
                ? JsValue.True
                : JsValue.False;
        }, "propertyIsEnumerable", 1);
        var isPrototypeOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var valueObj))
                return JsValue.False;
            if (!realm.TryToObject(thisValue, out var thisObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.prototype.isPrototypeOf called on null or undefined");

            var cursor = valueObj.GetPrototypeOf(info.Realm);
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, thisObj))
                    return JsValue.True;
                cursor = cursor.GetPrototypeOf(info.Realm);
            }

            return JsValue.False;
        }, "isPrototypeOf", 1);
        var valueOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            if (!realm.TryToObject(thisValue, out var obj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.prototype.valueOf called on null or undefined");
            return obj;
        }, "valueOf", 0);
        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                if (thisValue.IsUndefined) return "[object Undefined]";
                if (thisValue.IsNull) return "[object Null]";
                var obj = thisValue.TryGetObject(out var objectValue) ? objectValue : realm.BoxPrimitive(thisValue);

                string builtinTag;
                if (IsArrayObjectForObjectToString(obj))
                    builtinTag = "Array";
                else
                    builtinTag = obj switch
                    {
                        JsArgumentsObject => "Arguments",
                        JsFunction => "Function",
                        JsRegExpObject => "RegExp",
                        JsDateObject => "Date",
                        JsStringObject => "String",
                        JsNumberObject => "Number",
                        JsBooleanObject => "Boolean",
                        _ when IsErrorLikeObject(obj, realm) => "Error",
                        _ => "Object"
                    };

                if (obj.TryGetPropertyAtom(realm, IdSymbolToStringTag, out var toStringTag, out _) &&
                    toStringTag.IsString)
                    return $"[object {toStringTag.AsString()}]";

                return $"[object {builtinTag}]";
            }, "toString", 0);
        ObjectPrototypeToStringIntrinsic = toStringFn;
        var toLocaleStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                if (!realm.TryToObject(thisValue, out var obj))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.prototype.toLocaleString called on null or undefined");

                if (!GetPropertyValuePreservingReceiver(realm, thisValue, obj, IdToString, "toString",
                        out var toStringValue) ||
                    !toStringValue.TryGetObject(out var toStringObj) ||
                    toStringObj is not JsFunction toStringFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.prototype.toLocaleString requires callable toString");

                return realm.InvokeFunction(toStringFn, thisValue, ReadOnlySpan<JsValue>.Empty);
            }, "toLocaleString", 0);

        static bool GetPropertyValuePreservingReceiver(JsRealm realm, in JsValue receiverValue,
            JsObject receiverObject,
            int atom, string name, out JsValue value)
        {
            for (var cursor = receiverObject; cursor is not null; cursor = cursor.Prototype)
            {
                if (!cursor.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
                    continue;

                if (!descriptor.IsAccessor)
                {
                    value = descriptor.Value;
                    return true;
                }

                if (descriptor.Getter is null)
                {
                    value = JsValue.Undefined;
                    return true;
                }

                value = realm.InvokeFunction(descriptor.Getter, receiverValue, ReadOnlySpan<JsValue>.Empty);
                return true;
            }

            return receiverObject.TryGetProperty(name, out value);
        }

        static bool TryGetOwnPropertyEnumerable(JsRealm realm, JsObject target, in JsValue key, out bool hasOwn,
            out bool enumerable)
        {
            if (target.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var trapDescriptor))
            {
                if (trapDescriptor.IsUndefined)
                {
                    hasOwn = false;
                    enumerable = false;
                    return true;
                }

                if (!trapDescriptor.TryGetObject(out var descriptorObject))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Proxy getOwnPropertyDescriptor trap result must be object or undefined");
                hasOwn = true;
                enumerable = descriptorObject.TryGetPropertyAtom(realm, IdEnumerable, out var enumerableValue, out _) &&
                             DescriptorUtilities.ToBooleanForDescriptor(enumerableValue);
                return true;
            }

            if (key.IsSymbol)
            {
                var atom = key.AsSymbol().Atom;
                if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var symbolDescriptor))
                {
                    hasOwn = true;
                    enumerable = symbolDescriptor.Enumerable;
                    return true;
                }

                if (target is JsGlobalObject symbolGlobal &&
                    symbolGlobal.TryGetOwnGlobalDescriptorAtom(atom, out var globalSymbolDescriptor))
                {
                    hasOwn = true;
                    enumerable = globalSymbolDescriptor.Enumerable;
                    return true;
                }

                hasOwn = false;
                enumerable = false;
                return true;
            }

            if (key.IsNumber)
            {
                var numericKey = key.NumberValue;
                if (numericKey == Math.Truncate(numericKey) && numericKey >= 0d && numericKey <= uint.MaxValue)
                {
                    var numericIndex = (uint)numericKey;
                    if (target.TryGetOwnElementDescriptor(numericIndex, out var numericDescriptor))
                    {
                        hasOwn = true;
                        enumerable = numericDescriptor.Enumerable;
                        return true;
                    }
                }

                var numericName = JsValue.NumberToJsString(key.NumberValue);
                if (realm.Atoms.TryGetInterned(numericName, out var numericAtom))
                    if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, numericAtom, out var numericNamedDescriptor))
                    {
                        hasOwn = true;
                        enumerable = numericNamedDescriptor.Enumerable;
                        return true;
                    }

                hasOwn = false;
                enumerable = false;
                return true;
            }

            var name = key.AsString();
            if (TryGetArrayIndexFromCanonicalString(name, out var index) &&
                target.TryGetOwnElementDescriptor(index, out var elementDescriptor))
            {
                hasOwn = true;
                enumerable = elementDescriptor.Enumerable;
                return true;
            }

            if (realm.Atoms.TryGetInterned(name, out var nameAtom))
            {
                if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, nameAtom, out var namedDescriptor))
                {
                    hasOwn = true;
                    enumerable = namedDescriptor.Enumerable;
                    return true;
                }

                if (target is JsGlobalObject global &&
                    global.TryGetOwnGlobalDescriptorAtom(nameAtom, out var globalDescriptor))
                {
                    hasOwn = true;
                    enumerable = globalDescriptor.Enumerable;
                    return true;
                }
            }

            hasOwn = false;
            enumerable = false;
            return true;
        }

        static bool IsArrayObjectForObjectToString(JsObject obj)
        {
            while (true)
            {
                if (obj is JsArray)
                    return true;

                if (obj.TryGetProxyTargetOrThrow(obj.Realm, out var target))
                {
                    obj = target;
                    continue;
                }

                return false;
            }
        }

        static bool IsErrorLikeObject(JsObject obj, JsRealm realm)
        {
            if (ReferenceEquals(obj, realm.ErrorPrototype) ||
                ReferenceEquals(obj, realm.TypeErrorPrototype) ||
                ReferenceEquals(obj, realm.ReferenceErrorPrototype) ||
                ReferenceEquals(obj, realm.RangeErrorPrototype) ||
                ReferenceEquals(obj, realm.SyntaxErrorPrototype) ||
                ReferenceEquals(obj, realm.EvalErrorPrototype) ||
                ReferenceEquals(obj, realm.UriErrorPrototype) ||
                ReferenceEquals(obj, realm.AggregateErrorPrototype))
                return false;

            var cursor = obj.Prototype;
            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, realm.ErrorPrototype))
                    return true;
                cursor = cursor.Prototype;
            }

            return false;
        }

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(ObjectConstructor)),
            PropertyDefinition.Mutable(IdHasOwnProperty, JsValue.FromObject(hasOwnPropertyFn)),
            PropertyDefinition.Mutable(atomPropertyIsEnumerable, JsValue.FromObject(propertyIsEnumerableFn)),
            PropertyDefinition.Mutable(atomIsPrototypeOf, JsValue.FromObject(isPrototypeOfFn)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(atomToLocaleString, JsValue.FromObject(toLocaleStringFn))
        ];
        ObjectPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }
}
