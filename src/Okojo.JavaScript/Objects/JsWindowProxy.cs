namespace Okojo.JavaScript.Objects;

/// <summary>Host integration primitive for a same-agent, navigation-stable window reference.
/// Browser origin policy and browsing-context lifetime remain the host's responsibility.</summary>
public sealed class JsWindowProxy : JsObject, IProxyObject
{
    private ProxyCore core;
    private IWindowAccessPolicy? accessPolicy;

    private readonly JsObject handler;

    /// <summary>Creates a detached host window reference in a long-lived anchor realm.</summary>
    public JsWindowProxy(JsRealm ownerRealm)
        : base(ownerRealm)
    {
        handler = new JsPlainObject(ownerRealm) { Prototype = null };
        handler.SetProperty(
            "preventExtensions",
            new JsHostFunction(ownerRealm, (in CallInfo _) => JsValue.False, "preventExtensions", 1)
        );
        handler.SetProperty(
            "setPrototypeOf",
            new JsHostFunction(
                ownerRealm,
                (in CallInfo info) =>
                {
                    var current = EnsureTarget(info.Realm).GetPrototypeOf(info.Realm);
                    var proposed = info.GetArgument(1);
                    return (
                        proposed.IsNull
                            ? current is null
                            : proposed.TryGetObject(out var obj) && ReferenceEquals(current, obj)
                    )
                        ? JsValue.True
                        : JsValue.False;
                },
                "setPrototypeOf",
                2
            )
        );
    }

    /// <summary>The currently attached realm, or null when access is disabled.</summary>
    public JsRealm? TargetRealm => core.CurrentTarget?.Realm;

    /// <summary>Retargets this reference. Null disables access and releases the old target.</summary>
    public void SetTarget(JsRealm? target)
    {
        if (target is not null && !ReferenceEquals(target.Agent, Realm.Agent))
            throw new ArgumentException(
                "A window target must belong to the same agent.",
                nameof(target)
            );
        core = target is null ? default : new ProxyCore(target.GlobalObject, handler, accessPolicy);
    }

    /// <summary>
    /// Installs the host's cross-origin member policy. Null restores unrestricted access.
    /// </summary>
    public void SetAccessPolicy(IWindowAccessPolicy? policy)
    {
        accessPolicy = policy;
        core.AccessPolicy = policy;
    }

    private void EnsureAccess(JsRealm realm, string? member) => core.EnsureAccess(realm, member);

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
        EnsureAccess(realm, null);
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

    internal override bool TryGetPropertyAtomWithReceiverValue(
        JsRealm realm,
        in JsValue receiverValue,
        int atom,
        out JsValue value,
        out SlotInfo slotInfo
    )
    {
        var found = this.TryGetPropertyAtomViaProxy(realm, receiverValue, atom, out value, out _);
        slotInfo = SlotInfo.Invalid;
        return found;
    }

    internal override bool SetPropertyAtomWithReceiver(
        JsRealm realm,
        JsObject receiver,
        int atom,
        JsValue value,
        out SlotInfo slotInfo
    )
    {
        var result = this.SetPropertyAtomWithReceiverViaProxy(realm, receiver, atom, value, out _);
        slotInfo = SlotInfo.Invalid;
        return result;
    }

    internal override bool TryGetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        out JsValue value
    )
    {
        EnsureAccess(realm, index.ToString());
        return EnsureTarget(realm).TryGetElementWithReceiver(realm, receiver, index, out value);
    }

    internal override bool SetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        JsValue value
    )
    {
        EnsureAccess(realm, index.ToString());
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

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(
        JsRealm realm,
        int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true
    )
    {
        EnsureAccess(realm, atom < 0 ? null : realm.Atoms.AtomToString(atom));
        var target = EnsureTarget();
        return target.TryGetOwnNamedPropertyDescriptorAtom(
            realm,
            atom,
            out descriptor,
            needDescriptor
        );
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
        List<string> enumerableKeysOut
    )
    {
        EnsureAccess(realm, null);
        var target = EnsureTarget();
        target.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override bool TrySetPrototypeCore(JsObject? proto)
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
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Proxy preventExtensions trap returned false"
            );
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
