using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private readonly object privateBrandTokenGate = new();

    private readonly ConditionalWeakTable<JsObject, PrivateMemberStorage> privateMemberStorageByObject = new();
    private Dictionary<int, JsObject>? legacyPrivateBrandTokens;

    internal JsObject GetLegacyPrivateBrandToken(int brandId)
    {
        if (brandId < 0)
            throw new ArgumentOutOfRangeException(nameof(brandId));

        lock (privateBrandTokenGate)
        {
            legacyPrivateBrandTokens ??= new();
            if (legacyPrivateBrandTokens.TryGetValue(brandId, out var token))
                return token;

            token = new JsPrivateBrandTokenObject(MainRealm);
            legacyPrivateBrandTokens[brandId] = token;
            return token;
        }
    }

    internal void InitPrivateField(JsObject target, JsObject brandToken, int slotIndex, in JsValue value)
    {
        ref var slot = ref GetUninitializedPrivateSlot(target, brandToken, slotIndex);
        slot = value;
    }

    internal void InitPrivateAccessor(
        JsObject target,
        JsObject brandToken,
        int slotIndex,
        JsFunction? getter,
        JsFunction? setter)
    {
        ref var slot = ref GetUninitializedPrivateSlot(target, brandToken, slotIndex);
        slot = new JsPrivateAccessorDescriptor(target.Realm, getter, setter);
    }

    internal void InitPrivateMethod(JsObject target, JsObject brandToken, int slotIndex, JsFunction method)
    {
        ref var slot = ref GetUninitializedPrivateSlot(target, brandToken, slotIndex);
        slot = new JsPrivateMethodDescriptor(target.Realm, method);
    }

    internal bool TryGetPrivateSlot(JsObject target, JsObject brandToken, int slotIndex, out JsValue value)
    {
        value = JsValue.Undefined;
        if (slotIndex < 0)
            return false;
        if (!privateMemberStorageByObject.TryGetValue(target, out var storage) ||
            !storage.TryGetSlots(brandToken, out var slots) ||
            (uint)slotIndex >= (uint)slots.Length)
            return false;

        value = slots[slotIndex];
        return !value.IsTheHole;
    }

    internal bool TrySetPrivateField(JsObject target, JsObject brandToken, int slotIndex, in JsValue value)
    {
        if (slotIndex < 0)
            return false;
        if (!privateMemberStorageByObject.TryGetValue(target, out var storage) ||
            !storage.TryGetSlots(brandToken, out var slots) ||
            (uint)slotIndex >= (uint)slots.Length)
            return false;

        ref var slot = ref slots[slotIndex];
        if (slot.IsTheHole)
            return false;
        if (slot.TryGetObject(out var memberObj) &&
            memberObj is JsPrivateAccessorDescriptor or JsPrivateMethodDescriptor)
            return false;

        slot = value;
        return true;
    }

    private ref JsValue GetUninitializedPrivateSlot(JsObject target, JsObject brandToken, int slotIndex)
    {
        if (slotIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        var slots = privateMemberStorageByObject.GetValue(target, static _ => new())
            .GetOrCreateSlots(brandToken, slotIndex + 1);
        ref var slot = ref slots[slotIndex];
        if (!slot.IsTheHole)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot initialize private field twice on the same object", "PRIVATE_FIELD_REINIT");

        return ref slot;
    }

    private sealed class PrivateMemberStorage
    {
        private Dictionary<JsObject, JsValue[]>? additionalSlotsByBrandToken;
        private JsObject? primaryBrandToken;
        private JsValue[]? primarySlots;

        internal JsValue[] GetOrCreateSlots(JsObject brandToken, int minimumLength)
        {
            if (minimumLength <= 0)
                minimumLength = 1;

            if (ReferenceEquals(primaryBrandToken, brandToken))
            {
                primarySlots = GrowSlots(primarySlots, minimumLength);
                return primarySlots;
            }

            if (primaryBrandToken is null)
            {
                primaryBrandToken = brandToken;
                primarySlots = GrowSlots(null, minimumLength);
                return primarySlots;
            }

            additionalSlotsByBrandToken ??= new();
            if (!additionalSlotsByBrandToken.TryGetValue(brandToken, out var slots))
            {
                slots = GrowSlots(null, minimumLength);
                additionalSlotsByBrandToken[brandToken] = slots;
                return slots;
            }

            slots = GrowSlots(slots, minimumLength);
            additionalSlotsByBrandToken[brandToken] = slots;
            return slots;
        }

        internal bool TryGetSlots(JsObject brandToken, out JsValue[] slots)
        {
            if (ReferenceEquals(primaryBrandToken, brandToken) && primarySlots is not null)
            {
                slots = primarySlots;
                return true;
            }

            if (additionalSlotsByBrandToken is not null &&
                additionalSlotsByBrandToken.TryGetValue(brandToken, out slots!))
                return true;

            slots = Array.Empty<JsValue>();
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue[] GrowSlots(JsValue[]? slots, int minimumLength)
        {
            if (slots is null)
            {
                slots = new JsValue[minimumLength];
                slots.AsSpan().Fill(JsValue.TheHole);
                return slots;
            }

            if (slots.Length >= minimumLength)
                return slots;

            var oldLength = slots.Length;
            Array.Resize(ref slots, minimumLength);
            slots.AsSpan(oldLength).Fill(JsValue.TheHole);
            return slots;
        }
    }

    private sealed class JsPrivateBrandTokenObject : JsObject
    {
        internal JsPrivateBrandTokenObject(JsRealm realm)
            : base(realm)
        {
            Prototype = null;
            IsExtensibleFlag = false;
        }
    }
}
