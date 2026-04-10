using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateWeakMapConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap constructor must be called with new");

            var map = new JsWeakMapObject(
                realm,
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.WeakMapPrototype));
            if (args.Length == 0 || args[0].IsUndefined || args[0].IsNull)
                return map;

            if (!realm.TryToObject(args[0], out var iterable))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "WeakMap iterable must not be null or undefined");

            PopulateWeakMapFromIterable(realm, map, iterable);
            return map;
        }, "WeakMap", 0, true);
    }

    private JsHostFunction CreateWeakSetConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakSet constructor must be called with new");

            var set = new JsWeakSetObject(
                realm,
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.WeakSetPrototype));
            if (args.Length == 0 || args[0].IsUndefined || args[0].IsNull)
                return set;

            if (!realm.TryToObject(args[0], out var iterable))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "WeakSet iterable must not be null or undefined");

            PopulateWeakSetFromIterable(realm, set, iterable);
            return set;
        }, "WeakSet", 0, true);
    }

    private JsHostFunction CreateWeakRefConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakRef constructor must be called with new");
            if (args.Length == 0 || !CanBeHeldWeakly(args[0]))
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakRef target must be an object or symbol");

            var prototype =
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.WeakRefPrototype);
            if (args[0].TryGetObject(out var target))
                return new JsWeakRefObject(realm, target, prototype);
            if (args[0].IsSymbol)
                return new JsWeakRefObject(realm, args[0].AsSymbol(), prototype);
            throw new JsRuntimeException(JsErrorKind.TypeError, "WeakRef target must be an object or symbol");
        }, "WeakRef", 1, true);
    }

    private JsHostFunction CreateFinalizationRegistryConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "FinalizationRegistry constructor must be called with new");
            if (args.Length == 0 || !args[0].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction cleanupCallback)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "FinalizationRegistry cleanup callback must be a function");

            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                realm.FinalizationRegistryPrototype);
            return new JsFinalizationRegistryObject(realm, prototype, cleanupCallback);
        }, "FinalizationRegistry", 1, true);
    }

    private void InstallWeakCollectionBuiltins()
    {
        const int atomGet = IdGet;
        const int atomSet = IdSet;
        const int atomHas = IdHas;
        const int atomDelete = IdDelete;
        const int atomAdd = IdAdd;
        const int atomDeref = IdDeref;
        const int atomGetOrInsert = IdGetOrInsert;
        const int atomGetOrInsertComputed = IdGetOrInsertComputed;

        var weakMapGetFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            if (args.Length == 0)
                return JsValue.Undefined;
            if (args[0].TryGetObject(out var objectKey))
                return map.TryGetValue(objectKey, out var objectValue) ? objectValue : JsValue.Undefined;
            if (CanBeHeldWeakly(args[0]))
                return map.TryGetValue(args[0].AsSymbol(), out var symbolValue) ? symbolValue : JsValue.Undefined;
            return JsValue.Undefined;
        }, "get", 1);

        var weakMapSetFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            if (args.Length == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap key must be an object or symbol");
            var value = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (args[0].TryGetObject(out var objectKey))
            {
                map.SetValue(objectKey, value);
                return thisValue;
            }

            if (CanBeHeldWeakly(args[0]))
            {
                map.SetValue(args[0].AsSymbol(), value);
                return thisValue;
            }

            throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap key must be an object or symbol");
        }, "set", 2);

        var weakMapHasFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            if (args.Length == 0)
                return JsValue.False;
            if (args[0].TryGetObject(out var objectKey))
                return map.HasKey(objectKey) ? JsValue.True : JsValue.False;
            if (CanBeHeldWeakly(args[0]))
                return map.HasKey(args[0].AsSymbol()) ? JsValue.True : JsValue.False;
            return JsValue.False;
        }, "has", 1);

        var weakMapDeleteFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            if (args.Length == 0)
                return JsValue.False;
            if (args[0].TryGetObject(out var objectKey))
                return map.DeleteKey(objectKey) ? JsValue.True : JsValue.False;
            if (CanBeHeldWeakly(args[0]))
                return map.DeleteKey(args[0].AsSymbol()) ? JsValue.True : JsValue.False;
            return JsValue.False;
        }, "delete", 1);

        var weakSetAddFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisWeakSetValue(realm, thisValue);
            if (args.Length == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakSet value must be an object or symbol");
            if (args[0].TryGetObject(out var objectKey))
            {
                set.AddValue(objectKey);
                return thisValue;
            }

            if (CanBeHeldWeakly(args[0]))
            {
                set.AddValue(args[0].AsSymbol());
                return thisValue;
            }

            throw new JsRuntimeException(JsErrorKind.TypeError, "WeakSet value must be an object or symbol");
        }, "add", 1);

        var weakSetHasFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisWeakSetValue(realm, thisValue);
            if (args.Length == 0)
                return JsValue.False;
            if (args[0].TryGetObject(out var objectKey))
                return set.HasValue(objectKey) ? JsValue.True : JsValue.False;
            if (CanBeHeldWeakly(args[0]))
                return set.HasValue(args[0].AsSymbol()) ? JsValue.True : JsValue.False;
            return JsValue.False;
        }, "has", 1);

        var weakSetDeleteFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var set = ThisWeakSetValue(realm, thisValue);
            if (args.Length == 0)
                return JsValue.False;
            if (args[0].TryGetObject(out var objectKey))
                return set.DeleteValue(objectKey) ? JsValue.True : JsValue.False;
            if (CanBeHeldWeakly(args[0]))
                return set.DeleteValue(args[0].AsSymbol()) ? JsValue.True : JsValue.False;
            return JsValue.False;
        }, "delete", 1);

        var weakMapGetOrInsertFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (TryGetWeakCollectionKey(key, out var objectKey, out var symbolKey))
            {
                if (objectKey is not null && map.TryGetValue(objectKey, out var existingObjectValue))
                    return existingObjectValue;
                if (symbolKey is not null && map.TryGetValue(symbolKey, out var existingSymbolValue))
                    return existingSymbolValue;
            }
            else
            {
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap key must be an object or symbol");
            }

            var value = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (objectKey is not null)
                map.SetValue(objectKey, value);
            else
                map.SetValue(symbolKey!, value);
            return value;
        }, "getOrInsert", 2);

        var weakMapGetOrInsertComputedFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var map = ThisWeakMapValue(realm, thisValue);
            var key = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (args.Length < 2 || !args[1].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "WeakMap.prototype.getOrInsertComputed callback must be a function");
            if (!TryGetWeakCollectionKey(key, out var objectKey, out var symbolKey))
                throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap key must be an object or symbol");

            if (objectKey is not null && map.TryGetValue(objectKey, out var existingObjectValue))
                return existingObjectValue;
            if (symbolKey is not null && map.TryGetValue(symbolKey, out var existingSymbolValue))
                return existingSymbolValue;

            var callbackArgs = new InlineJsValueArray1 { Item0 = key };
            var value = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs.AsSpan());
            if (objectKey is not null)
                map.SetValue(objectKey, value);
            else
                map.SetValue(symbolKey!, value);
            return value;
        }, "getOrInsertComputed", 2);

        var weakRefDerefFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return ThisWeakRefValue(realm, thisValue).Deref();
            }, "deref", 0);

        var finalizationRegistryRegisterFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var callee = info.Function;
            _ = callee;
            var registry = ThisFinalizationRegistryValue(realm, thisValue);
            var target = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (!CanBeHeldWeakly(target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "FinalizationRegistry target must be an object or non-registered symbol");
            var heldValue = args.Length > 1 ? args[1] : JsValue.Undefined;
            if (JsValue.SameValue(target, heldValue))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "FinalizationRegistry target and holdings must not be the same value");

            var unregisterToken = JsValue.Undefined;
            if (args.Length > 2)
            {
                unregisterToken = args[2];
                if (!unregisterToken.IsUndefined && !CanBeHeldWeakly(unregisterToken))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "FinalizationRegistry unregisterToken must be undefined, an object, or a non-registered symbol");
            }

            registry.RegisterTarget(target, heldValue, unregisterToken);
            return JsValue.Undefined;
        }, "register", 2);

        var finalizationRegistryUnregisterFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var callee = info.Function;
            _ = callee;
            var registry = ThisFinalizationRegistryValue(realm, thisValue);
            var unregisterToken = args.Length != 0 ? args[0] : JsValue.Undefined;
            if (!CanBeHeldWeakly(unregisterToken))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "FinalizationRegistry unregisterToken must be an object or non-registered symbol");

            return registry.Unregister(unregisterToken) ? JsValue.True : JsValue.False;
        }, "unregister", 1);

        WeakMapConstructor.InitializePrototypeProperty(WeakMapPrototype);

        Span<PropertyDefinition> weakMapProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(WeakMapConstructor)),
            PropertyDefinition.Mutable(atomGet, JsValue.FromObject(weakMapGetFn)),
            PropertyDefinition.Mutable(atomSet, JsValue.FromObject(weakMapSetFn)),
            PropertyDefinition.Mutable(atomHas, JsValue.FromObject(weakMapHasFn)),
            PropertyDefinition.Mutable(atomDelete, JsValue.FromObject(weakMapDeleteFn)),
            PropertyDefinition.Mutable(atomGetOrInsert, JsValue.FromObject(weakMapGetOrInsertFn)),
            PropertyDefinition.Mutable(atomGetOrInsertComputed, JsValue.FromObject(weakMapGetOrInsertComputedFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("WeakMap"), configurable: true)
        ];
        WeakMapPrototype.DefineNewPropertiesNoCollision(Realm, weakMapProtoDefs);

        WeakSetConstructor.InitializePrototypeProperty(WeakSetPrototype);

        Span<PropertyDefinition> weakSetProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(WeakSetConstructor)),
            PropertyDefinition.Mutable(atomAdd, JsValue.FromObject(weakSetAddFn)),
            PropertyDefinition.Mutable(atomHas, JsValue.FromObject(weakSetHasFn)),
            PropertyDefinition.Mutable(atomDelete, JsValue.FromObject(weakSetDeleteFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("WeakSet"), configurable: true)
        ];
        WeakSetPrototype.DefineNewPropertiesNoCollision(Realm, weakSetProtoDefs);

        WeakRefConstructor.InitializePrototypeProperty(WeakRefPrototype);

        Span<PropertyDefinition> weakRefProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(WeakRefConstructor)),
            PropertyDefinition.Mutable(atomDeref, JsValue.FromObject(weakRefDerefFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("WeakRef"), configurable: true)
        ];
        WeakRefPrototype.DefineNewPropertiesNoCollision(Realm, weakRefProtoDefs);

        FinalizationRegistryConstructor.InitializePrototypeProperty(FinalizationRegistryPrototype);

        Span<PropertyDefinition> finalizationRegistryProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(FinalizationRegistryConstructor)),
            PropertyDefinition.Mutable(IdRegister, JsValue.FromObject(finalizationRegistryRegisterFn)),
            PropertyDefinition.Mutable(IdUnregister, JsValue.FromObject(finalizationRegistryUnregisterFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("FinalizationRegistry"),
                configurable: true)
        ];
        FinalizationRegistryPrototype.DefineNewPropertiesNoCollision(Realm, finalizationRegistryProtoDefs);
    }

    private static JsWeakMapObject ThisWeakMapValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsWeakMapObject weakMap)
            return weakMap;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "WeakMap.prototype method called on incompatible receiver");
    }

    private static JsWeakSetObject ThisWeakSetValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsWeakSetObject weakSet)
            return weakSet;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "WeakSet.prototype method called on incompatible receiver");
    }

    private static JsWeakRefObject ThisWeakRefValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsWeakRefObject weakRef)
            return weakRef;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "WeakRef.prototype method called on incompatible receiver");
    }

    private static JsFinalizationRegistryObject ThisFinalizationRegistryValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsFinalizationRegistryObject registry)
            return registry;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "FinalizationRegistry.prototype method called on incompatible receiver");
    }

    private static bool CanBeHeldWeakly(in JsValue value)
    {
        if (value.TryGetObject(out _))
            return true;
        return value.IsSymbol && !value.AsSymbol().IsRegistered;
    }

    private static void PopulateWeakMapFromIterable(JsRealm realm, JsWeakMapObject map, JsObject iterable)
    {
        const int atomSet = IdSet;
        if (!map.TryGetPropertyAtom(realm, atomSet, out var adderValue, out _) ||
            !adderValue.TryGetObject(out var adderObj) || adderObj is not JsFunction adder)
            throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap.prototype.set is not callable");

        var iteratorObj = GetWeakCollectionIterator(realm, iterable, "WeakMap");
        var nextFn = GetWeakCollectionIteratorNext(realm, iteratorObj, "WeakMap");

        while (true)
        {
            JsValue entryValue;
            try
            {
                entryValue = StepWeakCollectionIterator(realm, iteratorObj, nextFn, out var done, "WeakMap");
                if (done)
                    return;
            }
            catch
            {
                BestEffortIteratorCloseForWeakCollection(realm, iteratorObj);
                throw;
            }

            try
            {
                if (!realm.TryToObject(entryValue, out var entryObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "WeakMap iterable entry is not an object");
                _ = entryObj.TryGetElement(0, out var keyValue);
                _ = entryObj.TryGetElement(1, out var mappedValue);
                var args = new InlineJsValueArray2
                {
                    Item0 = keyValue,
                    Item1 = mappedValue
                };
                realm.InvokeFunction(adder, JsValue.FromObject(map), args.AsSpan());
            }
            catch
            {
                BestEffortIteratorCloseForWeakCollection(realm, iteratorObj);
                throw;
            }
        }
    }

    private static void PopulateWeakSetFromIterable(JsRealm realm, JsWeakSetObject set, JsObject iterable)
    {
        const int atomAdd = IdAdd;
        if (!set.TryGetPropertyAtom(realm, atomAdd, out var adderValue, out _) ||
            !adderValue.TryGetObject(out var adderObj) || adderObj is not JsFunction adder)
            throw new JsRuntimeException(JsErrorKind.TypeError, "WeakSet.prototype.add is not callable");

        var iteratorObj = GetWeakCollectionIterator(realm, iterable, "WeakSet");
        var nextFn = GetWeakCollectionIteratorNext(realm, iteratorObj, "WeakSet");

        while (true)
        {
            JsValue value;
            try
            {
                value = StepWeakCollectionIterator(realm, iteratorObj, nextFn, out var done, "WeakSet");
                if (done)
                    return;
            }
            catch
            {
                BestEffortIteratorCloseForWeakCollection(realm, iteratorObj);
                throw;
            }

            try
            {
                var args = new InlineJsValueArray1 { Item0 = value };
                realm.InvokeFunction(adder, JsValue.FromObject(set), args.AsSpan());
            }
            catch
            {
                BestEffortIteratorCloseForWeakCollection(realm, iteratorObj);
                throw;
            }
        }
    }

    private static bool TryGetWeakCollectionKey(in JsValue key, out JsObject? objectKey, out Symbol? symbolKey)
    {
        if (key.TryGetObject(out var obj))
        {
            objectKey = obj;
            symbolKey = null;
            return true;
        }

        if (CanBeHeldWeakly(key))
        {
            objectKey = null;
            symbolKey = key.AsSymbol();
            return true;
        }

        objectKey = null;
        symbolKey = null;
        return false;
    }

    private static JsObject GetWeakCollectionIterator(JsRealm realm, JsObject iterable, string collectionName)
    {
        if (!iterable.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethod, out _) ||
            !iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{collectionName} iterable is not iterable");

        var iteratorValue = realm.InvokeFunction(iteratorFn, JsValue.FromObject(iterable), ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{collectionName} iterator is not an object");
        return iteratorObj;
    }

    private static JsFunction GetWeakCollectionIteratorNext(JsRealm realm, JsObject iteratorObj,
        string collectionName)
    {
        const int atomNext = IdNext;
        if (!iteratorObj.TryGetPropertyAtom(realm, atomNext, out var nextMethod, out _) ||
            !nextMethod.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{collectionName} iterator next is not callable");

        return nextFn;
    }

    private static JsValue StepWeakCollectionIterator(JsRealm realm, JsObject iteratorObj, JsFunction nextFn,
        out bool done, string collectionName)
    {
        var step = realm.InvokeFunction(nextFn, JsValue.FromObject(iteratorObj), ReadOnlySpan<JsValue>.Empty);
        if (!step.TryGetObject(out var stepObj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"{collectionName} iterator result is not an object");

        _ = stepObj.TryGetPropertyAtom(realm, IdDone, out var doneValue, out _);
        if (doneValue.ToBoolean())
        {
            done = true;
            return JsValue.Undefined;
        }

        done = false;
        return stepObj.TryGetPropertyAtom(realm, IdValue, out var value, out _)
            ? value
            : JsValue.Undefined;
    }

    private static void BestEffortIteratorCloseForWeakCollection(JsRealm realm, JsObject iteratorObj)
    {
        try
        {
            const int atomReturn = IdReturn;
            if (!iteratorObj.TryGetPropertyAtom(realm, atomReturn, out var returnMethod, out _) ||
                returnMethod.IsUndefined || returnMethod.IsNull)
                return;

            if (!returnMethod.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                return;

            _ = realm.InvokeFunction(returnFn, JsValue.FromObject(iteratorObj), ReadOnlySpan<JsValue>.Empty);
        }
        catch
        {
        }
    }
}
