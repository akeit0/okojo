using System.Globalization;
using System.Runtime.InteropServices;
using Okojo.Internals;

namespace Okojo.Objects;

internal interface IProxyObject
{
    JsObject ProxyOwner { get; }
    ref ProxyCore Core { get; }
    void RevokeProxy();
}

internal struct ProxyCore
{
    internal ProxyCore(JsObject target, JsObject handler)
    {
        CurrentTarget = target;
        CurrentHandler = handler;
    }

    internal JsObject? CurrentTarget { get; private set; }

    internal JsObject? CurrentHandler { get; private set; }

    internal void Revoke(JsObject owner)
    {
        CurrentTarget = null;
        CurrentHandler = null;
        owner.Prototype = null;
    }

    internal bool TryGetProxyTarget(out JsObject target)
    {
        if (CurrentTarget is not null && CurrentHandler is not null)
        {
            target = CurrentTarget;
            return true;
        }

        target = null!;
        return false;
    }

    internal JsObject EnsureTarget(JsRealm errorRealm)
    {
        if (CurrentTarget is null || CurrentHandler is null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot perform operation on a revoked Proxy",
                errorRealm: errorRealm);
        return CurrentTarget;
    }

    internal JsFunction EnsureCallableTarget(JsRealm errorRealm)
    {
        var target = EnsureTarget(errorRealm);
        if (target is not JsFunction fn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy target is not callable");
        return fn;
    }

    internal JsObject? GetPrototypeOf(JsRealm realm)
    {
        var target = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomGetPrototypeOf = IdGetPrototypeOf;
        if (!handler.TryGetPropertyAtom(realm, atomGetPrototypeOf, out var trap, out _) || trap.IsUndefined ||
            trap.IsNull)
            return target.GetPrototypeOf(realm);

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy getPrototypeOf trap is not a function");

        var targetValue = JsValue.FromObject(target);
        var args = MemoryMarshal.CreateReadOnlySpan(ref targetValue, 1);
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args);
        if (trapResult.IsNull)
        {
            if (!QueryIsExtensible(realm, target) && !SamePrototype(target.GetPrototypeOf(realm), null))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getPrototypeOf trap cannot change prototype of non-extensible target");
            return null;
        }

        if (trapResult.TryGetObject(out var prototype))
        {
            if (!QueryIsExtensible(realm, target) && !SamePrototype(target.GetPrototypeOf(realm), prototype))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getPrototypeOf trap cannot change prototype of non-extensible target");
            return prototype;
        }

        throw new JsRuntimeException(JsErrorKind.TypeError,
            "Proxy getPrototypeOf trap result must be object or null");
    }

    internal bool IsExtensibleViaProxy(JsRealm realm)
    {
        var target = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomIsExtensible = IdIsExtensible;
        if (!handler.TryGetPropertyAtom(realm, atomIsExtensible, out var trap, out _) || trap.IsUndefined ||
            trap.IsNull)
            return QueryIsExtensible(realm, target);

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy isExtensible trap is not a function");

        var targetValue = JsValue.FromObject(target);
        var args = MemoryMarshal.CreateReadOnlySpan(ref targetValue, 1);
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args);
        var booleanTrapResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
        var targetResult = QueryIsExtensible(realm, target);
        if (booleanTrapResult != targetResult)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy isExtensible trap result must match target extensibility");
        return booleanTrapResult;
    }

    internal bool TryGetOwnKeysTrapKeys(JsRealm realm, out List<JsValue>? keys)
    {
        _ = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomOwnKeys = IdOwnKeys;
        if (!handler.TryGetPropertyAtom(realm, atomOwnKeys, out var trap, out _) || trap.IsUndefined || trap.IsNull)
        {
            keys = null;
            return false;
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy ownKeys trap is not a function");

        var targetValue = JsValue.FromObject(CurrentTarget!);
        var arg = MemoryMarshal.CreateReadOnlySpan(ref targetValue, 1);
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), arg);
        if (!trapResult.TryGetObject(out var resultObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy ownKeys trap result must be object");

        var lenLong = realm.GetArrayLikeLengthLong(resultObj);
        var len = lenLong <= 0
            ? 0u
            : (uint)Math.Min(uint.MaxValue, lenLong);
        keys = new((int)Math.Min(len, 8u));
        for (uint i = 0; i < len; i++)
            keys.Add(resultObj.TryGetElement(i, out var key) ? key : JsValue.Undefined);

        return true;
    }

    internal bool TryGetOwnEnumerableDescriptorViaTrap(JsRealm realm, in JsValue key, out bool hasDescriptor,
        out bool enumerable)
    {
        _ = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomGetOwnPropertyDescriptor = IdGetOwnPropertyDescriptor;
        if (!handler.TryGetPropertyAtom(realm, atomGetOwnPropertyDescriptor, out var trap, out _) ||
            trap.IsUndefined || trap.IsNull)
        {
            hasDescriptor = false;
            enumerable = false;
            return false;
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap is not a function");

        var args = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(CurrentTarget!),
            Item1 = key
        };
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());

        if (trapResult.IsUndefined)
        {
            hasDescriptor = false;
            enumerable = false;
            return true;
        }

        if (!trapResult.TryGetObject(out var descriptorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap result must be object or undefined");

        hasDescriptor = true;
        enumerable = false;
        if (descriptorObj.TryGetProperty("enumerable", out var enumerableValue))
            enumerable = DescriptorUtilities.ToBooleanForDescriptor(enumerableValue);
        return true;
    }

    internal bool TryGetOwnPropertyDescriptorViaTrap(JsRealm realm, in JsValue key, out JsValue descriptor)
    {
        var propertyKey = key.IsNumber ? JsValue.FromString(JsValue.NumberToJsString(key.NumberValue)) : key;
        var handler = CurrentHandler!;
        const int atomGetOwnPropertyDescriptor = IdGetOwnPropertyDescriptor;
        if (!handler.TryGetPropertyAtom(realm, atomGetOwnPropertyDescriptor, out var trap, out _) ||
            trap.IsUndefined || trap.IsNull)
        {
            descriptor =
                ProxyDescriptorUtilities.GetOwnPropertyDescriptorValue(realm, EnsureTarget(realm), propertyKey);
            return true;
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap is not a function");

        var args = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(CurrentTarget!),
            Item1 = propertyKey
        };
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
        ValidateOwnPropertyDescriptorTrapResult(realm, CurrentTarget!, propertyKey, trapResult);
        descriptor = trapResult;
        return true;
    }

    internal bool TryHasPropertyViaTrap(JsRealm realm, in JsValue key, out bool result)
    {
        var target = EnsureTarget(realm);
        var propertyKey = key.IsNumber ? JsValue.FromString(JsValue.NumberToJsString(key.NumberValue)) : key;
        var handler = CurrentHandler!;
        const int atomHas = IdHas;
        if (!handler.TryGetPropertyAtom(realm, atomHas, out var trap, out _) || trap.IsUndefined || trap.IsNull)
        {
            result = ProxyDescriptorUtilities.HasPropertyOnTarget(realm, target, propertyKey);
            return true;
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy has trap is not a function");

        var args = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(target),
            Item1 = propertyKey
        };
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
        result = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
        if (!result &&
            OwnKeysHelpers.TryGetOwnPropertyConfigurability(realm, target, propertyKey, out var configurable) &&
            (!configurable || !QueryIsExtensible(realm, target)))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy has trap cannot report existing property as missing");

        return true;
    }

    internal void GetOwnPropertyDescriptorKindViaProxy(JsRealm realm, in JsValue key, out bool hasDescriptor,
        out bool isAccessor)
    {
        var target = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomGetOwnPropertyDescriptor = IdGetOwnPropertyDescriptor;
        if (handler.TryGetPropertyAtom(realm, atomGetOwnPropertyDescriptor, out var trap, out _) &&
            !trap.IsUndefined && !trap.IsNull)
        {
            if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getOwnPropertyDescriptor trap is not a function");

            var args = new InlineJsValueArray2
            {
                Item0 = JsValue.FromObject(target),
                Item1 = key
            };
            var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
            if (trapResult.IsUndefined)
            {
                hasDescriptor = false;
                isAccessor = false;
                return;
            }

            if (!trapResult.TryGetObject(out var descriptorObj))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getOwnPropertyDescriptor trap result must be object or undefined");
            hasDescriptor = true;
            var hasGet = descriptorObj.TryGetPropertyAtom(realm, IdGet, out _, out _);
            var hasSet = descriptorObj.TryGetPropertyAtom(realm, IdSet, out _, out _);
            isAccessor = hasGet || hasSet;
            return;
        }

        hasDescriptor = TryGetOwnDescriptorKindFromTarget(realm, target, key, out isAccessor);
    }

    internal bool SetPrototypeViaProxy(JsObject owner, JsRealm realm, JsObject? proto)
    {
        var target = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomSetPrototypeOf = IdSetPrototypeOf;
        if (!handler.TryGetPropertyAtom(realm, atomSetPrototypeOf, out var trap, out _) || trap.IsUndefined ||
            trap.IsNull)
        {
            var delegated = SetPrototypeOnTarget(realm, target, proto);
            if (delegated)
                owner.Prototype = target.Prototype;
            return delegated;
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy setPrototypeOf trap is not a function");

        var args = new InlineJsValueArray2
        {
            Item0 = JsValue.FromObject(target),
            Item1 = proto is null ? JsValue.Null : JsValue.FromObject(proto)
        };
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
        var booleanTrapResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
        if (!booleanTrapResult)
            return false;

        if (QueryIsExtensible(realm, target))
            return true;

        var targetProto = target.GetPrototypeOf(realm);
        if (!SamePrototype(targetProto, proto))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy setPrototypeOf trap cannot change prototype of non-extensible target");

        owner.Prototype = target.Prototype;
        return true;
    }

    internal bool PreventExtensionsViaProxy(JsRealm realm)
    {
        var target = EnsureTarget(realm);
        var handler = CurrentHandler!;
        const int atomPreventExtensions = IdPreventExtensions;
        if (!handler.TryGetPropertyAtom(realm, atomPreventExtensions, out var trap, out _) ||
            trap.IsUndefined || trap.IsNull)
        {
            PreventExtensionsOnTarget(realm, target);
            return !QueryIsExtensible(realm, target);
        }

        if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy preventExtensions trap is not a function");

        var arg0 = JsValue.FromObject(target);
        var args = MemoryMarshal.CreateReadOnlySpan(ref arg0, 1);
        var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args);
        var booleanTrapResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
        if (!booleanTrapResult)
            return false;

        if (QueryIsExtensible(realm, target))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy preventExtensions trap returned true for extensible target");

        return true;
    }

    internal static bool TryGetOwnDescriptorKindFromTarget(JsRealm realm, JsObject target, in JsValue key,
        out bool isAccessor)
    {
        if (key.IsSymbol)
        {
            var atom = key.AsSymbol().Atom;
            if (target.TryGetOwnPropertySlotInfoAtom(atom, out var info))
            {
                isAccessor = (info.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
                return true;
            }

            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var descriptor))
            {
                isAccessor = descriptor.IsAccessor;
                return true;
            }

            if (target is JsGlobalObject global && global.TryGetOwnGlobalDescriptorAtom(atom, out _))
            {
                isAccessor = false;
                return true;
            }

            isAccessor = false;
            return false;
        }

        var text = key.IsString ? key.AsString() : key.ToString() ?? string.Empty;
        if (TryGetArrayIndexFromCanonicalString(text, out var index))
        {
            if (target.TryGetOwnElementDescriptor(index, out var elementDescriptor))
            {
                isAccessor = elementDescriptor.IsAccessor;
                return true;
            }

            isAccessor = false;
            return false;
        }

        var namedAtom = realm.Atoms.InternNoCheck(text);
        if (target.TryGetOwnPropertySlotInfoAtom(namedAtom, out var namedInfo))
        {
            isAccessor = (namedInfo.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0;
            return true;
        }

        if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, namedAtom, out var namedDescriptor))
        {
            isAccessor = namedDescriptor.IsAccessor;
            return true;
        }

        if (target is JsGlobalObject namedGlobal && namedGlobal.TryGetOwnGlobalDescriptorAtom(namedAtom, out _))
        {
            isAccessor = false;
            return true;
        }

        isAccessor = false;
        return false;
    }

    internal static bool SetPrototypeOnTarget(JsRealm realm, JsObject target, JsObject? proto)
    {
        if (target is IProxyObject proxy)
            return proxy.Core.SetPrototypeViaProxy(proxy.ProxyOwner, realm, proto);

        return target.TrySetPrototype(proto);
    }

    internal static void PreventExtensionsOnTarget(JsRealm realm, JsObject target)
    {
        if (target is IProxyObject proxy)
        {
            if (!proxy.Core.PreventExtensionsViaProxy(realm))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy preventExtensions trap returned false");
            return;
        }

        target.PreventExtensions();
    }

    internal static bool QueryIsExtensible(JsRealm realm, JsObject target)
    {
        return target is IProxyObject proxy ? proxy.Core.IsExtensibleViaProxy(realm) : target.IsExtensible;
    }

    private static bool SamePrototype(JsObject? left, JsObject? right)
    {
        return ReferenceEquals(left, right);
    }

    private static void ValidateOwnPropertyDescriptorTrapResult(JsRealm realm, JsObject target, in JsValue key,
        in JsValue trapResult)
    {
        var extensibleTarget = QueryIsExtensible(realm, target);
        var targetHasOwn =
            OwnKeysHelpers.TryGetOwnPropertyConfigurability(realm, target, key, out var targetConfigurable);
        if (trapResult.IsUndefined)
        {
            if (targetHasOwn && (!targetConfigurable || !extensibleTarget))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getOwnPropertyDescriptor trap cannot hide existing property");

            return;
        }

        if (!trapResult.TryGetObject(out var descriptorObject))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap result must be object or undefined");

        var hasGet = descriptorObject.TryGetPropertyAtom(realm, IdGet, out _, out _);
        var hasSet = descriptorObject.TryGetPropertyAtom(realm, IdSet, out _, out _);
        var hasValue = descriptorObject.TryGetPropertyAtom(realm, IdValue, out _, out _);
        var hasWritable = descriptorObject.TryGetPropertyAtom(realm, IdWritable, out var writableValue, out _);
        var hasConfigurable =
            descriptorObject.TryGetPropertyAtom(realm, IdConfigurable, out var configurableValue, out _);
        if ((hasGet || hasSet) && (hasValue || hasWritable))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");

        var resultConfigurable = hasConfigurable && DescriptorUtilities.ToBooleanForDescriptor(configurableValue);
        var resultIsAccessor = hasGet || hasSet;
        if (!targetHasOwn)
        {
            if (!extensibleTarget || !resultConfigurable)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getOwnPropertyDescriptor trap reported incompatible descriptor");

            return;
        }

        if (resultConfigurable && !targetConfigurable)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap cannot report configurable descriptor for non-configurable target property");

        if (!resultConfigurable && targetConfigurable)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap cannot report non-configurable descriptor for configurable target property");

        if (!targetConfigurable && TryGetOwnDescriptorKindFromTarget(realm, target, key, out var targetIsAccessor) &&
            targetIsAccessor != resultIsAccessor)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap cannot change descriptor kind of non-configurable target property");

        if (!resultConfigurable && hasWritable && !DescriptorUtilities.ToBooleanForDescriptor(writableValue) &&
            ProxyDescriptorUtilities.TryGetOwnPropertyDescriptor(realm, target, key, out var targetDescriptor) &&
            !targetDescriptor.IsAccessor && !targetDescriptor.Configurable && targetDescriptor.Writable)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy getOwnPropertyDescriptor trap cannot report non-writable descriptor for writable target property");
    }
}

internal static class ProxyObjectExtensions
{
    private static JsValue GetPropertyKey(JsRealm realm, int atom)
    {
        return atom < 0
            ? JsValue.FromSymbol(realm.Atoms.TryGetSymbolByAtom(atom, out var sym)
                ? sym
                : new(atom, realm.Atoms.AtomToString(atom)))
            : JsValue.FromString(realm.Atoms.AtomToString(atom));
    }

    private static bool SamePrototype(JsObject? left, JsObject? right)
    {
        return ReferenceEquals(left, right);
    }

    private static void ValidateGetTrapResult(JsRealm realm, JsObject target, in JsValue key, in JsValue value)
    {
        if (!ProxyDescriptorUtilities.TryGetOwnPropertyDescriptor(realm, target, key, out var targetDescriptor) ||
            targetDescriptor.Configurable)
            return;

        if (!targetDescriptor.IsAccessor)
        {
            if (!targetDescriptor.Writable && !JsValue.SameValue(targetDescriptor.Value, value))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy get trap cannot report different value for non-writable property");

            return;
        }

        if (targetDescriptor.Getter is null && !value.IsUndefined)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy get trap cannot report value for accessor without getter");
    }

    private static void ValidateDeletePropertyTrapSuccess(JsRealm realm, JsObject target, in JsValue key)
    {
        if (!ProxyDescriptorUtilities.TryGetOwnPropertyDescriptor(realm, target, key, out var targetDescriptor))
            return;
        if (!targetDescriptor.Configurable || !target.IsExtensibleViaProxyAware(realm))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy deleteProperty trap cannot report successful deletion");
    }

    private static void ValidateDefinePropertyTrapSuccess(JsRealm realm, JsObject target, in JsValue key,
        JsObject descriptorObject)
    {
        var request = ProxyDescriptorUtilities.ReadDescriptorRequest(realm, descriptorObject);
        var extensibleTarget = target.IsExtensibleViaProxyAware(realm);
        var hasTargetDescriptor =
            ProxyDescriptorUtilities.TryGetOwnPropertyDescriptor(realm, target, key, out var targetDescriptor);
        var settingConfigFalse = request.HasConfigurable && !request.Configurable;
        if (!hasTargetDescriptor)
        {
            if (!extensibleTarget || settingConfigFalse)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy defineProperty trap reported incompatible descriptor");

            return;
        }

        if (!ProxyDescriptorUtilities.IsCompatibleDefineRequest(request, targetDescriptor))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy defineProperty trap reported incompatible descriptor");

        if (settingConfigFalse && targetDescriptor.Configurable)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy defineProperty trap cannot report non-configurable descriptor for configurable target property");

        if (!targetDescriptor.Configurable &&
            request.HasWritable &&
            !request.Writable &&
            !targetDescriptor.IsAccessor &&
            targetDescriptor.Writable)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy defineProperty trap cannot report non-writable descriptor for writable target property");
    }

    private static void ValidateSetTrapSuccess(JsRealm realm, JsObject target, in JsValue key, in JsValue value)
    {
        if (!ProxyDescriptorUtilities.TryGetOwnPropertyDescriptor(realm, target, key, out var targetDescriptor) ||
            targetDescriptor.Configurable)
            return;

        if (!targetDescriptor.IsAccessor)
        {
            if (!targetDescriptor.Writable && !JsValue.SameValue(targetDescriptor.Value, value))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy set trap cannot report successful write to non-writable property");

            return;
        }

        if (targetDescriptor.Setter is null)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy set trap cannot report successful write to accessor without setter");
    }

    extension(JsObject target)
    {
        internal bool TryGetProxyTargetOrThrow(JsRealm errorRealm, out JsObject proxyTarget)
        {
            if (target is IProxyObject proxy)
            {
                if (!proxy.Core.TryGetProxyTarget(out proxyTarget))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot perform operation on a revoked Proxy",
                        errorRealm: errorRealm);

                return true;
            }

            proxyTarget = null!;
            return false;
        }

        internal bool TryGetOwnKeysTrapKeys(JsRealm realm, out List<JsValue>? keys)
        {
            if (target is IProxyObject proxy) return proxy.Core.TryGetOwnKeysTrapKeys(realm, out keys);

            keys = null;
            return false;
        }

        internal bool TryGetProxyTarget(out JsObject proxyTarget)
        {
            if (target is IProxyObject proxy) return proxy.Core.TryGetProxyTarget(out proxyTarget);

            proxyTarget = null!;
            return false;
        }

        internal bool TryGetOwnEnumerableDescriptorViaTrap(JsRealm realm, in JsValue key,
            out bool hasDescriptor, out bool enumerable)
        {
            if (target is IProxyObject proxy)
                return proxy.Core.TryGetOwnEnumerableDescriptorViaTrap(realm, key, out hasDescriptor, out enumerable);

            hasDescriptor = false;
            enumerable = false;
            return false;
        }

        internal bool TryGetOwnPropertyDescriptorViaTrap(JsRealm realm, in JsValue key,
            out JsValue descriptor)
        {
            if (target is IProxyObject proxy)
                return proxy.Core.TryGetOwnPropertyDescriptorViaTrap(realm, key, out descriptor);

            descriptor = JsValue.Undefined;
            return false;
        }

        internal bool TryHasPropertyViaTrap(JsRealm realm, in JsValue key, out bool result)
        {
            if (target is IProxyObject proxy) return proxy.Core.TryHasPropertyViaTrap(realm, key, out result);

            result = false;
            return false;
        }

        internal bool TryDefineOwnDataPropertyForSet(JsRealm realm, int atom, JsValue value,
            out SlotInfo slotInfo)
        {
            if (target is IProxyObject proxy)
                return proxy.TryDefineOwnDataPropertyForSet(realm, atom, value, out slotInfo);

            slotInfo = SlotInfo.Invalid;
            var key = GetPropertyKey(realm, atom);
            var hasDescriptor = target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out _);
            return ProxyDescriptorUtilities.TryDefineOwnDataPropertyForSet(realm, target, key, value, hasDescriptor);
        }

        internal bool TryDefineOwnDataPropertyForSet(JsRealm realm, uint index, JsValue value,
            out SlotInfo slotInfo)
        {
            if (target is IProxyObject proxy)
                return proxy.TryDefineOwnDataPropertyForSet(realm, index, value, out slotInfo);

            slotInfo = SlotInfo.Invalid;
            var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
            var hasDescriptor = target.TryGetOwnElementDescriptor(index, out _);
            return ProxyDescriptorUtilities.TryDefineOwnDataPropertyForSet(realm, target, key, value, hasDescriptor);
        }

        internal bool TryDefinePropertyFromDescriptorObject(JsRealm realm, in JsValue keyValue,
            JsPlainObject descriptorObject, out bool result)
        {
            if (target is IProxyObject proxy)
            {
                result = proxy.TryDefinePropertyFromDescriptorObject(realm, keyValue, descriptorObject);
                return true;
            }

            result = false;
            return false;
        }

        internal bool DefinePropertyFromDescriptorObject(JsRealm realm, in JsValue keyValue,
            JsPlainObject descriptorObject)
        {
            if (target is IProxyObject proxy)
            {
                proxy.DefinePropertyFromDescriptorObject(realm, keyValue, descriptorObject);
                return true;
            }

            return false;
        }

        internal bool IsExtensibleViaProxyAware(JsRealm realm)
        {
            return target is IProxyObject proxy
                ? proxy.Core.IsExtensibleViaProxy(realm)
                : target.IsExtensible;
        }

        internal bool TrySetPrototypeViaProxyAware(JsRealm realm, JsObject? proto)
        {
            return target is IProxyObject proxy
                ? proxy.SetPrototypeViaProxy(realm, proto)
                : target.TrySetPrototype(proto);
        }

        internal void PreventExtensionsViaProxyAware(JsRealm realm)
        {
            if (target is IProxyObject proxy)
            {
                if (!proxy.PreventExtensionsViaProxy(realm))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy preventExtensions trap returned false");
                return;
            }

            target.PreventExtensions();
        }
    }

    extension(IProxyObject proxy)
    {
        internal bool TryGetPropertyAtomViaProxy(JsRealm realm, JsObject receiver, int atom,
            out JsValue value, out SlotInfo slotInfo)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomGet = IdGet;
            if (handler.TryGetPropertyAtom(realm, atomGet, out var trap, out _) && !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy get trap is not a function");

                var key = GetPropertyKey(realm, atom);
                var args = new InlineJsValueArray3
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = JsValue.FromObject(receiver)
                };
                value = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                ValidateGetTrapResult(realm, target, key, value);
                slotInfo = SlotInfo.Invalid;
                return true;
            }

            if (target is IProxyObject nestedProxy)
                return nestedProxy.TryGetPropertyAtomViaProxy(realm, (JsValue)receiver, atom, out value, out slotInfo);

            return target.TryGetPropertyAtomWithReceiver(realm, receiver, atom, out value, out slotInfo);
        }

        internal bool TryGetPropertyAtomViaProxy(JsRealm realm, in JsValue receiverValue,
            int atom, out JsValue value, out SlotInfo slotInfo)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomGet = IdGet;
            if (handler.TryGetPropertyAtom(realm, atomGet, out var trap, out _) && !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy get trap is not a function");

                var key = GetPropertyKey(realm, atom);
                var args = new InlineJsValueArray3
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = receiverValue
                };
                value = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                ValidateGetTrapResult(realm, target, key, value);
                slotInfo = SlotInfo.Invalid;
                return true;
            }

            if (target is IProxyObject nestedProxy)
                return nestedProxy.TryGetPropertyAtomViaProxy(realm, receiverValue, atom, out value, out slotInfo);

            return target.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
        }

        internal bool SetPropertyAtomWithReceiverViaProxy(JsRealm realm, JsObject receiver,
            int atom, JsValue value, out SlotInfo slotInfo)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomSet = IdSet;
            if (handler.TryGetPropertyAtom(realm, atomSet, out var trap, out _) && !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy set trap is not a function");

                var key = GetPropertyKey(realm, atom);
                var args = new InlineJsValueArray4
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = value,
                    Item3 = JsValue.FromObject(receiver)
                };
                var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                slotInfo = SlotInfo.Invalid;
                var setResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
                if (setResult)
                    ValidateSetTrapSuccess(realm, target, key, value);
                return setResult;
            }

            return target.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
        }

        internal bool TryGetElementViaProxy(uint index, JsObject receiver, out JsValue value)
        {
            var owner = proxy.ProxyOwner;
            var target = proxy.Core.EnsureTarget(owner.Realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomGet = IdGet;
            if (handler.TryGetPropertyAtom(target.Realm, atomGet, out var trap, out _) && !trap.IsUndefined &&
                !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy get trap is not a function");

                var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
                var args = new InlineJsValueArray3
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = JsValue.FromObject(receiver)
                };
                value = target.Realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                return true;
            }

            return target.TryGetElement(index, out value);
        }

        internal bool SetElementWithReceiverViaProxy(JsRealm realm, JsObject receiver,
            uint index, JsValue value)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomSet = IdSet;
            if (handler.TryGetPropertyAtom(target.Realm, atomSet, out var trap, out _) && !trap.IsUndefined &&
                !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy set trap is not a function");

                var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
                var args = new InlineJsValueArray4
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = value,
                    Item3 = JsValue.FromObject(receiver)
                };
                var trapResult = target.Realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                var setResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
                if (setResult)
                    ValidateSetTrapSuccess(target.Realm, target, key, value);
                return setResult;
            }

            return target.SetElementWithReceiver(target.Realm, receiver, index, value);
        }

        internal bool DeleteElementViaProxy(uint index)
        {
            var owner = proxy.ProxyOwner;
            var target = proxy.Core.EnsureTarget(owner.Realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomDeleteProperty = IdDeleteProperty;
            if (handler.TryGetPropertyAtom(target.Realm, atomDeleteProperty, out var trap, out _) &&
                !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy deleteProperty trap is not a function");

                var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
                var args = new InlineJsValueArray2
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key
                };
                var trapResult = target.Realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                var deleteResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
                if (deleteResult)
                    ValidateDeletePropertyTrapSuccess(target.Realm, target, key);
                return deleteResult;
            }

            return target.DeleteElement(index);
        }

        internal bool DeletePropertyAtomViaProxy(JsRealm realm, int atom)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomDeleteProperty = IdDeleteProperty;
            if (handler.TryGetPropertyAtom(realm, atomDeleteProperty, out var trap, out _) &&
                !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy deleteProperty trap is not a function");

                var key = GetPropertyKey(realm, atom);
                var args = new InlineJsValueArray2
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key
                };
                var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                var deleteResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
                if (deleteResult)
                    ValidateDeletePropertyTrapSuccess(realm, target, key);
                return deleteResult;
            }

            return target.DeletePropertyAtom(realm, atom);
        }

        internal bool SetPrototypeViaProxy(JsRealm realm, JsObject? proto)
        {
            var owner = proxy.ProxyOwner;
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomSetPrototypeOf = IdSetPrototypeOf;
            if (!handler.TryGetPropertyAtom(realm, atomSetPrototypeOf, out var trap, out _) || trap.IsUndefined ||
                trap.IsNull)
            {
                var delegated = target.TrySetPrototypeViaProxyAware(realm, proto);
                if (delegated)
                    owner.Prototype = target.Prototype;
                return delegated;
            }

            if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy setPrototypeOf trap is not a function");

            var args = new InlineJsValueArray2
            {
                Item0 = JsValue.FromObject(target),
                Item1 = proto is null ? JsValue.Null : JsValue.FromObject(proto)
            };
            var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
            var booleanTrapResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
            if (!booleanTrapResult)
                return false;

            if (target.IsExtensibleViaProxyAware(realm))
                return true;

            var targetProto = target.GetPrototypeOf(realm);
            if (!SamePrototype(targetProto, proto))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy setPrototypeOf trap cannot change prototype of non-extensible target");

            owner.Prototype = target.Prototype;
            return true;
        }

        internal bool PreventExtensionsViaProxy(JsRealm realm)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomPreventExtensions = IdPreventExtensions;
            if (!handler.TryGetPropertyAtom(realm, atomPreventExtensions, out var trap, out _) ||
                trap.IsUndefined || trap.IsNull)
            {
                target.PreventExtensionsViaProxyAware(realm);
                return !target.IsExtensibleViaProxyAware(realm);
            }

            if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy preventExtensions trap is not a function");

            var arg0 = JsValue.FromObject(target);
            var args = MemoryMarshal.CreateReadOnlySpan(ref arg0, 1);
            var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args);
            var booleanTrapResult = DescriptorUtilities.ToBooleanForDescriptor(trapResult);
            if (!booleanTrapResult)
                return false;

            if (target.IsExtensibleViaProxyAware(realm))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy preventExtensions trap returned true for extensible target");

            return true;
        }

        internal void GetOwnPropertyDescriptorKindViaProxy(JsRealm realm, in JsValue key,
            out bool hasDescriptor, out bool isAccessor)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomGetOwnPropertyDescriptor = IdGetOwnPropertyDescriptor;
            if (handler.TryGetPropertyAtom(realm, atomGetOwnPropertyDescriptor, out var trap, out _) &&
                !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Proxy getOwnPropertyDescriptor trap is not a function");

                var args = new InlineJsValueArray2
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key
                };
                var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                if (trapResult.IsUndefined)
                {
                    hasDescriptor = false;
                    isAccessor = false;
                    return;
                }

                if (!trapResult.TryGetObject(out var descriptorObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Proxy getOwnPropertyDescriptor trap result must be object or undefined");

                hasDescriptor = true;
                var hasGet = descriptorObj.TryGetPropertyAtom(realm, IdGet, out _, out _);
                var hasSet = descriptorObj.TryGetPropertyAtom(realm, IdSet, out _, out _);
                isAccessor = hasGet || hasSet;
                return;
            }

            hasDescriptor = ProxyCore.TryGetOwnDescriptorKindFromTarget(realm, target, key, out isAccessor);
        }

        internal void SealDataPropertiesViaProxy(JsRealm realm)
        {
            var keys = OwnKeysHelpers.CollectForProxy(realm, proxy.ProxyOwner);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                proxy.GetOwnPropertyDescriptorKindViaProxy(realm, key, out var hasDesc, out _);
                if (!hasDesc)
                    continue;
                var descObj = new JsPlainObject(realm);
                descObj.DefineDataPropertyAtom(realm, IdConfigurable, JsValue.False, JsShapePropertyFlags.Open);
                proxy.DefinePropertyFromDescriptorObject(realm, key, descObj);
            }
        }

        internal void FreezeDataPropertiesViaProxy(JsRealm realm)
        {
            var keys = OwnKeysHelpers.CollectForProxy(realm, proxy.ProxyOwner);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                proxy.GetOwnPropertyDescriptorKindViaProxy(realm, key, out var hasDesc, out var isAccessor);
                if (!hasDesc)
                    continue;
                var descObj = new JsPlainObject(realm);
                descObj.DefineDataPropertyAtom(realm, IdConfigurable, JsValue.False, JsShapePropertyFlags.Open);
                if (!isAccessor)
                    descObj.DefineDataPropertyAtom(realm, IdWritable, JsValue.False, JsShapePropertyFlags.Open);
                proxy.DefinePropertyFromDescriptorObject(realm, key, descObj);
            }
        }

        internal bool TryDefinePropertyFromDescriptorObject(JsRealm realm, in JsValue key,
            JsPlainObject descriptorObject)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            const int atomDefineProperty = IdDefineProperty;
            if (handler.TryGetPropertyAtom(realm, atomDefineProperty, out var trap, out _) &&
                !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy defineProperty trap is not a function");

                var args = new InlineJsValueArray3
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = JsValue.FromObject(descriptorObject)
                };
                var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                if (!DescriptorUtilities.ToBooleanForDescriptor(trapResult))
                    return false;
                ValidateDefinePropertyTrapSuccess(realm, target, key, descriptorObject);
                return true;
            }

            if (target is IProxyObject nestedProxy)
                return nestedProxy.TryDefinePropertyFromDescriptorObject(realm, key, descriptorObject);

            const int atomObjectDefineProperty = IdDefineProperty;
            if (!realm.ObjectConstructor.TryGetPropertyAtom(realm, atomObjectDefineProperty, out var methodValue,
                    out _) ||
                !methodValue.TryGetObject(out var methodObj) || methodObj is not JsFunction methodFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Object.defineProperty is not callable");

            var defineArgs = new InlineJsValueArray3
            {
                Item0 = JsValue.FromObject(target),
                Item1 = key,
                Item2 = JsValue.FromObject(descriptorObject)
            };

            try
            {
                _ = realm.InvokeFunction(methodFn, JsValue.FromObject(realm.ObjectConstructor), defineArgs.AsSpan());
                return true;
            }
            catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.TypeError)
            {
                return false;
            }
        }

        internal void DefinePropertyFromDescriptorObject(JsRealm realm, in JsValue key,
            JsPlainObject descriptorObject)
        {
            if (proxy.TryDefinePropertyFromDescriptorObject(realm, key, descriptorObject))
                return;

            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy defineProperty trap returned false");
        }

        internal bool TryDefineOwnDataPropertyForSet(JsRealm realm, int atom, JsValue value,
            out SlotInfo slotInfo)
        {
            var key = GetPropertyKey(realm, atom);
            var hasDescriptor = proxy.Core.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var descriptorValue) &&
                                !descriptorValue.IsUndefined;
            slotInfo = SlotInfo.Invalid;
            return proxy.TryDefinePropertyViaProxyOrTargetForSet(realm, key, value, hasDescriptor);
        }

        internal bool TryDefineOwnDataPropertyForSet(JsRealm realm, uint index, JsValue value,
            out SlotInfo slotInfo)
        {
            var key = JsValue.FromString(index.ToString(CultureInfo.InvariantCulture));
            var hasDescriptor = proxy.Core.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var descriptorValue) &&
                                !descriptorValue.IsUndefined;
            slotInfo = SlotInfo.Invalid;
            return proxy.TryDefinePropertyViaProxyOrTargetForSet(realm, key, value, hasDescriptor);
        }

        internal bool TryDefinePropertyViaProxyOrTargetForSet(JsRealm realm, in JsValue key,
            in JsValue value, bool hasDescriptor)
        {
            var target = proxy.Core.EnsureTarget(realm);
            var handler = proxy.Core.CurrentHandler!;
            var descriptorObject = new JsPlainObject(realm);
            descriptorObject.DefineDataPropertyAtom(realm, IdValue, value, JsShapePropertyFlags.Open);
            if (!hasDescriptor)
            {
                descriptorObject.DefineDataPropertyAtom(realm, IdWritable, JsValue.True, JsShapePropertyFlags.Open);
                descriptorObject.DefineDataPropertyAtom(realm, IdEnumerable, JsValue.True, JsShapePropertyFlags.Open);
                descriptorObject.DefineDataPropertyAtom(realm, IdConfigurable, JsValue.True, JsShapePropertyFlags.Open);
            }

            const int atomDefineProperty = IdDefineProperty;
            if (handler.TryGetPropertyAtom(realm, atomDefineProperty, out var trap, out _) &&
                !trap.IsUndefined && !trap.IsNull)
            {
                if (!trap.TryGetObject(out var trapObj) || trapObj is not JsFunction trapFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy defineProperty trap is not a function");

                var args = new InlineJsValueArray3
                {
                    Item0 = JsValue.FromObject(target),
                    Item1 = key,
                    Item2 = JsValue.FromObject(descriptorObject)
                };
                var trapResult = realm.InvokeFunction(trapFn, JsValue.FromObject(handler), args.AsSpan());
                if (!DescriptorUtilities.ToBooleanForDescriptor(trapResult))
                    return false;
                ValidateDefinePropertyTrapSuccess(realm, target, key, descriptorObject);
                return true;
            }

            return ProxyOperations.TryDefinePropertyViaProxyOrTargetForSet(realm, target, key, value, hasDescriptor);
        }
    }
}

internal static class ProxyOperations
{
    internal static bool TryDefinePropertyViaProxyOrTargetForSet(JsRealm realm, JsObject target, in JsValue key,
        in JsValue value, bool hasDescriptor)
    {
        if (target is IProxyObject proxy)
            return proxy.TryDefinePropertyViaProxyOrTargetForSet(realm, key, value, hasDescriptor);

        return ProxyDescriptorUtilities.TryDefineOwnDataPropertyForSet(realm, target, key, value, hasDescriptor);
    }
}
