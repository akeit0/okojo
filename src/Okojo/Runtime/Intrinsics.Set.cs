using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateSetConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Set constructor must be called with new");

            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.SetPrototype);
            var set = new JsSetObject(realm, prototype);
            if (args.Length == 0 || args[0].IsUndefined || args[0].IsNull)
                return set;

            if (!realm.TryToObject(args[0], out var iterable))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Set iterable must not be null or undefined");

            PopulateSetFromIterable(realm, set, iterable);
            return set;
        }, "Set", 0, true);
    }

    private void InstallSetConstructorBuiltins()
    {
        const int atomAdd = IdAdd;
        const int atomClear = IdClear;
        const int atomDelete = IdDelete;
        const int atomDifference = IdDifference;
        const int atomEntries = IdEntries;
        const int atomForEach = IdForEach;
        const int atomHas = IdHas;
        const int atomIntersection = IdIntersection;
        const int atomIsDisjointFrom = IdIsDisjointFrom;
        const int atomIsSubsetOf = IdIsSubsetOf;
        const int atomIsSupersetOf = IdIsSupersetOf;
        const int atomKeys = IdKeys;
        const int atomValues = IdValues;
        const int atomSize = IdSize;
        const int atomUnion = IdUnion;
        const int atomSymmetricDifference = IdSymmetricDifference;
        const int atomNextLocal = IdNext;

        var addFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            set.AddValue(args.Length != 0 ? args[0] : JsValue.Undefined);
            return thisValue;
        }, "add", 1);

        var clearFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            ThisSetValue(realm, thisValue).ClearEntries();
            return JsValue.Undefined;
        }, "clear", 0);

        var deleteFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            return set.DeleteValue(args.Length != 0 ? args[0] : JsValue.Undefined) ? JsValue.True : JsValue.False;
        }, "delete", 1);

        var hasFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            return set.HasValue(args.Length != 0 ? args[0] : JsValue.Undefined) ? JsValue.True : JsValue.False;
        }, "has", 1);

        var forEachFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Set.prototype.forEach callback must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            var cursor = 0;
            while (set.TryGetNextLiveValue(ref cursor, out var value))
            {
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = value,
                    Item2 = JsValue.FromObject(set)
                };
                realm.InvokeFunction(callbackFn, thisArg, callbackArgs.AsSpan());
            }

            return JsValue.Undefined;
        }, "forEach", 1);

        var sizeGetter = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return JsValue.FromInt32(ThisSetValue(realm, thisValue).Count);
            },
            "get size", 0);

        var valuesFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return new JsSetIteratorObject(realm, ThisSetValue(realm, thisValue),
                    JsSetIteratorObject.IterationKind.Values);
            }, "values", 0);

        var entriesFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return new JsSetIteratorObject(realm, ThisSetValue(realm, thisValue),
                    JsSetIteratorObject.IterationKind.Entries);
            }, "entries", 0);

        var differenceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            if (set.Count <= other.Size)
            {
                var filtered = new JsSetObject(realm);
                var cursor = 0;
                while (set.TryGetNextLiveValue(ref cursor, out var value))
                    if (!SetLikeHas(realm, other, value))
                        filtered.AddValue(value);

                return filtered;
            }

            var result = CloneSet(realm, set);
            IterateSetLikeKeys(realm, other, static (_, resultSet, _, value) => { resultSet.DeleteValue(value); },
                result,
                set);

            return result;
        }, "difference", 1);

        var intersectionFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            var result = new JsSetObject(realm);
            if (set.Count <= other.Size)
            {
                var cursor = 0;
                while (set.TryGetNextLiveValue(ref cursor, out var value))
                    if (SetLikeHas(realm, other, value))
                        result.AddValue(value);
            }
            else
            {
                IterateSetLikeKeys(realm, other, static (_, resultSet, receiverSet, value) =>
                {
                    if (receiverSet.HasValue(value))
                        resultSet.AddValue(value);
                }, result, set);
            }

            return result;
        }, "intersection", 1);

        var symmetricDifferenceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            var result = CloneSet(realm, set);

            IterateSetLikeKeys(realm, other, static (_, resultSet, receiverSet, value) =>
            {
                if (receiverSet.HasValue(value))
                    resultSet.DeleteValue(value);
                else
                    resultSet.AddValue(value);
            }, result, set);

            return result;
        }, "symmetricDifference", 1);

        var unionFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            var result = CloneSet(realm, set);

            IterateSetLikeKeys(realm, other, static (_, resultSet, _, value) => { resultSet.AddValue(value); }, result,
                set);

            return result;
        }, "union", 1);

        var isDisjointFromFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            if (set.Count <= other.Size)
            {
                var cursor = 0;
                while (set.TryGetNextLiveValue(ref cursor, out var value))
                    if (SetLikeHas(realm, other, value))
                        return JsValue.False;
            }
            else if (AnySetLikeKeyMatches(realm, other, static (receiverSet, value) => receiverSet.HasValue(value),
                         set))
            {
                return JsValue.False;
            }

            return JsValue.True;
        }, "isDisjointFrom", 1);

        var isSubsetOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            if (set.Count > other.Size)
                return JsValue.False;
            var cursor = 0;
            while (set.TryGetNextLiveValue(ref cursor, out var value))
                if (!SetLikeHas(realm, other, value))
                    return JsValue.False;

            return JsValue.True;
        }, "isSubsetOf", 1);

        var isSupersetOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisSetValue(realm, thisValue);
            var other = GetSetLikeOperand(realm, args);
            if (set.Count < other.Size)
                return JsValue.False;
            return AnySetLikeKeyMatches(realm, other, static (receiverSet, value) => !receiverSet.HasValue(value), set)
                ? JsValue.False
                : JsValue.True;
        }, "isSupersetOf", 1);

        var speciesGetter = new JsHostFunction(Realm, (in info) =>
            {
                var thisValue = info.ThisValue;
                return thisValue;
            }, "get [Symbol.species]",
            0);

        var iteratorNextFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsSetIteratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Set Iterator.prototype.next called on incompatible receiver");
            return iterator.Next();
        }, "next", 0);

        var iteratorSelfFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsSetIteratorObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Set Iterator [Symbol.iterator] called on incompatible receiver");
            return thisValue;
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(atomNextLocal, JsValue.FromObject(iteratorNextFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorSelfFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Set Iterator"),
                configurable: true)
        ];
        SetIteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);

        SetConstructor.InitializePrototypeProperty(SetPrototype);
        SetConstructor.DefineAccessorPropertyAtom(Realm, IdSymbolSpecies, speciesGetter, null,
            JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.Configurable);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(SetConstructor)),
            PropertyDefinition.Mutable(atomAdd, JsValue.FromObject(addFn)),
            PropertyDefinition.Mutable(atomClear, JsValue.FromObject(clearFn)),
            PropertyDefinition.Mutable(atomDelete, JsValue.FromObject(deleteFn)),
            PropertyDefinition.Mutable(atomDifference, JsValue.FromObject(differenceFn)),
            PropertyDefinition.Mutable(atomEntries, JsValue.FromObject(entriesFn)),
            PropertyDefinition.Mutable(atomForEach, JsValue.FromObject(forEachFn)),
            PropertyDefinition.Mutable(atomHas, JsValue.FromObject(hasFn)),
            PropertyDefinition.Mutable(atomIntersection, JsValue.FromObject(intersectionFn)),
            PropertyDefinition.Mutable(atomIsDisjointFrom, JsValue.FromObject(isDisjointFromFn)),
            PropertyDefinition.Mutable(atomIsSubsetOf, JsValue.FromObject(isSubsetOfFn)),
            PropertyDefinition.Mutable(atomIsSupersetOf, JsValue.FromObject(isSupersetOfFn)),
            PropertyDefinition.Mutable(atomKeys, JsValue.FromObject(valuesFn)),
            PropertyDefinition.Mutable(atomValues, JsValue.FromObject(valuesFn)),
            PropertyDefinition.Mutable(atomSymmetricDifference, JsValue.FromObject(symmetricDifferenceFn)),
            PropertyDefinition.Mutable(atomUnion, JsValue.FromObject(unionFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(valuesFn)),
            PropertyDefinition.GetterData(atomSize, sizeGetter, configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Set"), configurable: true)
        ];
        SetPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);
    }

    internal static JsSetObject ThisSetValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsSetObject set)
            return set;
        throw new JsRuntimeException(JsErrorKind.TypeError, "Set.prototype method called on incompatible receiver");
    }

    private static void PopulateSetFromIterable(JsRealm realm, JsSetObject set, JsObject iterable)
    {
        var adder = GetSetAdder(realm, set);
        if (iterable is JsArray array)
        {
            for (uint i = 0; i < array.Length; i++)
                if (array.TryGetElement(i, out var value))
                    CallSetAdder(realm, set, adder, value);

            return;
        }

        if (iterable is JsStringObject stringObject)
        {
            foreach (var rune in stringObject.Value.EnumerateRunes())
                CallSetAdder(realm, set, adder, JsValue.FromString(rune.ToString()));
            return;
        }

        var iterator = GetIteratorObjectForSet(realm, iterable);
        try
        {
            while (true)
            {
                var nextValue = StepIteratorForSet(realm, iterator, out var done);
                if (done)
                    break;
                CallSetAdder(realm, set, adder, nextValue);
            }
        }
        catch
        {
            realm.BestEffortIteratorCloseOnThrow(iterator);
            throw;
        }
    }

    private static JsFunction GetSetAdder(JsRealm realm, JsSetObject set)
    {
        const int atomAdd = IdAdd;
        if (!set.TryGetPropertyAtom(realm, atomAdd, out var adderValue, out _) ||
            !adderValue.TryGetObject(out var adderObj) || adderObj is not JsFunction adder)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set.prototype.add is not callable");
        return adder;
    }

    private static void CallSetAdder(JsRealm realm, JsSetObject set, JsFunction adder, in JsValue value)
    {
        var args = new InlineJsValueArray1 { Item0 = value };
        realm.InvokeFunction(adder, JsValue.FromObject(set), args.AsSpan());
    }

    private static JsObject GetIteratorObjectForSet(JsRealm realm, JsObject iterable)
    {
        return realm.GetIteratorObjectForIterable(iterable,
            "Set iterable is not iterable",
            "Set iterator result must be object");
    }

    private static JsValue StepIteratorForSet(JsRealm realm, JsObject iterator, out bool done)
    {
        return JsRealm.StepIteratorForIterable(realm, iterator,
            "Set iterator.next is not a function",
            "Set iterator result must be object",
            out done);
    }

    private static SetLikeOperand GetSetLikeOperand(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        if (args.Length == 0 || !args[0].TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set operation operand must be an object");

        var rawSize = obj.TryGetPropertyAtom(realm, IdSize, out var sizeValue, out _)
            ? sizeValue
            : JsValue.Undefined;
        var size = realm.ToNumberSlowPath(rawSize);
        if (double.IsNaN(size))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like operand size must be numeric");

        if (!obj.TryGetPropertyAtom(realm, IdHas, out var hasMethod, out _) ||
            !hasMethod.TryGetObject(out var hasMethodObj) || hasMethodObj is not JsFunction hasFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like operand has must be callable");

        if (!obj.TryGetPropertyAtom(realm, IdKeys, out var keysMethod, out _) ||
            !keysMethod.TryGetObject(out var keysMethodObj) || keysMethodObj is not JsFunction keysFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like operand keys must be callable");

        return new(obj, size, hasFn, keysFn);
    }

    private static bool SetLikeHas(JsRealm realm, in SetLikeOperand operand, in JsValue value)
    {
        var args = new InlineJsValueArray1 { Item0 = value };
        return JsRealm.ToBoolean(realm.InvokeFunction(operand.Has, JsValue.FromObject(operand.Object), args.AsSpan()));
    }

    private static void IterateSetLikeKeys(
        JsRealm realm,
        in SetLikeOperand operand,
        Action<JsRealm, JsSetObject, JsSetObject, JsValue> visitor,
        JsSetObject result,
        JsSetObject receiver)
    {
        var iteratorRecord = GetSetLikeKeysIterator(realm, operand);
        try
        {
            while (true)
            {
                var nextValue = StepSetLikeIterator(realm, iteratorRecord, out var done);
                if (done)
                    break;
                visitor(realm, result, receiver, nextValue);
            }
        }
        catch
        {
            realm.BestEffortIteratorCloseOnThrow(iteratorRecord.Iterator);
            throw;
        }
    }

    private static bool AnySetLikeKeyMatches(JsRealm realm, in SetLikeOperand operand,
        Func<JsSetObject, JsValue, bool> predicate, JsSetObject receiver)
    {
        var iteratorRecord = GetSetLikeKeysIterator(realm, operand);
        try
        {
            while (true)
            {
                var nextValue = StepSetLikeIterator(realm, iteratorRecord, out var done);
                if (done)
                    return false;
                if (!predicate(receiver, nextValue))
                    continue;
                realm.IteratorCloseForDestructuring(iteratorRecord.Iterator);
                return true;
            }
        }
        catch
        {
            realm.BestEffortIteratorCloseOnThrow(iteratorRecord.Iterator);
            throw;
        }
    }

    private static JsSetObject CloneSet(JsRealm realm, JsSetObject source)
    {
        var clone = new JsSetObject(realm);
        var cursor = 0;
        while (source.TryGetNextLiveValue(ref cursor, out var value))
            clone.AddValue(value);
        return clone;
    }

    private static SetLikeIteratorRecord GetSetLikeKeysIterator(JsRealm realm, in SetLikeOperand operand)
    {
        var iteratorValue =
            realm.InvokeFunction(operand.Keys, JsValue.FromObject(operand.Object), ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iterator))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like keys iterator result must be object");
        if (!iterator.TryGetPropertyAtom(realm, IdNext, out var nextMethod, out _) ||
            !nextMethod.TryGetObject(out var nextMethodObj) || nextMethodObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like keys iterator.next is not a function");

        return new(iterator, nextFn);
    }

    private static JsValue StepSetLikeIterator(JsRealm realm, in SetLikeIteratorRecord iteratorRecord, out bool done)
    {
        var stepResult =
            realm.InvokeFunction(iteratorRecord.Next, JsValue.FromObject(iteratorRecord.Iterator),
                ReadOnlySpan<JsValue>.Empty);
        if (!stepResult.TryGetObject(out var resultObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Set-like keys iterator result must be object");

        _ = resultObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
        done = JsRealm.ToBoolean(doneValue);
        if (done)
            return JsValue.Undefined;

        return resultObj.TryGetPropertyAtom(realm, IdValue, out var value, out _)
            ? value
            : JsValue.Undefined;
    }

    private readonly struct SetLikeOperand
    {
        internal readonly JsObject Object;
        internal readonly double Size;
        internal readonly JsFunction Has;
        internal readonly JsFunction Keys;

        internal SetLikeOperand(JsObject obj, double size, JsFunction has, JsFunction keys)
        {
            Object = obj;
            Size = size;
            Has = has;
            Keys = keys;
        }
    }

    private readonly struct SetLikeIteratorRecord
    {
        internal readonly JsObject Iterator;
        internal readonly JsFunction Next;

        internal SetLikeIteratorRecord(JsObject iterator, JsFunction next)
        {
            Iterator = iterator;
            Next = next;
        }
    }
}
