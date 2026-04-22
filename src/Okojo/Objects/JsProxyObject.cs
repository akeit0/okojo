namespace Okojo.Objects;

internal sealed class JsProxyObject : JsObject, IProxyObject
{
    private ProxyCore core;

    internal JsProxyObject(JsRealm realm, JsObject target, JsObject handler)
        : base(realm)
    {
        core = new(target, handler);
        Prototype = target.Prototype;
    }

    public override bool IsExtensible => EnsureTarget().IsExtensible;
    JsObject IProxyObject.ProxyOwner => this;
    ref ProxyCore IProxyObject.Core => ref core;

    void IProxyObject.RevokeProxy()
    {
        Revoke();
    }

    internal void Revoke()
    {
        core.Revoke(this);
    }

    internal override JsObject? GetPrototypeOf(JsRealm realm)
    {
        return core.GetPrototypeOf(realm);
    }

    private JsObject EnsureTarget()
    {
        return EnsureTarget(Realm);
    }

    private JsObject EnsureTarget(JsRealm errorRealm)
    {
        return core.EnsureTarget(errorRealm);
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        return this.TryGetPropertyAtomViaProxy(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        return this.SetPropertyAtomWithReceiverViaProxy(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool TryGetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, out JsValue value)
    {
        return this.TryGetElementViaProxy(index, receiver, out value);
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        return this.SetElementWithReceiverViaProxy(realm, receiver, index, value);
    }

    public override bool DeleteElement(uint index)
    {
        return this.DeleteElementViaProxy(index);
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        return this.DeletePropertyAtomViaProxy(realm, atom);
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        var target = EnsureTarget();
        return target.TryGetOwnElementDescriptor(index, out descriptor);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        var target = EnsureTarget();
        return target.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        _ = index;
        _ = value;
        hadOwnElement = false;
        return false;
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        var target = EnsureTarget();
        target.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }

    internal override void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        var target = EnsureTarget();
        target.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override bool TrySetPrototype(JsObject? proto)
    {
        return this.SetPrototypeViaProxy(EnsureTarget().Realm, proto);
    }

    internal bool PreventExtensionsViaProxy(JsRealm realm)
    {
        return ((IProxyObject)this).PreventExtensionsViaProxy(realm);
    }

    internal override void PreventExtensions()
    {
        if (!PreventExtensionsViaProxy(EnsureTarget().Realm))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy preventExtensions trap returned false");
    }

    internal override void SealDataProperties()
    {
        this.SealDataPropertiesViaProxy(EnsureTarget().Realm);
    }

    internal override void FreezeDataProperties()
    {
        this.FreezeDataPropertiesViaProxy(EnsureTarget().Realm);
    }

    internal override bool AreAllOwnPropertiesSealed()
    {
        var target = EnsureTarget();
        return target.AreAllOwnPropertiesSealed();
    }

    internal override bool AreAllOwnPropertiesFrozen()
    {
        var target = EnsureTarget();
        return target.AreAllOwnPropertiesFrozen();
    }
}
