using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateMapConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Map constructor must be called with new");

            var prototype =
                realm.Intrinsics.GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.MapPrototype);
            var map = new JsMapObject(realm, prototype);
            if (args.Length == 0 || args[0].IsUndefined || args[0].IsNull)
                return map;

            if (!realm.TryToObject(args[0], out var iterable))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Map iterable must not be null or undefined");

            PopulateMapFromIterable(realm, map, iterable);
            return map;
        }, "Map", 0, true);
    }

    private void InstallMapConstructorBuiltins()
    {
        const int atomGet = IdGet;
        const int atomSet = IdSet;
        const int atomHas = IdHas;
        const int atomDelete = IdDelete;
        const int atomClear = IdClear;
        const int atomForEach = IdForEach;
        const int atomSize = IdSize;
        const int atomGroupBy = IdGroupBy;
        const int atomGetOrInsert = IdGetOrInsert;
        const int atomGetOrInsertComputed = IdGetOrInsertComputed;
        const int atomEntries = IdEntries;
        const int atomKeys = IdKeys;
        const int atomValues = IdValues;
        const int atomNextLocal = IdNext;

        var speciesGetter = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            return thisValue;
        }, "get [Symbol.species]", 0);

        var getFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            return map.TryGetValue(key, out var value) ? value : JsValue.Undefined;
        }, "get", 1);

        var setFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            var value = args.Length > 1 ? args[1] : JsValue.Undefined;
            map.SetValue(key, value);
            return thisValue;
        }, "set", 2);

        var hasFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            return map.HasKey(key) ? JsValue.True : JsValue.False;
        }, "has", 1);

        var deleteFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            return map.DeleteKey(key) ? JsValue.True : JsValue.False;
        }, "delete", 1);

        var clearFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            ThisMapValue(realm, thisValue).ClearEntries();
            return JsValue.Undefined;
        }, "clear", 0);

        var forEachFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Map.prototype.forEach callback must be a function");

            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            var cursor = 0;
            while (map.TryGetNextLiveEntry(ref cursor, out var key, out var value))
            {
                var callbackArgs = new InlineJsValueArray3
                {
                    Item0 = value,
                    Item1 = key,
                    Item2 = JsValue.FromObject(map)
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
                return JsValue.FromInt32(ThisMapValue(realm, thisValue).Count);
            },
            "get size", 0);

        var entriesFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return new JsMapIteratorObject(realm, ThisMapValue(realm, thisValue),
                    JsMapIteratorObject.IterationKind.Entries);
            }, "entries", 0);

        var keysFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return new JsMapIteratorObject(realm, ThisMapValue(realm, thisValue),
                    JsMapIteratorObject.IterationKind.Keys);
            }, "keys", 0);

        var valuesFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return new JsMapIteratorObject(realm, ThisMapValue(realm, thisValue),
                    JsMapIteratorObject.IterationKind.Values);
            }, "values", 0);

        var getOrInsertFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (map.TryGetValue(key, out var existingValue))
                return existingValue;

            var value = args.Length > 1 ? args[1] : JsValue.Undefined;
            map.SetValue(key, value);
            return value;
        }, "getOrInsert", 2);

        var getOrInsertComputedFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (map.TryGetValue(key, out var existingValue))
                return existingValue;

            if (args.Length < 2 || !args[1].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Map.prototype.getOrInsertComputed callback must be a function");

            var callbackArgs = new InlineJsValueArray1 { Item0 = key };
            var value = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs.AsSpan());
            map.SetValue(key, value);
            return value;
        }, "getOrInsertComputed", 2);

        var groupByFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var items))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Map.groupBy items must not be null or undefined");

            if (args.Length < 2 || !args[1].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Map.groupBy callback must be a function");

            var result = new JsMapObject(realm);

            if (items is JsArray array)
            {
                for (uint i = 0; i < array.Length; i++)
                {
                    var value = array.TryGetElement(i, out var existingValue) ? existingValue : JsValue.Undefined;
                    AppendMapGroupByValueForCallback(realm, result, callbackFn, value, i);
                }
            }
            else if (items is JsStringObject stringObject)
            {
                long index = 0;
                foreach (var codePoint in stringObject.Value.EnumerateRunes())
                {
                    AppendMapGroupByValueForCallback(realm, result, callbackFn,
                        JsValue.FromString(codePoint.ToString()), index);
                    index++;
                }
            }
            else
            {
                var iterator = GetIteratorObjectForMap(realm, items);
                long index = 0;
                while (true)
                {
                    var nextValue = StepIteratorForMap(realm, iterator, out var done);
                    if (done)
                        break;
                    AppendMapGroupByValueForCallback(realm, result, callbackFn, nextValue, index);
                    index++;
                }
            }

            return result;
        }, "groupBy", 2);

        var iteratorNextFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsMapIteratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Map Iterator.prototype.next called on incompatible receiver");
            return iterator.Next();
        }, "next", 0);

        var iteratorSelfFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsMapIteratorObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Map Iterator [Symbol.iterator] called on incompatible receiver");
            return thisValue;
        }, "[Symbol.iterator]", 0);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(atomNextLocal, JsValue.FromObject(iteratorNextFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorSelfFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Map Iterator"),
                configurable: true)
        ];
        MapIteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.GetterData(IdSymbolSpecies, speciesGetter, configurable: true),
            PropertyDefinition.Mutable(atomGroupBy, JsValue.FromObject(groupByFn))
        ];
        MapConstructor.InitializePrototypeProperty(MapPrototype);
        MapConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(MapConstructor)),
            PropertyDefinition.Mutable(atomGet, JsValue.FromObject(getFn)),
            PropertyDefinition.Mutable(atomSet, JsValue.FromObject(setFn)),
            PropertyDefinition.Mutable(atomHas, JsValue.FromObject(hasFn)),
            PropertyDefinition.Mutable(atomDelete, JsValue.FromObject(deleteFn)),
            PropertyDefinition.Mutable(atomClear, JsValue.FromObject(clearFn)),
            PropertyDefinition.Mutable(atomForEach, JsValue.FromObject(forEachFn)),
            PropertyDefinition.Mutable(atomGetOrInsert, JsValue.FromObject(getOrInsertFn)),
            PropertyDefinition.Mutable(atomGetOrInsertComputed, JsValue.FromObject(getOrInsertComputedFn)),
            PropertyDefinition.Mutable(atomEntries, JsValue.FromObject(entriesFn)),
            PropertyDefinition.Mutable(atomKeys, JsValue.FromObject(keysFn)),
            PropertyDefinition.Mutable(atomValues, JsValue.FromObject(valuesFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(entriesFn)),
            PropertyDefinition.GetterData(atomSize, sizeGetter, configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("Map"), configurable: true)
        ];
        MapPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);
    }

    internal static JsMapObject ThisMapValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsMapObject map)
            return map;
        throw new JsRuntimeException(JsErrorKind.TypeError, "Map.prototype method called on incompatible receiver");
    }

    private static void PopulateMapFromIterable(JsRealm realm, JsMapObject map, JsObject iterable)
    {
        var adder = GetMapAdder(realm, map);
        if (iterable is JsArray array)
        {
            for (uint i = 0; i < array.Length; i++)
            {
                if (!array.TryGetElement(i, out var entryValue))
                    continue;
                AddMapEntryFromIterableValue(realm, map, adder, entryValue);
            }

            return;
        }

        var iterator = GetIteratorObjectForMap(realm, iterable);
        while (true)
        {
            JsValue nextValue;
            bool done;
            try
            {
                nextValue = StepIteratorForMap(realm, iterator, out done);
                if (done)
                    break;
                AddMapEntryFromIterableValue(realm, map, adder, nextValue);
            }
            catch
            {
                realm.BestEffortIteratorCloseOnThrow(iterator);
                throw;
            }
        }
    }

    private static JsFunction GetMapAdder(JsRealm realm, JsMapObject map)
    {
        const int atomSet = IdSet;
        if (!map.TryGetPropertyAtom(realm, atomSet, out var adderValue, out _) ||
            !adderValue.TryGetObject(out var adderObj) || adderObj is not JsFunction adder)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Map.prototype.set is not callable");
        return adder;
    }

    private static JsObject GetIteratorObjectForMap(JsRealm realm, JsObject iterable)
    {
        return realm.GetIteratorObjectForIterable(iterable,
            "Map iterable is not iterable",
            "Map iterator result must be object");
    }

    private static JsValue StepIteratorForMap(JsRealm realm, JsObject iterator, out bool done)
    {
        return JsRealm.StepIteratorForIterable(realm, iterator,
            "Map iterator.next is not a function",
            "Map iterator result must be object",
            out done);
    }

    private static void AddMapEntryFromIterableValue(JsRealm realm, JsMapObject map, JsFunction adder,
        in JsValue entryValue)
    {
        if (!entryValue.TryGetObject(out var entry))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Map iterable value must be an object");

        var key = entry.TryGetElementWithReceiver(realm, entry, 0, out var keyValue) ? keyValue : JsValue.Undefined;
        var value = entry.TryGetElementWithReceiver(realm, entry, 1, out var valueValue)
            ? valueValue
            : JsValue.Undefined;
        var args = new InlineJsValueArray2
        {
            Item0 = key,
            Item1 = value
        };
        realm.InvokeFunction(adder, JsValue.FromObject(map), args.AsSpan());
    }

    private static void AppendMapGroupByValueForCallback(
        JsRealm realm,
        JsMapObject result,
        JsFunction callbackFn,
        in JsValue value,
        long index)
    {
        var callbackArgs = new InlineJsValueArray2
        {
            Item0 = value,
            Item1 = index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index)
        };
        var key = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs.AsSpan());
        if (result.TryGetValue(key, out var existingGroup))
        {
            if (!existingGroup.TryGetObject(out var existingGroupObj) ||
                existingGroupObj is not JsArray existingArray)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Map.groupBy internal group must be an array");
            existingArray.SetElement(existingArray.Length, value);
            return;
        }

        var newGroupArray = realm.CreateArrayObject();
        newGroupArray.SetElement(0, value);
        result.SetValue(key, JsValue.FromObject(newGroupArray));
    }
}
