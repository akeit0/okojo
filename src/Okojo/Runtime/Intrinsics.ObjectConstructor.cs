using System.Diagnostics;
using System.Globalization;
using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private const int OwnDataDescriptorValueSlot = 0;
    private const int OwnDataDescriptorWritableSlot = 1;
    private const int OwnDataDescriptorEnumerableSlot = 2;
    private const int OwnDataDescriptorConfigurableSlot = 3;
    private const int OwnAccessorDescriptorGetSlot = 0;
    private const int OwnAccessorDescriptorSetSlot = 1;
    private const int OwnAccessorDescriptorEnumerableSlot = 2;
    private const int OwnAccessorDescriptorConfigurableSlot = 3;
    private StaticNamedPropertyLayout? ownAccessorDescriptorShape;
    private StaticNamedPropertyLayout? ownDataDescriptorShape;

    private JsPlainObject CreateOwnDataDescriptorObject(
        in JsValue value,
        in JsValue writable,
        in JsValue enumerable,
        in JsValue configurable)
    {
        var shape = ownDataDescriptorShape ??= CreateOwnDataDescriptorShape();
        var result = new JsPlainObject(shape);
        result.SetNamedSlotUnchecked(OwnDataDescriptorValueSlot, value);
        result.SetNamedSlotUnchecked(OwnDataDescriptorWritableSlot, writable);
        result.SetNamedSlotUnchecked(OwnDataDescriptorEnumerableSlot, enumerable);
        result.SetNamedSlotUnchecked(OwnDataDescriptorConfigurableSlot, configurable);
        return result;
    }

    private JsPlainObject CreateOwnAccessorDescriptorObject(
        in JsValue getter,
        in JsValue setter,
        in JsValue enumerable,
        in JsValue configurable)
    {
        var shape = ownAccessorDescriptorShape ??= CreateOwnAccessorDescriptorShape();
        var result = new JsPlainObject(shape);
        result.SetNamedSlotUnchecked(OwnAccessorDescriptorGetSlot, getter);
        result.SetNamedSlotUnchecked(OwnAccessorDescriptorSetSlot, setter);
        result.SetNamedSlotUnchecked(OwnAccessorDescriptorEnumerableSlot, enumerable);
        result.SetNamedSlotUnchecked(OwnAccessorDescriptorConfigurableSlot, configurable);
        return result;
    }

    private StaticNamedPropertyLayout CreateOwnDataDescriptorShape()
    {
        var shape = Realm.EmptyShape.GetOrAddTransition(IdValue, JsShapePropertyFlags.Open, out var valueInfo);
        shape = shape.GetOrAddTransition(IdWritable, JsShapePropertyFlags.Open, out var writableInfo);
        shape = shape.GetOrAddTransition(IdEnumerable, JsShapePropertyFlags.Open, out var enumerableInfo);
        shape = shape.GetOrAddTransition(IdConfigurable, JsShapePropertyFlags.Open, out var configurableInfo);
        Debug.Assert(valueInfo.Slot == OwnDataDescriptorValueSlot);
        Debug.Assert(writableInfo.Slot == OwnDataDescriptorWritableSlot);
        Debug.Assert(enumerableInfo.Slot == OwnDataDescriptorEnumerableSlot);
        Debug.Assert(configurableInfo.Slot == OwnDataDescriptorConfigurableSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateOwnAccessorDescriptorShape()
    {
        var shape = Realm.EmptyShape.GetOrAddTransition(IdGet, JsShapePropertyFlags.Open, out var getInfo);
        shape = shape.GetOrAddTransition(IdSet, JsShapePropertyFlags.Open, out var setInfo);
        shape = shape.GetOrAddTransition(IdEnumerable, JsShapePropertyFlags.Open, out var enumerableInfo);
        shape = shape.GetOrAddTransition(IdConfigurable, JsShapePropertyFlags.Open, out var configurableInfo);
        Debug.Assert(getInfo.Slot == OwnAccessorDescriptorGetSlot);
        Debug.Assert(setInfo.Slot == OwnAccessorDescriptorSetSlot);
        Debug.Assert(enumerableInfo.Slot == OwnAccessorDescriptorEnumerableSlot);
        Debug.Assert(configurableInfo.Slot == OwnAccessorDescriptorConfigurableSlot);
        return shape;
    }

    private void InstallObjectConstructorBuiltins()
    {
        var createFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.create prototype must be object or null");

            JsObject? proto;
            if (args[0].IsNull) proto = null;
            else if (args[0].TryGetObject(out var protoObj)) proto = protoObj;
            else
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.create prototype must be object or null");

            var obj = new JsPlainObject(realm, false) { Prototype = proto };
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                if (!realm.TryToObject(args[1], out var props))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.create properties must not be null or undefined");
                ApplyPropertyDescriptorsFromObject(realm, obj, props);
            }

            return obj;
        }, "create", 2);
        var getPrototypeOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.getPrototypeOf target must not be null or undefined");
            return target.GetPrototypeOf(realm) ?? JsValue.Null;
        }, "getPrototypeOf", 1);
        var setPrototypeOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.setPrototypeOf requires target and prototype");

            var targetValue = args[0];
            if (targetValue.IsNull || targetValue.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.setPrototypeOf target must be object");

            JsObject? proto;
            if (args[1].IsNull) proto = null;
            else if (args[1].TryGetObject(out var p)) proto = p;
            else
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.setPrototypeOf prototype must be object or null");

            if (!targetValue.TryGetObject(out var target))
                return targetValue;

            var ok = ProxyCore.SetPrototypeOnTarget(info.Realm, target, proto);

            if (!ok)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.setPrototypeOf failed");
            return targetValue;
        }, "setPrototypeOf", 2);
        var preventExtensionsFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return args.Length == 0 ? JsValue.Undefined : args[0];

            ProxyCore.PreventExtensionsOnTarget(info.Realm, target);
            if (target is not IProxyObject && target.IsExtensible)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.preventExtensions failed");
            return args[0];
        }, "preventExtensions", 1);
        var isExtensibleFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return JsValue.False;

            return ProxyCore.QueryIsExtensible(info.Realm, target) ? JsValue.True : JsValue.False;
        }, "isExtensible", 1);
        var sealFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return args[0];

            target.SealDataProperties();
            target.PreventExtensions();
            if (target.IsExtensible)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.seal failed");
            return args[0];
        }, "seal", 1);
        var freezeFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return args[0];

            target.FreezeDataProperties();
            target.PreventExtensions();
            if (target.IsExtensible)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.freeze failed");
            return args[0];
        }, "freeze", 1);
        var isSealedFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return JsValue.False;

            return !QueryIsExtensibleForIntegrityLevel(realm, target) &&
                   AreAllOwnPropertiesSealedForIntegrityLevel(realm, target)
                ? JsValue.True
                : JsValue.False;
        }, "isSealed", 1);
        var isFrozenFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var target))
                return JsValue.False;

            return !QueryIsExtensibleForIntegrityLevel(realm, target) &&
                   AreAllOwnPropertiesFrozenForIntegrityLevel(realm, target)
                ? JsValue.True
                : JsValue.False;
        }, "isFrozen", 1);
        var getOwnPropertyDescriptorFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.getOwnPropertyDescriptor target must not be null or undefined");

            return GetOwnPropertyDescriptorByKey(realm, target, JsRealm.NormalizePropertyKey(realm, args[1]));
        }, "getOwnPropertyDescriptor", 2);
        var definePropertyFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 3 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.defineProperty target must be object");

            DefinePropertyFromDescriptorValue(realm, target, args[1], args[2]);
            return args[0];
        }, "defineProperty", 3);
        var definePropertiesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.defineProperties target must be object");

            if (!realm.TryToObject(args[1], out var props))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.defineProperties descriptors must not be null or undefined");
            ApplyPropertyDescriptorsFromObject(realm, target, props);

            return args[0];
        }, "defineProperties", 2);
        var assignFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (IsCurrentCallConstruct(realm))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.assign is not a constructor");

            if (args.Length == 0 || !realm.TryToObject(args[0], out var to))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert undefined or null to object");

            if (args.Length == 1)
                return to;

            var elementIndices = Realm.RentScratchList<uint>(8);
            var enumerableNamedAtoms = Realm.RentScratchList<int>(8);
            try
            {
                for (var srcIndex = 1; srcIndex < args.Length; srcIndex++)
                {
                    var nextSource = args[srcIndex];
                    if (nextSource.IsNull || nextSource.IsUndefined)
                        continue;
                    if (!realm.TryToObject(nextSource, out var from))
                        continue;

                    if (TryAssignFromProxySource(realm, from, to))
                        continue;

                    if (from is JsStringObject stringSource)
                    {
                        for (uint key = 0; key < (uint)stringSource.Value.Length; key++)
                        {
                            _ = from.TryGetElement(key, out var propValue);
                            if (!to.TrySetElement(key, propValue))
                                throw new JsRuntimeException(JsErrorKind.TypeError,
                                    "Cannot assign to read only property");
                        }
                    }
                    else if (from is JsArray arraySource)
                    {
                        for (uint key = 0; key < arraySource.Length; key++)
                        {
                            if (!from.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                                continue;
                            _ = from.TryGetElement(key, out var propValue);
                            if (!to.TrySetElement(key, propValue))
                                throw new JsRuntimeException(JsErrorKind.TypeError,
                                    "Cannot assign to read only property");
                        }
                    }
                    else
                    {
                        elementIndices.Clear();
                        from.CollectOwnElementIndices(elementIndices, false);
                        if (elementIndices.Count != 0)
                            elementIndices.Sort();
                        for (var i = 0; i < elementIndices.Count; i++)
                        {
                            var key = elementIndices[i];
                            if (!from.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                                continue;
                            _ = from.TryGetElement(key, out var propValue);
                            if (!to.TrySetElement(key, propValue))
                                throw new JsRuntimeException(JsErrorKind.TypeError,
                                    "Cannot assign to read only property");
                        }
                    }

                    enumerableNamedAtoms.Clear();
                    from.CollectOwnNamedPropertyAtoms(realm, enumerableNamedAtoms, true);
                    for (var i = 0; i < enumerableNamedAtoms.Count; i++)
                    {
                        var atom = enumerableNamedAtoms[i];
                        if (atom < 0)
                            continue;
                        if (!from.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                            continue;
                        if (!to.TrySetPropertyAtom(realm, atom, propValue, out _))
                            throw new JsRuntimeException(JsErrorKind.TypeError,
                                "Cannot assign to read only property");
                    }

                    for (var i = 0; i < enumerableNamedAtoms.Count; i++)
                    {
                        var atom = enumerableNamedAtoms[i];
                        if (atom >= 0)
                            continue;
                        if (!from.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                            continue;
                        if (!to.TrySetPropertyAtom(realm, atom, propValue, out _))
                            throw new JsRuntimeException(JsErrorKind.TypeError,
                                "Cannot assign to read only property");
                    }

                    if (from is JsGlobalObject globalSource)
                        foreach (var pair in globalSource.EnumerateNamedGlobalDescriptors())
                        {
                            var atom = pair.Key;
                            var desc = pair.Value;
                            if (!desc.Enumerable)
                                continue;
                            if (!from.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                                continue;
                            if (!to.TrySetPropertyAtom(realm, atom, propValue, out _))
                                throw new JsRuntimeException(JsErrorKind.TypeError,
                                    "Cannot assign to read only property");
                        }
                }
            }
            finally
            {
                Realm.ReturnScratchList(enumerableNamedAtoms);
                Realm.ReturnScratchList(elementIndices);
            }

            return to;
        }, "assign", 2);

        static bool IsCurrentCallConstruct(JsRealm realm)
        {
            return realm.CurrentCallInfo.IsConstruct;
        }

        static bool TryAssignFromProxySource(JsRealm realm, JsObject from, JsObject to)
        {
            JsObject? proxyTarget = null;
            var isProxy = from.TryGetOwnKeysTrapKeys(realm, out var trapKeys) ||
                          from.TryGetProxyTarget(out proxyTarget);

            if (!isProxy)
                return false;

            var keys = trapKeys;
            if (keys is null)
            {
                keys = realm.RentScratchList<JsValue>(8);
                CollectOwnPropertyKeysForProxyFallback(realm, proxyTarget!, keys, false);
            }

            try
            {
                if (keys.Count == 0)
                    return true;

                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (!TryGetOwnEnumerableForAssign(realm, from, key, out var enumerable))
                        continue;
                    if (!enumerable)
                        continue;

                    CopyValueForAssignKey(realm, from, to, key);
                }

                return true;
            }
            finally
            {
                if (proxyTarget is not null && !ReferenceEquals(keys, trapKeys))
                    realm.ReturnScratchList(keys);
            }
        }

        static void CollectOwnPropertyKeysForProxyFallback(JsRealm realm, JsObject source, List<JsValue> keysOut,
            bool includeSymbols)
        {
            var elementIndices = realm.RentScratchList<uint>(8);
            var seenStringAtoms = realm.RentScratchHashSet<int>();
            var namedAtoms = realm.RentScratchList<int>(8);
            var symbolAtoms = includeSymbols ? realm.RentScratchList<int>(4) : null;
            try
            {
                source.CollectOwnElementIndices(elementIndices, false);
                if (elementIndices.Count != 0)
                    elementIndices.Sort();
                for (var i = 0; i < elementIndices.Count; i++)
                    keysOut.Add(JsValue.FromString(elementIndices[i].ToString(CultureInfo.InvariantCulture)));

                source.CollectOwnNamedPropertyAtoms(realm, namedAtoms, false);
                for (var i = 0; i < namedAtoms.Count; i++)
                {
                    var atom = namedAtoms[i];
                    if (atom < 0)
                    {
                        if (includeSymbols)
                            symbolAtoms!.Add(atom);
                        continue;
                    }

                    if (seenStringAtoms.Add(atom))
                        keysOut.Add(JsValue.FromString(realm.Atoms.AtomToString(atom)));
                }

                if (source is JsGlobalObject globalSource)
                    foreach (var pair in globalSource.EnumerateNamedGlobalDescriptors())
                    {
                        var atom = pair.Key;
                        if (atom < 0)
                        {
                            if (includeSymbols)
                                symbolAtoms!.Add(atom);
                            continue;
                        }

                        if (seenStringAtoms.Add(atom))
                            keysOut.Add(JsValue.FromString(realm.Atoms.AtomToString(atom)));
                    }

                if (!includeSymbols || symbolAtoms is null || symbolAtoms.Count == 0)
                    return;

                for (var i = 0; i < symbolAtoms.Count; i++)
                {
                    var sym = realm.Atoms.TryGetSymbolByAtom(symbolAtoms[i], out var existing)
                        ? existing
                        : new(symbolAtoms[i], realm.Atoms.AtomToString(symbolAtoms[i]));
                    keysOut.Add(JsValue.FromSymbol(sym));
                }
            }
            finally
            {
                if (symbolAtoms is not null)
                    realm.ReturnScratchList(symbolAtoms);
                realm.ReturnScratchList(namedAtoms);
                realm.ReturnScratchHashSet(seenStringAtoms);
                realm.ReturnScratchList(elementIndices);
            }
        }

        static bool TryGetOwnEnumerableForAssign(JsRealm realm, JsObject source, in JsValue key,
            out bool enumerable)
        {
            return TryGetOwnEnumerableCore(realm, source, key, out enumerable);
        }

        static void CopyValueForAssignKey(JsRealm realm, JsObject from, JsObject to, in JsValue key)
        {
            var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);
            if (isIndex)
            {
                _ = from.TryGetElement(index, out var indexValue);
                if (!to.TrySetElement(index, indexValue))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to read only property");
                return;
            }

            if (!from.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                return;
            if (!to.TrySetPropertyAtom(realm, atom, propValue, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot assign to read only property");
        }

        var entriesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var from))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.entries target must not be null or undefined");

            var keys = realm.RentScratchList<JsValue>(8);
            try
            {
                CollectOwnPropertyKeysForDefineProperties(realm, from, keys);

                var result = realm.CreateArrayObject();
                uint outIndex = 0;
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (!key.IsString)
                        continue;

                    if (!TryGetOwnEnumerableForAssign(realm, from, key, out var enumerable) || !enumerable)
                        continue;

                    var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);

                    JsValue value;
                    if (isIndex)
                        _ = from.TryGetElement(index, out value);
                    else
                        _ = from.TryGetPropertyAtom(realm, atom, out value, out _);

                    var pair = realm.CreateArrayObject();
                    FreshArrayOperations.DefineElement(pair, 0, key);
                    FreshArrayOperations.DefineElement(pair, 1, value);
                    FreshArrayOperations.DefineElement(result, outIndex++, JsValue.FromObject(pair));
                }

                return result;
            }
            finally
            {
                realm.ReturnScratchList(keys);
            }
        }, "entries", 1);
        var fromEntriesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var iterable))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.fromEntries iterable must not be null or undefined");

            var result = new JsPlainObject(realm);
            if (iterable is JsArray array)
            {
                for (uint i = 0; i < array.Length; i++)
                {
                    if (!array.TryGetElement(i, out var entryValue))
                        continue;
                    AddEntryToObjectFromEntries(realm, result, entryValue);
                }

                return result;
            }

            var iterator = GetIteratorObjectForFromEntries(realm, iterable);
            try
            {
                while (true)
                {
                    var nextResult = StepIteratorForFromEntries(realm, iterator, out var done);
                    if (done)
                        break;

                    AddEntryToObjectFromEntries(realm, result, nextResult);
                }
            }
            catch
            {
                realm.BestEffortIteratorCloseOnThrow(iterator);
                throw;
            }

            return result;
        }, "fromEntries", 1);
        var getOwnPropertyDescriptorsFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.getOwnPropertyDescriptors target must not be null or undefined");

            var outObj = new JsPlainObject(realm);
            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var descriptor = GetOwnPropertyDescriptorByKey(realm, target, key);
                if (descriptor.IsUndefined)
                    continue;

                if (key.IsSymbol)
                {
                    outObj.DefineDataPropertyAtom(realm, key.AsSymbol().Atom, descriptor, JsShapePropertyFlags.Open);
                    continue;
                }

                if (realm.TryResolvePropertyKey(key, out var index, out var atom))
                    outObj.SetElement(index, descriptor);
                else
                    outObj.DefineDataPropertyAtom(realm, atom, descriptor, JsShapePropertyFlags.Open);
            }

            return outObj;
        }, "getOwnPropertyDescriptors", 1);
        var getOwnPropertySymbolsFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 1 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.getOwnPropertySymbols target must not be null or undefined");

            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            var result = realm.CreateArrayObject();
            uint outIndex = 0;
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key.IsSymbol)
                    FreshArrayOperations.DefineElement(result, outIndex++, key);
            }

            return result;
        }, "getOwnPropertySymbols", 1);
        var groupByFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var items))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.groupBy items must not be null or undefined");

            if (args.Length < 2 || !args[1].TryGetObject(out var callbackObj) ||
                callbackObj is not JsFunction callbackFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.groupBy callback must be a function");

            var result = new JsPlainObject(realm, false) { Prototype = null };

            if (items is JsArray array)
            {
                for (uint i = 0; i < array.Length; i++)
                {
                    var value = array.TryGetElement(i, out var existingValue) ? existingValue : JsValue.Undefined;
                    AppendObjectGroupByValueForCallback(realm, result, callbackFn, value, i);
                }
            }
            else if (items is JsStringObject stringObject)
            {
                long index = 0;
                foreach (var codePoint in stringObject.Value.EnumerateRunes())
                {
                    AppendObjectGroupByValueForCallback(realm, result, callbackFn,
                        JsValue.FromString(codePoint.ToString()), index);
                    index++;
                }
            }
            else
            {
                var iterator = GetIteratorObjectForObjectGroupBy(realm, items);
                long index = 0;
                while (true)
                {
                    var nextValue = StepIteratorForObjectGroupBy(realm, iterator, out var done);
                    if (done)
                        break;

                    AppendObjectGroupByValueForCallback(realm, result, callbackFn, nextValue, index);
                    index++;
                }
            }

            return result;
        }, "groupBy", 2);
        var hasOwnFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.hasOwn target must not be null or undefined");

            var key = JsRealm.NormalizePropertyKey(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            return GetOwnPropertyDescriptorByKey(realm, target, key).IsUndefined ? JsValue.False : JsValue.True;
        }, "hasOwn", 2);
        var isFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            if (args.Length < 2) return args.Length == 0 || args[0].IsUndefined ? JsValue.True : JsValue.False;

            var x = args[0];
            var y = args[1];
            return JsValue.SameValue(x, y) ? JsValue.True : JsValue.False;
        }, "is", 2);
        var valuesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var from))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.values target must not be null or undefined");

            var keys = Realm.RentScratchList<JsValue>(8);
            try
            {
                CollectOwnPropertyKeysForDefineProperties(realm, from, keys);

                var result = realm.CreateArrayObject();
                uint outIndex = 0;
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (!key.IsString)
                        continue;

                    if (!TryGetOwnEnumerableForAssign(realm, from, key, out var enumerable) || !enumerable)
                        continue;

                    var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);
                    JsValue value;
                    if (isIndex)
                        _ = from.TryGetElement(index, out value);
                    else
                        _ = from.TryGetPropertyAtom(realm, atom, out value, out _);

                    FreshArrayOperations.DefineElement(result, outIndex++, value);
                }

                return result;
            }
            finally
            {
                Realm.ReturnScratchList(keys);
            }
        }, "values", 1);

        static JsValue CreateDescriptorObject(JsRealm realm, in PropertyDescriptor descriptor)
        {
            var result = new JsPlainObject(realm);
            if (descriptor.IsAccessor)
            {
                result.DefineDataPropertyAtom(realm, IdGet,
                    descriptor.Getter ?? JsValue.Undefined,
                    JsShapePropertyFlags.Open);
                result.DefineDataPropertyAtom(realm, IdSet,
                    descriptor.Setter ?? JsValue.Undefined,
                    JsShapePropertyFlags.Open);
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

        static JsObject GetIteratorObjectForFromEntries(JsRealm realm, JsObject iterable)
        {
            return realm.GetIteratorObjectForIterable(iterable,
                "Object.fromEntries value is not iterable",
                "Object.fromEntries iterator result must be object");
        }

        static JsValue StepIteratorForFromEntries(JsRealm realm, JsObject iterator, out bool done)
        {
            return JsRealm.StepIteratorForIterable(realm, iterator,
                "Object.fromEntries iterator.next is not a function",
                "Object.fromEntries iterator result must be object",
                out done);
        }

        static JsObject GetIteratorObjectForObjectGroupBy(JsRealm realm, JsObject iterable)
        {
            return realm.GetIteratorObjectForIterable(iterable,
                "Object.groupBy value is not iterable",
                "Object.groupBy iterator result must be object");
        }

        static JsValue StepIteratorForObjectGroupBy(JsRealm realm, JsObject iterator, out bool done)
        {
            return JsRealm.StepIteratorForIterable(realm, iterator,
                "Object.groupBy iterator.next is not a function",
                "Object.groupBy iterator result must be object",
                out done);
        }

        static JsValue NormalizeObjectGroupByKey(JsRealm realm, in JsValue key)
        {
            if (key.IsSymbol || key.IsString || key.IsNumber)
                return key;

            var primitive = realm.ToPrimitiveSlowPath(key, true);
            if (primitive.IsSymbol || primitive.IsString || primitive.IsNumber)
                return primitive;

            return realm.ToJsStringSlowPath(primitive);
        }

        static void AppendObjectGroupByValueForCallback(
            JsRealm realm,
            JsPlainObject result,
            JsFunction callbackFn,
            in JsValue value,
            long index)
        {
            var callbackArgs = new InlineJsValueArray2
            {
                Item0 = value,
                Item1 = index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index)
            };
            var rawKey = realm.InvokeFunction(callbackFn, JsValue.Undefined, callbackArgs.AsSpan());
            var normalizedKey = NormalizeObjectGroupByKey(realm, rawKey);
            AppendObjectGroupByValue(realm, result, normalizedKey, value);
        }

        static void AppendObjectGroupByValue(JsRealm realm, JsPlainObject result, in JsValue key, in JsValue value)
        {
            if (realm.TryResolvePropertyKey(key, out var index, out var atom))
            {
                if (result.TryGetElement(index, out var existingIndexGroup))
                {
                    if (!existingIndexGroup.TryGetObject(out var existingIndexGroupObj) ||
                        existingIndexGroupObj is not JsArray existingIndexArray)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Object.groupBy internal group must be an array");
                    existingIndexArray.SetElement(existingIndexArray.Length, value);
                    return;
                }

                var groupArray = realm.CreateArrayObject();
                groupArray.SetElement(0, value);
                result.SetElement(index, JsValue.FromObject(groupArray));
                return;
            }

            if (result.TryGetPropertyAtom(realm, atom, out var existingGroup, out _))
            {
                if (!existingGroup.TryGetObject(out var existingGroupObj) ||
                    existingGroupObj is not JsArray existingArray)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.groupBy internal group must be an array");
                existingArray.SetElement(existingArray.Length, value);
                return;
            }

            var newGroupArray = realm.CreateArrayObject();
            newGroupArray.SetElement(0, value);
            result.DefineDataPropertyAtom(realm, atom, JsValue.FromObject(newGroupArray), JsShapePropertyFlags.Open);
        }

        static void AddEntryToObjectFromEntries(JsRealm realm, JsPlainObject target, in JsValue entryValue)
        {
            if (!entryValue.TryGetObject(out var entry))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.fromEntries iterator value must be an object");

            var key = entry.TryGetElement(0, out var keyValue) ? keyValue : JsValue.Undefined;
            var value = entry.TryGetElement(1, out var valueValue) ? valueValue : JsValue.Undefined;
            key = JsRealm.NormalizePropertyKey(realm, key);
            if (realm.TryResolvePropertyKey(key, out var index, out var atom))
                target.SetElement(index, value);
            else
                target.DefineDataPropertyAtom(realm, atom, value, JsShapePropertyFlags.Open);
        }

        static bool QueryIsExtensibleForIntegrityLevel(JsRealm realm, JsObject target)
        {
            return ProxyCore.QueryIsExtensible(realm, target);
        }

        static bool AreAllOwnPropertiesSealedForIntegrityLevel(JsRealm realm, JsObject target)
        {
            if (target is not IProxyObject)
                return target.AreAllOwnPropertiesSealed();

            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            var allSealed = true;
            for (var i = 0; i < keys.Count; i++)
            {
                var descriptor = GetOwnPropertyDescriptorByKey(realm, target, keys[i]);
                if (descriptor.IsUndefined)
                    continue;
                if (!descriptor.TryGetObject(out var descriptorObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.isSealed descriptor must be object");
                if (!descriptorObj.TryGetPropertyAtom(realm, IdConfigurable, out var configurableValue, out _) ||
                    DescriptorUtilities.ToBooleanForDescriptor(configurableValue))
                    allSealed = false;
            }

            return allSealed;
        }

        static bool AreAllOwnPropertiesFrozenForIntegrityLevel(JsRealm realm, JsObject target)
        {
            if (target is not IProxyObject)
                return target.AreAllOwnPropertiesFrozen();

            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            var allFrozen = true;
            for (var i = 0; i < keys.Count; i++)
            {
                var descriptor = GetOwnPropertyDescriptorByKey(realm, target, keys[i]);
                if (descriptor.IsUndefined)
                    continue;
                if (!descriptor.TryGetObject(out var descriptorObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.isFrozen descriptor must be object");
                if (!descriptorObj.TryGetPropertyAtom(realm, IdConfigurable, out var configurableValue, out _) ||
                    DescriptorUtilities.ToBooleanForDescriptor(configurableValue))
                    allFrozen = false;

                var isAccessor = descriptorObj.TryGetPropertyAtom(realm, IdGet, out _, out _) ||
                                 descriptorObj.TryGetPropertyAtom(realm, IdSet, out _, out _);
                if (!isAccessor &&
                    descriptorObj.TryGetPropertyAtom(realm, IdWritable, out var writableValue, out _) &&
                    DescriptorUtilities.ToBooleanForDescriptor(writableValue))
                    allFrozen = false;
            }

            return allFrozen;
        }

        static void ApplyPropertyDescriptorsFromObject(JsRealm realm, JsObject target, JsObject props)
        {
            var keys = realm.RentScratchList<JsValue>(8);
            try
            {
                CollectOwnPropertyKeysForDefineProperties(realm, props, keys);
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (!TryGetOwnEnumerableForAssign(realm, props, key, out var enumerable))
                        continue;
                    if (!enumerable)
                        continue;

                    var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);

                    JsValue descriptorValue;
                    if (isIndex)
                    {
                        if (!props.TryGetElement(index, out descriptorValue))
                            continue;
                    }
                    else
                    {
                        if (!props.TryGetPropertyAtom(realm, atom, out descriptorValue, out _))
                            continue;
                    }

                    if (!descriptorValue.TryGetObject(out _) &&
                        props.TryGetOwnEnumerableDescriptorViaTrap(realm, key, out var hasDescriptorFromTrap, out _) &&
                        !hasDescriptorFromTrap)
                        continue;

                    DefinePropertyFromDescriptorValue(realm, target, key, descriptorValue);
                }
            }
            finally
            {
                realm.ReturnScratchList(keys);
            }
        }

        static void CollectOwnPropertyKeysForDefineProperties(JsRealm realm, JsObject props, List<JsValue> keys)
        {
            List<JsValue>? trapKeys = null;
            JsObject? proxyTarget = null;
            var isProxy = props.TryGetOwnKeysTrapKeys(realm, out trapKeys) ||
                          props.TryGetProxyTarget(out proxyTarget);

            if (isProxy)
            {
                if (trapKeys is not null)
                {
                    keys.AddRange(trapKeys);
                    return;
                }

                CollectOwnPropertyKeysForProxyFallback(realm, proxyTarget!, keys, true);
                return;
            }

            CollectOwnPropertyKeysForProxyFallback(realm, props, keys, true);
        }

        static void DefinePropertyFromDescriptorValue(JsRealm realm, JsObject target, in JsValue keyValue,
            in JsValue descriptorValue)
        {
            if (!descriptorValue.TryGetObject(out var descriptor))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Property description must be an object");

            var spec = ReadDescriptorSnapshot(realm, descriptor);

            if (target.DefinePropertyFromDescriptorObject(realm, keyValue,
                    BuildDescriptorObjectFromSnapshot(realm, spec)))
                return;

            if (target is JsTypedArrayObject typedArray)
            {
                var typedArrayDefineResult = TryDefineTypedArrayIntegerIndexedProperty(
                    realm, typedArray, keyValue, spec, out var typedArrayHandled);
                if (typedArrayHandled)
                {
                    if (!typedArrayDefineResult)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Cannot define typed array index property");
                    return;
                }
            }

            if (spec is { IsAccessorDescriptor: true, IsDataDescriptor: true })
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");

            if (realm.TryResolvePropertyKey(keyValue, out var index, out var atom))
            {
                ApplyIndexedPropertyDescriptorFromSnapshot(realm, target, index, spec);
                return;
            }

            if (target is JsModuleNamespaceObject moduleNamespace &&
                !TryDefineModuleNamespaceNamedPropertyFromSnapshot(realm, moduleNamespace, atom, spec))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Cannot redefine property: {realm.Atoms.AtomToString(atom)}");

            if (target is JsModuleNamespaceObject)
                return;

            ApplyNamedPropertyDescriptorFromSnapshot(realm, target, atom, spec);
        }

        static JsPlainObject BuildDescriptorObjectFromSnapshot(JsRealm realm, in DescriptorSnapshot spec)
        {
            var descriptorObject = new JsPlainObject(realm);
            if (spec.HasValue)
                descriptorObject.DefineDataPropertyAtom(realm, IdValue, spec.Value, JsShapePropertyFlags.Open);
            if (spec.HasWritable)
                descriptorObject.DefineDataPropertyAtom(realm, IdWritable, spec.WritableValue,
                    JsShapePropertyFlags.Open);
            if (spec.HasEnumerable)
                descriptorObject.DefineDataPropertyAtom(realm, IdEnumerable, spec.EnumerableValue,
                    JsShapePropertyFlags.Open);
            if (spec.HasConfigurable)
                descriptorObject.DefineDataPropertyAtom(realm, IdConfigurable, spec.ConfigurableValue,
                    JsShapePropertyFlags.Open);
            if (spec.HasGet)
                descriptorObject.DefineDataPropertyAtom(realm, IdGet, spec.GetValue, JsShapePropertyFlags.Open);
            if (spec.HasSet)
                descriptorObject.DefineDataPropertyAtom(realm, IdSet, spec.SetValue, JsShapePropertyFlags.Open);
            return descriptorObject;
        }

        static bool TryDefineModuleNamespaceNamedPropertyFromSnapshot(
            JsRealm realm,
            JsModuleNamespaceObject target,
            int atom,
            in DescriptorSnapshot spec)
        {
            if (atom == IdSymbolToStringTag)
            {
                if (!target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var existing))
                    return false;

                if (spec.IsAccessorDescriptor)
                    return false;

                var requestedEnumerable = spec.HasEnumerable
                    ? DescriptorUtilities.ToBooleanForDescriptor(spec.EnumerableValue)
                    : existing.Enumerable;
                var requestedConfigurable = spec.HasConfigurable
                    ? DescriptorUtilities.ToBooleanForDescriptor(spec.ConfigurableValue)
                    : existing.Configurable;
                var requestedWritable = spec.HasWritable
                    ? DescriptorUtilities.ToBooleanForDescriptor(spec.WritableValue)
                    : existing.Writable;
                var requestedValue = spec.HasValue ? spec.Value : existing.Value;

                if (requestedEnumerable != existing.Enumerable || requestedConfigurable != existing.Configurable)
                    return false;
                if (requestedWritable && !existing.Writable)
                    return false;
                if (spec.HasValue && !JsValue.SameValue(existing.Value, requestedValue))
                    return false;
                return true;
            }

            if (atom < 0)
                return false;
            if (!target.TryGetOwnPropertySlotInfoAtom(atom, out _))
                return false;
            if (spec.IsAccessorDescriptor)
                return false;
            if (spec.HasConfigurable && DescriptorUtilities.ToBooleanForDescriptor(spec.ConfigurableValue))
                return false;
            if (spec.HasEnumerable && !DescriptorUtilities.ToBooleanForDescriptor(spec.EnumerableValue))
                return false;
            if (spec.HasWritable && !DescriptorUtilities.ToBooleanForDescriptor(spec.WritableValue))
                return false;
            if (spec.HasValue)
            {
                JsValue currentValue;
                try
                {
                    if (!target.TryGetPropertyAtom(realm, atom, out currentValue, out _))
                        return false;
                }
                catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.ReferenceError)
                {
                    return false;
                }

                if (!JsValue.SameValue(currentValue, spec.Value))
                    return false;
            }

            return true;
        }

        static void ApplyIndexedPropertyDescriptorFromSnapshot(
            JsRealm realm, JsObject target, uint index, in DescriptorSnapshot spec)
        {
            var hasExistingElement = target.TryGetOwnElementDescriptor(index, out var existingElement);
            if (!target.IsExtensible && !hasExistingElement)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot define property on non-extensible object");
            if (target is JsArray arrayTargetForIndex && !hasExistingElement &&
                !arrayTargetForIndex.CanDefineElementAtIndex(index))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Cannot define property {index}, object is not extensible");

            var defineAccessor = spec.IsAccessorDescriptor ||
                                 (spec.IsGenericDescriptor && hasExistingElement && existingElement.IsAccessor);
            var requestedEnumerable = DescriptorUtilities.IsRequestedTrue(spec.HasEnumerable, spec.EnumerableValue);
            var requestedConfigurable =
                DescriptorUtilities.IsRequestedTrue(spec.HasConfigurable, spec.ConfigurableValue);

            if (hasExistingElement && !existingElement.Configurable)
            {
                if (spec.HasConfigurable && requestedConfigurable)
                    ThrowCannotRedefineIndex(index);
                if (spec.HasEnumerable && requestedEnumerable != existingElement.Enumerable)
                    ThrowCannotRedefineIndex(index);
                if (defineAccessor != existingElement.IsAccessor)
                    ThrowCannotRedefineIndex(index);
            }

            if (defineAccessor)
            {
                var existingGetter =
                    hasExistingElement && existingElement.IsAccessor ? existingElement.Getter : null;
                var existingSetter =
                    hasExistingElement && existingElement.IsAccessor ? existingElement.Setter : null;
                var getter = existingGetter;
                var setter = existingSetter;
                var includeGetter = hasExistingElement && existingElement is { IsAccessor: true, HasGetter: true };
                var includeSetter = hasExistingElement && existingElement is { IsAccessor: true, HasSetter: true };

                if (spec.HasGet)
                {
                    includeGetter = true;
                    getter = ToDescriptorAccessorFunction(spec.GetValue, "Getter must be a function or undefined");
                }

                if (spec.HasSet)
                {
                    includeSetter = true;
                    setter = ToDescriptorAccessorFunction(spec.SetValue, "Setter must be a function or undefined");
                }

                if (hasExistingElement && existingElement is { IsAccessor: true, Configurable: false })
                {
                    if (spec.HasGet && !SameValueAccessorFunction(existingGetter, getter))
                        ThrowCannotRedefineIndex(index);
                    if (spec.HasSet && !SameValueAccessorFunction(existingSetter, setter))
                        ThrowCannotRedefineIndex(index);
                }

                var finalEnumerable = spec.HasEnumerable
                    ? requestedEnumerable
                    : hasExistingElement && existingElement.Enumerable;
                var finalConfigurable = spec.HasConfigurable
                    ? requestedConfigurable
                    : hasExistingElement && existingElement.Configurable;
                var flags = DescriptorUtilities.BuildAccessorFlags(finalEnumerable, finalConfigurable,
                    includeGetter, includeSetter);

                var descriptor = includeGetter && includeSetter
                    ? new(getter ?? JsValue.Undefined, setter, flags)
                    : includeGetter
                        ? new(getter ?? JsValue.Undefined, null, flags)
                        : includeSetter
                            ? new(JsValue.Undefined, setter, flags)
                            : PropertyDescriptor.Data(JsValue.Undefined, false, finalEnumerable,
                                finalConfigurable);
                target.DefineElementDescriptor(index, descriptor);
                return;
            }

            var finalWritable = spec.HasWritable
                ? DescriptorUtilities.ToBooleanForDescriptor(spec.WritableValue)
                : hasExistingElement && existingElement is { IsAccessor: false, Writable: true };
            var finalValue = spec.HasValue
                ? spec.Value
                : hasExistingElement && !existingElement.IsAccessor
                    ? existingElement.Value
                    : JsValue.Undefined;
            var finalEnumerableData = spec.HasEnumerable
                ? requestedEnumerable
                : hasExistingElement && existingElement.Enumerable;
            var finalConfigurableData = spec.HasConfigurable
                ? requestedConfigurable
                : hasExistingElement && existingElement.Configurable;

            if (hasExistingElement && existingElement is { IsAccessor: false, Configurable: false, Writable: false })
            {
                if (spec.HasWritable && finalWritable)
                    ThrowCannotRedefineIndex(index);
                if (spec.HasValue && !JsValue.SameValue(existingElement.Value, finalValue))
                    ThrowCannotRedefineIndex(index);
            }

            target.DefineElementDescriptor(index,
                PropertyDescriptor.Data(finalValue, finalWritable, finalEnumerableData, finalConfigurableData));
        }

        static void ApplyNamedPropertyDescriptorFromSnapshot(
            JsRealm realm, JsObject target, int atom, in DescriptorSnapshot spec)
        {
            if (!target.IsExtensible && !target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot define property on non-extensible object");

            var requestedEnumerable = DescriptorUtilities.IsRequestedTrue(spec.HasEnumerable, spec.EnumerableValue);
            var requestedConfigurable =
                DescriptorUtilities.IsRequestedTrue(spec.HasConfigurable, spec.ConfigurableValue);

            if (target is JsGlobalObject globalTarget &&
                globalTarget.TryGetNamedGlobalDescriptorAtom(atom, out var existingGlobalDescriptor))
            {
                var globalDescriptor = BuildGlobalOwnDescriptor(
                    realm, atom, existingGlobalDescriptor,
                    spec.HasValue, spec.Value, spec.HasWritable, spec.WritableValue,
                    spec.HasEnumerable, spec.EnumerableValue, spec.HasConfigurable, spec.ConfigurableValue,
                    spec.IsAccessorDescriptor, spec.HasGet, spec.GetValue, spec.HasSet, spec.SetValue);
                if (!globalTarget.TrySetOwnGlobalDescriptorAtom(atom, globalDescriptor))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot redefine property");
                return;
            }

            if (target is JsArray arrayTarget && atom == IdLength)
            {
                if (spec.IsAccessorDescriptor)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid property descriptor for length");
                var requested = DescriptorUtilities.ReadRequestedBooleans(
                    spec.HasWritable, spec.WritableValue,
                    spec.HasEnumerable, spec.EnumerableValue,
                    spec.HasConfigurable, spec.ConfigurableValue);
                if (!arrayTarget.TryDefineLengthDescriptor(
                        spec.HasValue, spec.Value,
                        spec.HasWritable, requested.Writable,
                        spec.HasEnumerable, requested.Enumerable,
                        spec.HasConfigurable, requested.Configurable,
                        out var isRangeError))
                {
                    if (isRangeError)
                        throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length");
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot redefine property: length");
                }

                return;
            }

            if (target is JsArgumentsObject argumentsTarget && atom == IdLength)
            {
                if (spec.IsAccessorDescriptor)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid property descriptor for length");
                var requested = DescriptorUtilities.ReadRequestedBooleans(
                    spec.HasWritable, spec.WritableValue,
                    spec.HasEnumerable, spec.EnumerableValue,
                    spec.HasConfigurable, spec.ConfigurableValue);
                if (!argumentsTarget.TryDefineLengthDescriptor(
                        spec.HasValue, spec.Value,
                        spec.HasWritable, requested.Writable,
                        spec.HasEnumerable, requested.Enumerable,
                        spec.HasConfigurable, requested.Configurable,
                        out var isRangeError))
                {
                    if (isRangeError)
                        throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid length");
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot redefine property: length");
                }

                return;
            }

            var defineAccessor = spec.IsAccessorDescriptor;
            var isGenericDescriptor = spec.IsGenericDescriptor;
            var existingHasOwnNamed =
                target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var existingDescriptor);
            var existingIsAccessor = false;
            var existingWritable = false;
            var existingEnumerable = false;
            var existingConfigurable = false;
            var existingValue = JsValue.Undefined;
            JsFunction? existingGetter = null;
            JsFunction? existingSetter = null;
            var existingHasGetter = false;
            var existingHasSetter = false;

            if (existingHasOwnNamed)
            {
                existingIsAccessor = existingDescriptor.IsAccessor;
                existingEnumerable = existingDescriptor.Enumerable;
                existingConfigurable = existingDescriptor.Configurable;
                if (existingIsAccessor)
                {
                    existingHasGetter = existingDescriptor.HasGetter;
                    existingHasSetter = existingDescriptor.HasSetter;
                    existingGetter = existingDescriptor.Getter;
                    existingSetter = existingDescriptor.Setter;
                }
                else
                {
                    existingWritable = existingDescriptor.Writable;
                    existingValue = existingDescriptor.Value;
                }

                if (isGenericDescriptor)
                    defineAccessor = existingIsAccessor;

                if (!existingConfigurable)
                {
                    if (spec.HasConfigurable && requestedConfigurable)
                        ThrowCannotRedefine(realm, atom);
                    if (spec.HasEnumerable && requestedEnumerable != existingEnumerable)
                        ThrowCannotRedefine(realm, atom);
                    if (defineAccessor != existingIsAccessor)
                        ThrowCannotRedefine(realm, atom);
                }
            }

            if (defineAccessor)
            {
                var getter = existingHasOwnNamed && existingIsAccessor ? existingGetter : null;
                var setter = existingHasOwnNamed && existingIsAccessor ? existingSetter : null;
                var includeGetter = existingHasOwnNamed && existingIsAccessor && existingHasGetter;
                var includeSetter = existingHasOwnNamed && existingIsAccessor && existingHasSetter;
                if (spec.HasGet)
                {
                    includeGetter = true;
                    getter = ToDescriptorAccessorFunction(spec.GetValue, "Getter must be a function or undefined");
                }

                if (spec.HasSet)
                {
                    includeSetter = true;
                    setter = ToDescriptorAccessorFunction(spec.SetValue, "Setter must be a function or undefined");
                }

                if (existingHasOwnNamed && existingIsAccessor && !existingConfigurable)
                {
                    if (spec.HasGet && !SameValueAccessorFunction(existingGetter, getter))
                        ThrowCannotRedefine(realm, atom);
                    if (spec.HasSet && !SameValueAccessorFunction(existingSetter, setter))
                        ThrowCannotRedefine(realm, atom);
                }

                var finalEnumerable =
                    spec.HasEnumerable ? requestedEnumerable : existingHasOwnNamed && existingEnumerable;
                var finalConfigurable = spec.HasConfigurable
                    ? requestedConfigurable
                    : existingHasOwnNamed && existingConfigurable;
                var flags = DescriptorUtilities.BuildAccessorFlags(finalEnumerable, finalConfigurable, includeGetter,
                    includeSetter);
                if (!target.DefineOwnAccessorPropertyExact(realm, atom, getter, setter, flags))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        $"Cannot redefine property: {realm.Atoms.AtomToString(atom)}");
                return;
            }

            var finalWritable = spec.HasWritable
                ? DescriptorUtilities.ToBooleanForDescriptor(spec.WritableValue)
                : existingHasOwnNamed && !existingIsAccessor && existingWritable;
            var finalValue = spec.HasValue
                ? spec.Value
                : existingHasOwnNamed && !existingIsAccessor
                    ? existingValue
                    : JsValue.Undefined;
            var finalEnumerableData =
                spec.HasEnumerable ? requestedEnumerable : existingHasOwnNamed && existingEnumerable;
            var finalConfigurableData = spec.HasConfigurable
                ? requestedConfigurable
                : existingHasOwnNamed && existingConfigurable;

            if (existingHasOwnNamed && !existingIsAccessor && !existingConfigurable && !existingWritable)
            {
                if (spec.HasWritable && finalWritable)
                    ThrowCannotRedefine(realm, atom);
                if (spec.HasValue && !JsValue.SameValue(existingValue, finalValue))
                    ThrowCannotRedefine(realm, atom);
            }

            var dataFlags =
                DescriptorUtilities.BuildDataFlags(finalWritable, finalEnumerableData, finalConfigurableData);
            if (!target.DefineOwnDataPropertyExact(realm, atom, finalValue, dataFlags))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"Cannot redefine property: {realm.Atoms.AtomToString(atom)}");
        }

        static DescriptorSnapshot ReadDescriptorSnapshot(JsRealm realm, JsObject descriptor)
        {
            var hasValue = descriptor.TryGetPropertyAtom(realm, IdValue, out var value, out _);
            var hasWritable = descriptor.TryGetPropertyAtom(realm, IdWritable, out var writableValue, out _);
            var hasEnumerable =
                descriptor.TryGetPropertyAtom(realm, IdEnumerable, out var enumerableValue, out _);
            var hasConfigurable =
                descriptor.TryGetPropertyAtom(realm, IdConfigurable, out var configurableValue, out _);
            var hasGet = descriptor.TryGetPropertyAtom(realm, IdGet, out var getValue, out _);
            var hasSet = descriptor.TryGetPropertyAtom(realm, IdSet, out var setValue, out _);
            return new(
                hasValue, value,
                hasWritable, writableValue,
                hasEnumerable, enumerableValue,
                hasConfigurable, configurableValue,
                hasGet, getValue,
                hasSet, setValue);
        }

        static JsFunction? ToDescriptorAccessorFunction(in JsValue value, string errorMessage)
        {
            if (value.IsUndefined)
                return null;
            if (!value.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
                throw new JsRuntimeException(JsErrorKind.TypeError, errorMessage);
            return fn;
        }

        static PropertyDescriptor BuildGlobalOwnDescriptor(
            JsRealm realm,
            int atom,
            in PropertyDescriptor existing,
            bool hasValue, in JsValue value,
            bool hasWritable, in JsValue writableValue,
            bool hasEnumerable, in JsValue enumerableValue,
            bool hasConfigurable, in JsValue configurableValue,
            bool accessorDescriptor,
            bool hasGet, in JsValue getValue,
            bool hasSet, in JsValue setValue)
        {
            var requestedEnumerable = hasEnumerable
                ? DescriptorUtilities.ToBooleanForDescriptor(enumerableValue)
                : existing.Enumerable;
            var requestedConfigurable =
                hasConfigurable ? DescriptorUtilities.ToBooleanForDescriptor(configurableValue) : existing.Configurable;

            if (!existing.Configurable)
            {
                if (requestedConfigurable)
                    ThrowCannotRedefine(realm, atom);
                if (requestedEnumerable != existing.Enumerable)
                    ThrowCannotRedefine(realm, atom);
                if (existing.IsAccessor != accessorDescriptor)
                    ThrowCannotRedefine(realm, atom);
            }

            if (accessorDescriptor)
            {
                var existingGetter = existing.Getter;
                var existingSetter = existing.Setter;
                var requestedGetter = existingGetter;
                var requestedSetter = existingSetter;
                var includeGetter = existing.HasGetter;
                var includeSetter = existing.HasSetter;

                if (hasGet)
                {
                    includeGetter = true;
                    if (getValue.IsUndefined)
                    {
                        requestedGetter = null;
                    }
                    else
                    {
                        if (!getValue.TryGetObject(out var getterObj) || getterObj is not JsFunction getterFn)
                            throw new JsRuntimeException(JsErrorKind.TypeError,
                                "Getter must be a function or undefined");
                        requestedGetter = getterFn;
                    }
                }

                if (hasSet)
                {
                    includeSetter = true;
                    if (setValue.IsUndefined)
                    {
                        requestedSetter = null;
                    }
                    else
                    {
                        if (!setValue.TryGetObject(out var setterObj) || setterObj is not JsFunction setterFn)
                            throw new JsRuntimeException(JsErrorKind.TypeError,
                                "Setter must be a function or undefined");
                        requestedSetter = setterFn;
                    }
                }

                if (!existing.Configurable)
                {
                    if (hasGet && !SameValueAccessorFunction(existingGetter, requestedGetter))
                        ThrowCannotRedefine(realm, atom);
                    if (hasSet && !SameValueAccessorFunction(existingSetter, requestedSetter))
                        ThrowCannotRedefine(realm, atom);
                }

                var accessorFlags = DescriptorUtilities.BuildAccessorFlags(
                    requestedEnumerable,
                    requestedConfigurable,
                    includeGetter,
                    includeSetter);

                if (includeGetter && includeSetter)
                    return new(
                        requestedGetter ?? JsValue.Undefined,
                        requestedSetter,
                        accessorFlags);
                if (includeGetter)
                    return new(
                        requestedGetter ?? JsValue.Undefined,
                        null,
                        accessorFlags);
                if (includeSetter)
                    return new(
                        JsValue.Undefined,
                        requestedSetter,
                        accessorFlags);

                return PropertyDescriptor.Data(JsValue.Undefined, false, requestedEnumerable,
                    requestedConfigurable);
            }

            var requestedWritable =
                hasWritable ? DescriptorUtilities.ToBooleanForDescriptor(writableValue) : existing.Writable;
            var requestedValue = hasValue ? value : existing.Value;

            if (existing is { IsAccessor: false, Configurable: false, Writable: false })
            {
                if (requestedWritable)
                    ThrowCannotRedefine(realm, atom);
                if (hasValue && !JsValue.SameValue(existing.Value, requestedValue))
                    ThrowCannotRedefine(realm, atom);
            }

            return PropertyDescriptor.Data(requestedValue, requestedWritable, requestedEnumerable,
                requestedConfigurable);
        }

        static bool SameValueAccessorFunction(JsFunction? a, JsFunction? b)
        {
            if (a is null && b is null)
                return true;
            if (a is null || b is null)
                return false;
            return ReferenceEquals(a, b);
        }

        static void ThrowCannotRedefine(JsRealm realm, int atom)
        {
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Cannot redefine property: {realm.Atoms.AtomToString(atom)}",
                "DEFINE_PROPERTY_REDEFINE");
        }

        static void ThrowCannotRedefineIndex(uint index)
        {
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                $"Cannot redefine property: {index}",
                "DEFINE_PROPERTY_REDEFINE");
        }

        static JsValue GetOwnPropertyDescriptorByAtom(JsRealm realm, JsObject target, int atom)
        {
            if (target is JsModuleNamespaceObject moduleNs)
            {
                if (!moduleNs.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var moduleDescriptor))
                    return JsValue.Undefined;

                return realm.Intrinsics.CreateOwnDataDescriptorObject(
                    moduleDescriptor.Value,
                    moduleDescriptor.Writable ? JsValue.True : JsValue.False,
                    moduleDescriptor.Enumerable ? JsValue.True : JsValue.False,
                    moduleDescriptor.Configurable ? JsValue.True : JsValue.False);
            }

            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
            {
                var enumerable = descriptor.Enumerable;
                var configurable = descriptor.Configurable;

                if (descriptor.IsAccessor)
                {
                    var getterValue = descriptor.HasGetter && descriptor.Getter is not null
                        ? JsValue.FromObject(descriptor.Getter)
                        : JsValue.Undefined;
                    var setterValue = descriptor.HasSetter && descriptor.Setter is not null
                        ? JsValue.FromObject(descriptor.Setter)
                        : JsValue.Undefined;

                    return realm.Intrinsics.CreateOwnAccessorDescriptorObject(
                        getterValue,
                        setterValue,
                        new(enumerable),
                        new(configurable));
                }

                return realm.Intrinsics.CreateOwnDataDescriptorObject(
                    descriptor.Value,
                    new(descriptor.Writable),
                    new(enumerable),
                    new(configurable));
            }

            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var virtualDescriptor))
                return realm.Intrinsics.CreateOwnDataDescriptorObject(
                    virtualDescriptor.Value,
                    new(virtualDescriptor.Writable),
                    new(virtualDescriptor.Enumerable),
                    new(virtualDescriptor.Configurable));

            if (target is JsGlobalObject globalTarget &&
                globalTarget.TryGetOwnGlobalDescriptorAtom(atom, out var globalDescriptor))
                return realm.Intrinsics.CreateOwnDataDescriptorObject(
                    globalDescriptor.Value,
                    new(globalDescriptor.Writable),
                    new(globalDescriptor.Enumerable),
                    new(globalDescriptor.Configurable));

            return JsValue.Undefined;
        }

        static JsValue GetOwnPropertyDescriptorByKey(JsRealm realm, JsObject target, in JsValue key)
        {
            if (target.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var trapDescriptor))
                return trapDescriptor;

            var descriptorTarget = target.TryGetProxyTarget(out var proxyTarget) ? proxyTarget : target;
            if (descriptorTarget is JsTypedArrayObject typedArray &&
                TryGetTypedArrayOwnPropertyDescriptorByKey(realm, typedArray, key, out var typedArrayDescriptor,
                    out var typedArrayHandled))
                if (typedArrayHandled)
                    return typedArrayDescriptor;

            if (realm.TryResolvePropertyKey(key, out var index, out var atom))
            {
                if (descriptorTarget.TryGetOwnElementDescriptor(index, out var indexDescriptor))
                    return CreateDescriptorObject(realm, indexDescriptor);
                return JsValue.Undefined;
            }

            return GetOwnPropertyDescriptorByAtom(realm, descriptorTarget, atom);
        }

        var getOwnPropertyNamesFn = new JsHostFunction(Realm, "getOwnPropertyNames", 1,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                if (args.Length == 0 || !realm.TryToObject(args[0], out var target))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Object.getOwnPropertyNames target must not be null or undefined");

                var outArr = realm.CreateArrayObject();
                var keys = OwnKeysHelpers.CollectForProxy(realm, target);
                uint outIndex = 0;
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (key.IsString)
                        FreshArrayOperations.DefineElement(outArr, outIndex++, key);
                }

                return outArr;
            }, false);
        const int atomKeys = IdKeys;
        const int atomPreventExtensions = IdPreventExtensions;
        const int atomIsExtensible = IdIsExtensible;
        const int atomSeal = IdSeal;
        const int atomFreeze = IdFreeze;
        const int atomIsSealed = IdIsSealed;
        const int atomIsFrozen = IdIsFrozen;
        const int atomDefineProperty = IdDefineProperty;
        const int atomDefineProperties = IdDefineProperties;
        const int atomAssign = IdAssign;
        const int atomEntries = IdEntries;
        const int atomFromEntries = IdFromEntries;
        const int atomGetOwnPropertyDescriptors = IdGetOwnPropertyDescriptors;
        const int atomGetOwnPropertySymbols = IdGetOwnPropertySymbols;
        const int atomGroupBy = IdGroupBy;
        const int atomHasOwn = IdHasOwn;
        const int atomIs = IdIs;
        const int atomValues = IdValues;
        var keysFn = new JsHostFunction(Realm, "keys", 1, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Object.keys target must not be null or undefined");

            var keys = OwnKeysHelpers.CollectForProxy(realm, target);
            var outArr = realm.CreateArrayObject();
            uint outIndex = 0;
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!key.IsString)
                    continue;
                if (!TryGetOwnEnumerableForCopyDataProperties(realm, target, key, out var enumerable) || !enumerable)
                    continue;
                FreshArrayOperations.DefineElement(outArr, outIndex++, key);
            }

            return outArr;
        }, false);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdCreate, createFn),
            PropertyDefinition.Mutable(IdGetPrototypeOf, getPrototypeOfFn),
            PropertyDefinition.Mutable(IdSetPrototypeOf, setPrototypeOfFn),
            PropertyDefinition.Mutable(IdGetOwnPropertyDescriptor,
                getOwnPropertyDescriptorFn),
            PropertyDefinition.Mutable(atomDefineProperty, definePropertyFn),
            PropertyDefinition.Mutable(atomDefineProperties, definePropertiesFn),
            PropertyDefinition.Mutable(atomAssign, assignFn),
            PropertyDefinition.Mutable(atomEntries, entriesFn),
            PropertyDefinition.Mutable(atomFromEntries, fromEntriesFn),
            PropertyDefinition.Mutable(atomGetOwnPropertyDescriptors, getOwnPropertyDescriptorsFn),
            PropertyDefinition.Mutable(atomGetOwnPropertySymbols, getOwnPropertySymbolsFn),
            PropertyDefinition.Mutable(atomGroupBy, groupByFn),
            PropertyDefinition.Mutable(atomHasOwn, hasOwnFn),
            PropertyDefinition.Mutable(atomIs, isFn),
            PropertyDefinition.Mutable(IdGetOwnPropertyNames, getOwnPropertyNamesFn),
            PropertyDefinition.Mutable(atomPreventExtensions, preventExtensionsFn),
            PropertyDefinition.Mutable(atomIsExtensible, isExtensibleFn),
            PropertyDefinition.Mutable(atomSeal, sealFn),
            PropertyDefinition.Mutable(atomFreeze, freezeFn),
            PropertyDefinition.Mutable(atomIsSealed, isSealedFn),
            PropertyDefinition.Mutable(atomIsFrozen, isFrozenFn),
            PropertyDefinition.Mutable(atomValues, valuesFn),
            PropertyDefinition.Mutable(atomKeys, keysFn)
        ];
        ObjectConstructor.InitializePrototypeProperty(ObjectPrototype);
        ObjectConstructor.DefineNewPropertiesNoCollision(Realm, defs);
    }

    internal void CopyDataPropertiesOntoObject(JsObject target, in JsValue sourceValue)
    {
        if (sourceValue.IsNullOrUndefined)
            return;
        if (!Realm.TryToObject(sourceValue, out var source))
            return;

        if (TryCopyDataPropertiesFromProxySource(Realm, source, target))
            return;

        var elementIndices = Realm.RentScratchList<uint>(8);
        var enumerableNamedAtoms = Realm.RentScratchList<int>(8);
        try
        {
            if (source is JsStringObject stringSource)
            {
                for (uint key = 0; key < (uint)stringSource.Value.Length; key++)
                {
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }
            else if (source is JsArray arraySource)
            {
                for (uint key = 0; key < arraySource.Length; key++)
                {
                    if (!source.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                        continue;
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }
            else
            {
                elementIndices.Clear();
                source.CollectOwnElementIndices(elementIndices, false);
                if (elementIndices.Count != 0)
                    elementIndices.Sort();
                for (var i = 0; i < elementIndices.Count; i++)
                {
                    var key = elementIndices[i];
                    if (!source.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                        continue;
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }

            enumerableNamedAtoms.Clear();
            source.CollectOwnNamedPropertyAtoms(Realm, enumerableNamedAtoms, true);
            for (var i = 0; i < enumerableNamedAtoms.Count; i++)
            {
                var atom = enumerableNamedAtoms[i];
                if (atom < 0)
                    continue;
                if (!source.TryGetPropertyAtom(Realm, atom, out var propValue, out _))
                    continue;
                target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
            }

            for (var i = 0; i < enumerableNamedAtoms.Count; i++)
            {
                var atom = enumerableNamedAtoms[i];
                if (atom >= 0)
                    continue;
                if (!source.TryGetPropertyAtom(Realm, atom, out var propValue, out _))
                    continue;
                target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
            }

            if (source is JsGlobalObject globalSource)
                foreach (var pair in globalSource.EnumerateNamedGlobalDescriptors())
                {
                    var atom = pair.Key;
                    var desc = pair.Value;
                    if (!desc.Enumerable)
                        continue;
                    if (!source.TryGetPropertyAtom(Realm, atom, out var propValue, out _))
                        continue;
                    target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
                }
        }
        finally
        {
            Realm.ReturnScratchList(enumerableNamedAtoms);
            Realm.ReturnScratchList(elementIndices);
        }
    }

    internal void CopyDataPropertiesOntoObjectExcluding(
        JsObject target,
        in JsValue sourceValue,
        ReadOnlySpan<JsValue> excludedKeys)
    {
        var realm = Realm;
        if (sourceValue.IsNullOrUndefined)
            return;
        if (!Realm.TryToObject(sourceValue, out var source))
            return;

        List<JsValue>? trapKeys;
        JsObject? proxyTarget = null;
        var isProxy = source.TryGetOwnKeysTrapKeys(realm, out trapKeys) ||
                      source.TryGetProxyTarget(out proxyTarget);

        if (isProxy)
        {
            var keys = trapKeys;
            if (keys is null)
            {
                keys = realm.RentScratchList<JsValue>(8);
                CollectOwnPropertyKeysForCopyDataProperties(realm, proxyTarget!, keys);
            }

            try
            {
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (IsCopyDataPropertiesExcludedKey(realm, key, excludedKeys))
                        continue;
                    if (!TryGetOwnEnumerableForCopyDataProperties(realm, source, key, out var enumerable) ||
                        !enumerable)
                        continue;

                    CopyValueForCopyDataPropertiesKey(realm, source, target, key);
                }

                return;
            }
            finally
            {
                if (proxyTarget is not null && !ReferenceEquals(keys, trapKeys))
                    realm.ReturnScratchList(keys);
            }
        }

        var elementIndices = realm.RentScratchList<uint>(8);
        var enumerableNamedAtoms = realm.RentScratchList<int>(8);
        try
        {
            if (source is JsStringObject stringSource)
            {
                for (uint key = 0; key < (uint)stringSource.Value.Length; key++)
                {
                    var jsKey = JsValue.FromString(key.ToString(CultureInfo.InvariantCulture));
                    if (IsCopyDataPropertiesExcludedKey(realm, jsKey, excludedKeys))
                        continue;
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }
            else if (source is JsArray arraySource)
            {
                for (uint key = 0; key < arraySource.Length; key++)
                {
                    if (!source.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                        continue;
                    var jsKey = JsValue.FromString(key.ToString(CultureInfo.InvariantCulture));
                    if (IsCopyDataPropertiesExcludedKey(realm, jsKey, excludedKeys))
                        continue;
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }
            else
            {
                elementIndices.Clear();
                source.CollectOwnElementIndices(elementIndices, false);
                if (elementIndices.Count != 0)
                    elementIndices.Sort();
                for (var i = 0; i < elementIndices.Count; i++)
                {
                    var key = elementIndices[i];
                    if (!source.TryGetOwnElementDescriptor(key, out var desc) || !desc.Enumerable)
                        continue;
                    var jsKey = JsValue.FromString(key.ToString(CultureInfo.InvariantCulture));
                    if (IsCopyDataPropertiesExcludedKey(realm, jsKey, excludedKeys))
                        continue;
                    _ = source.TryGetElement(key, out var propValue);
                    target.DefineElementDescriptor(key, PropertyDescriptor.OpenData(propValue));
                }
            }

            enumerableNamedAtoms.Clear();
            source.CollectOwnNamedPropertyAtoms(realm, enumerableNamedAtoms, true);
            for (var i = 0; i < enumerableNamedAtoms.Count; i++)
            {
                var atom = enumerableNamedAtoms[i];
                if (atom < 0)
                    continue;
                var key = JsValue.FromString(Atoms.AtomToString(atom));
                if (IsCopyDataPropertiesExcludedKey(realm, key, excludedKeys))
                    continue;
                if (!source.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                    continue;
                target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
            }

            for (var i = 0; i < enumerableNamedAtoms.Count; i++)
            {
                var atom = enumerableNamedAtoms[i];
                if (atom >= 0)
                    continue;
                var symbol = Atoms.TryGetSymbolByAtom(atom, out var existing)
                    ? existing
                    : new(atom, Atoms.AtomToString(atom));
                var key = JsValue.FromSymbol(symbol);
                if (IsCopyDataPropertiesExcludedKey(realm, key, excludedKeys))
                    continue;
                if (!source.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                    continue;
                target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
            }

            if (source is JsGlobalObject globalSource)
                foreach (var pair in globalSource.EnumerateNamedGlobalDescriptors())
                {
                    var atom = pair.Key;
                    var desc = pair.Value;
                    if (!desc.Enumerable)
                        continue;
                    var key = atom >= 0
                        ? JsValue.FromString(Atoms.AtomToString(atom))
                        : JsValue.FromSymbol(Atoms.TryGetSymbolByAtom(atom, out var existing)
                            ? existing
                            : new(atom, Atoms.AtomToString(atom)));
                    if (IsCopyDataPropertiesExcludedKey(realm, key, excludedKeys))
                        continue;
                    if (!source.TryGetPropertyAtom(realm, atom, out var propValue, out _))
                        continue;
                    target.DefineOwnDataPropertyExact(Realm, atom, propValue, JsShapePropertyFlags.Open);
                }
        }
        finally
        {
            Realm.ReturnScratchList(enumerableNamedAtoms);
            Realm.ReturnScratchList(elementIndices);
        }
    }

    private static bool TryCopyDataPropertiesFromProxySource(JsRealm realm, JsObject source, JsObject target)
    {
        JsObject? proxyTarget = null;
        var isProxy = source.TryGetOwnKeysTrapKeys(realm, out var trapKeys) ||
                      source.TryGetProxyTarget(out proxyTarget);

        if (!isProxy)
            return false;

        var keys = trapKeys;
        if (keys is null)
        {
            keys = realm.RentScratchList<JsValue>(8);
            CollectOwnPropertyKeysForCopyDataProperties(realm, proxyTarget!, keys);
        }

        try
        {
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!TryGetOwnEnumerableForCopyDataProperties(realm, source, key, out var enumerable) || !enumerable)
                    continue;

                CopyValueForCopyDataPropertiesKey(realm, source, target, key);
            }

            return true;
        }
        finally
        {
            if (proxyTarget is not null && !ReferenceEquals(keys, trapKeys))
                realm.ReturnScratchList(keys);
        }
    }

    private static void CollectOwnPropertyKeysForCopyDataProperties(JsRealm realm, JsObject source,
        List<JsValue> keysOut)
    {
        var elementIndices = realm.RentScratchList<uint>(8);
        var seenStringAtoms = realm.RentScratchHashSet<int>();
        var namedAtoms = realm.RentScratchList<int>(8);
        var symbolAtoms = realm.RentScratchList<int>(4);
        try
        {
            source.CollectOwnElementIndices(elementIndices, false);
            if (elementIndices.Count != 0)
                elementIndices.Sort();
            for (var i = 0; i < elementIndices.Count; i++)
                keysOut.Add(JsValue.FromString(elementIndices[i].ToString(CultureInfo.InvariantCulture)));

            source.CollectOwnNamedPropertyAtoms(realm, namedAtoms, false);
            for (var i = 0; i < namedAtoms.Count; i++)
            {
                var atom = namedAtoms[i];
                if (atom < 0)
                {
                    symbolAtoms.Add(atom);
                    continue;
                }

                if (seenStringAtoms.Add(atom))
                    keysOut.Add(JsValue.FromString(realm.Atoms.AtomToString(atom)));
            }

            if (source is JsGlobalObject globalSource)
                foreach (var pair in globalSource.EnumerateNamedGlobalDescriptors())
                {
                    var atom = pair.Key;
                    if (atom < 0)
                    {
                        symbolAtoms.Add(atom);
                        continue;
                    }

                    if (seenStringAtoms.Add(atom))
                        keysOut.Add(JsValue.FromString(realm.Atoms.AtomToString(atom)));
                }

            for (var i = 0; i < symbolAtoms.Count; i++)
            {
                var sym = realm.Atoms.TryGetSymbolByAtom(symbolAtoms[i], out var existing)
                    ? existing
                    : new(symbolAtoms[i], realm.Atoms.AtomToString(symbolAtoms[i]));
                keysOut.Add(JsValue.FromSymbol(sym));
            }
        }
        finally
        {
            realm.ReturnScratchList(symbolAtoms);
            realm.ReturnScratchList(namedAtoms);
            realm.ReturnScratchHashSet(seenStringAtoms);
            realm.ReturnScratchList(elementIndices);
        }
    }

    private static bool TryGetOwnEnumerableForCopyDataProperties(JsRealm realm, JsObject source, in JsValue key,
        out bool enumerable)
    {
        return TryGetOwnEnumerableCore(realm, source, key, out enumerable);
    }

    private static bool TryGetOwnEnumerableCore(JsRealm realm, JsObject source, in JsValue key,
        out bool enumerable)
    {
        var trapHasDescriptor = false;
        var trapEnumerable = false;
        var trapUsed =
            source.TryGetOwnEnumerableDescriptorViaTrap(realm, key, out trapHasDescriptor, out trapEnumerable);

        if (trapUsed)
        {
            enumerable = trapHasDescriptor && trapEnumerable;
            return trapHasDescriptor;
        }

        var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);

        if (isIndex && source.TryGetOwnElementDescriptor(index, out var elementDescriptor))
        {
            enumerable = elementDescriptor.Enumerable;
            return true;
        }

        if (source.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var namedDescriptor))
        {
            enumerable = namedDescriptor.Enumerable;
            return true;
        }

        if (source is JsGlobalObject globalObject &&
            globalObject.TryGetNamedGlobalDescriptorAtom(atom, out var globalDescriptor))
        {
            enumerable = globalDescriptor.Enumerable;
            return true;
        }

        enumerable = false;
        return false;
    }

    private static void CopyValueForCopyDataPropertiesKey(JsRealm realm, JsObject source, JsObject target,
        in JsValue key)
    {
        var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);
        if (isIndex)
        {
            _ = source.TryGetElement(index, out var value);
            target.DefineElementDescriptor(index, PropertyDescriptor.OpenData(value));
            return;
        }

        if (!source.TryGetPropertyAtom(realm, atom, out var propValue, out _))
            return;

        target.DefineOwnDataPropertyExact(realm, atom, propValue, JsShapePropertyFlags.Open);
    }

    private static bool IsCopyDataPropertiesExcludedKey(
        JsRealm realm,
        in JsValue key,
        ReadOnlySpan<JsValue> excludedKeys)
    {
        for (var i = 0; i < excludedKeys.Length; i++)
        {
            var excludedKey = excludedKeys[i];
            if (SameValueForCopyDataPropertiesKey(realm, key, excludedKey))
                return true;
        }

        return false;
    }

    private static bool SameValueForCopyDataPropertiesKey(JsRealm realm, in JsValue left, in JsValue right)
    {
        if (left.IsString && right.IsString)
            return left.AsJsString().Equals(right.AsJsString());
        if (left.IsSymbol && right.IsSymbol)
            return ReferenceEquals(left.AsSymbol(), right.AsSymbol());
        if (realm.TryResolvePropertyKey(left, out var leftIndex, out var leftAtom) &&
            realm.TryResolvePropertyKey(right, out var rightIndex, out var rightAtom))
            return leftIndex == rightIndex && leftAtom == rightAtom;

        return JsValue.SameValue(left, right);
    }

    private readonly struct DescriptorSnapshot
    {
        internal readonly bool HasValue;
        internal readonly JsValue Value;
        internal readonly bool HasWritable;
        internal readonly JsValue WritableValue;
        internal readonly bool HasEnumerable;
        internal readonly JsValue EnumerableValue;
        internal readonly bool HasConfigurable;
        internal readonly JsValue ConfigurableValue;
        internal readonly bool HasGet;
        internal readonly JsValue GetValue;
        internal readonly bool HasSet;
        internal readonly JsValue SetValue;

        internal DescriptorSnapshot(
            bool hasValue, in JsValue value,
            bool hasWritable, in JsValue writableValue,
            bool hasEnumerable, in JsValue enumerableValue,
            bool hasConfigurable, in JsValue configurableValue,
            bool hasGet, in JsValue getValue,
            bool hasSet, in JsValue setValue)
        {
            HasValue = hasValue;
            Value = value;
            HasWritable = hasWritable;
            WritableValue = writableValue;
            HasEnumerable = hasEnumerable;
            EnumerableValue = enumerableValue;
            HasConfigurable = hasConfigurable;
            ConfigurableValue = configurableValue;
            HasGet = hasGet;
            GetValue = getValue;
            HasSet = hasSet;
            SetValue = setValue;
        }

        internal bool IsAccessorDescriptor => HasGet || HasSet;
        internal bool IsDataDescriptor => HasValue || HasWritable;
        internal bool IsGenericDescriptor => !IsAccessorDescriptor && !IsDataDescriptor;
    }
}
