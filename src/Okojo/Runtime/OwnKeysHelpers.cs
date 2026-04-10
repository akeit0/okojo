using System.Globalization;

namespace Okojo.Runtime;

internal static class OwnKeysHelpers
{
    internal static List<JsValue> CollectForProxy(JsRealm realm, JsObject proxy)
    {
        if (proxy.TryGetOwnKeysTrapKeys(realm, out var trapKeys))
        {
            if (proxy.TryGetProxyTarget(out var trapTarget))
                return ValidateProxyTrapKeys(realm, trapTarget, trapKeys ?? new List<JsValue>());
            return trapKeys ?? new List<JsValue>();
        }

        if (proxy.TryGetProxyTarget(out var proxyTarget))
        {
            if (proxyTarget.TryGetProxyTarget(out _) ||
                proxyTarget.TryGetOwnKeysTrapKeys(realm, out _))
                return CollectForProxy(realm, proxyTarget);
            return CollectOrdinaryOwnPropertyKeys(realm, proxyTarget);
        }

        return CollectOrdinaryOwnPropertyKeys(realm, proxy);
    }

    internal static List<JsValue> CollectOrdinaryOwnPropertyKeys(JsRealm realm, JsObject target)
    {
        var keys = new List<JsValue>(16);
        var seenStrings = new HashSet<string>(StringComparer.Ordinal);
        var seenSymbols = new HashSet<int>();
        var symbolAtoms = new List<int>(4);

        var indices = new List<uint>(8);
        target.CollectOwnElementIndices(indices, false);
        if (indices.Count != 0)
            indices.Sort();
        for (var i = 0; i < indices.Count; i++)
        {
            var text = indices[i].ToString(CultureInfo.InvariantCulture);
            if (seenStrings.Add(text))
                keys.Add(JsValue.FromString(text));
        }

        var namedAtoms = new List<int>(8);
        target.CollectOwnNamedPropertyAtoms(realm, namedAtoms, false);
        for (var i = 0; i < namedAtoms.Count; i++)
        {
            var atom = namedAtoms[i];
            if (atom < 0)
            {
                if (seenSymbols.Add(atom))
                    symbolAtoms.Add(atom);
                continue;
            }

            var text = realm.Atoms.AtomToString(atom);
            if (seenStrings.Add(text))
                keys.Add(JsValue.FromString(text));
        }

        if (target is JsGlobalObject global)
            foreach (var pair in global.EnumerateNamedGlobalDescriptors())
            {
                var atom = pair.Key;
                if (atom < 0)
                {
                    if (seenSymbols.Add(atom))
                        symbolAtoms.Add(atom);
                    continue;
                }

                var text = realm.Atoms.AtomToString(atom);
                if (seenStrings.Add(text))
                    keys.Add(JsValue.FromString(text));
            }

        for (var i = 0; i < symbolAtoms.Count; i++)
        {
            var atom = symbolAtoms[i];
            var sym = realm.Agent.TryGetRegisteredSymbolByAtom(atom, out var registered)
                ? registered
                : realm.Atoms.TryGetSymbolByAtom(atom, out var existing)
                    ? existing
                    : new(atom, realm.Atoms.AtomToString(atom));
            keys.Add(JsValue.FromSymbol(sym));
        }

        return keys;
    }

    private static List<JsValue> ValidateProxyTrapKeys(JsRealm realm, JsObject target, List<JsValue> trapKeys)
    {
        var seen = new HashSet<JsValue>(JsValueSameValueZeroComparer.Instance);
        for (var i = 0; i < trapKeys.Count; i++)
        {
            var key = trapKeys[i];
            if (!key.IsString && !key.IsSymbol)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy ownKeys trap result entries must be property keys");
            if (!seen.Add(key))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy ownKeys trap returned duplicate keys");
        }

        var targetKeys = target.TryGetProxyTarget(out _) ||
                         target.TryGetOwnKeysTrapKeys(realm, out _)
            ? CollectForProxy(realm, target)
            : CollectOrdinaryOwnPropertyKeys(realm, target);

        if (target.IsExtensible)
        {
            for (var i = 0; i < targetKeys.Count; i++)
            {
                var key = targetKeys[i];
                if (!TryGetOwnPropertyConfigurability(realm, target, key, out var configurable))
                    continue;
                if (!configurable && !seen.Contains(key))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Proxy ownKeys trap result is missing a non-configurable target key");
            }

            return trapKeys;
        }

        var targetKeySet = new HashSet<JsValue>(targetKeys, JsValueSameValueZeroComparer.Instance);
        for (var i = 0; i < targetKeys.Count; i++)
            if (!seen.Contains(targetKeys[i]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy ownKeys trap result is missing a target key for a non-extensible target");

        for (var i = 0; i < trapKeys.Count; i++)
            if (!targetKeySet.Contains(trapKeys[i]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy ownKeys trap result contains a new key for a non-extensible target");

        return trapKeys;
    }

    internal static bool TryGetOwnPropertyConfigurability(JsRealm realm, JsObject target, in JsValue key,
        out bool configurable)
    {
        if (target.TryGetOwnPropertyDescriptorViaTrap(realm, key, out var descriptorValue))
        {
            if (descriptorValue.IsUndefined)
            {
                configurable = true;
                return false;
            }

            if (!descriptorValue.TryGetObject(out var descriptorObject))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Proxy getOwnPropertyDescriptor trap result must be object or undefined");

            configurable = false;
            if (descriptorObject.TryGetProperty("configurable", out var configurableValue))
                configurable = DescriptorUtilities.ToBooleanForDescriptor(configurableValue);
            return true;
        }

        if (key.IsSymbol)
        {
            var atom = key.AsSymbol().Atom;
            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var symbolDescriptor))
            {
                configurable = symbolDescriptor.Configurable;
                return true;
            }

            if (target is JsGlobalObject symbolGlobal &&
                symbolGlobal.TryGetOwnGlobalDescriptorAtom(atom, out var globalSymbolDescriptor))
            {
                configurable = globalSymbolDescriptor.Configurable;
                return true;
            }

            configurable = true;
            return false;
        }

        var name = key.AsString();
        if (TryGetArrayIndexFromCanonicalString(name, out var index) &&
            target.TryGetOwnElementDescriptor(index, out var elementDescriptor))
        {
            configurable = elementDescriptor.Configurable;
            return true;
        }

        if (realm.Atoms.TryGetInterned(name, out var nameAtom))
        {
            if (target.TryGetOwnNamedPropertyDescriptorAtom(realm, nameAtom, out var namedDescriptor))
            {
                configurable = namedDescriptor.Configurable;
                return true;
            }

            if (target is JsGlobalObject global &&
                global.TryGetOwnGlobalDescriptorAtom(nameAtom, out var globalDescriptor))
            {
                configurable = globalDescriptor.Configurable;
                return true;
            }
        }

        configurable = true;
        return false;
    }
}
