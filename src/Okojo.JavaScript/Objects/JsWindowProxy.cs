namespace Okojo.JavaScript.Objects;

/// <summary>
/// Host-supplied cross-origin window access policy. The engine knows realm identity only, so the
/// host decides which realms may reach a window's members and supplies the value thrown otherwise.
/// </summary>
public interface IWindowAccessPolicy
{
    /// <summary>
    /// Decides access for <paramref name="accessingRealm"/>. <paramref name="member"/> is null for
    /// enumeration, prototype, and extensibility operations.
    /// </summary>
    WindowAccessDecision CheckMemberAccess(JsRealm accessingRealm, string? member);
}

/// <summary>Access decision: allowed, or denied with the value thrown to the accessing realm.</summary>
public readonly record struct WindowAccessDecision(bool Allowed, JsValue ThrownValue)
{
    public static WindowAccessDecision Allow => new(true, JsValue.Undefined);

    public static WindowAccessDecision Deny(JsValue thrown) => new(false, thrown);
}

/// <summary>
/// Host-supplied source of a window's dynamic indexed and named properties: child browsing contexts
/// and document named elements. The engine consults it only for a property the attached target does
/// not already provide, so declared globals and prototypes take precedence.
/// </summary>
public interface IWindowPropertyResolver
{
    /// <summary>Resolves the child browsing context at <paramref name="index"/>, in tree order.</summary>
    bool TryGetIndexed(uint index, out JsValue value);

    /// <summary>Resolves a named child browsing context or document element.</summary>
    bool TryGetNamed(string name, out JsValue value);

    /// <summary>Appends the currently addressable child indices in tree order.</summary>
    void CollectIndices(List<uint> indices);

    /// <summary>Appends the currently addressable names in tree order.</summary>
    void CollectNames(List<string> names);
}

/// <summary>Host integration primitive for a same-agent, navigation-stable window reference.
/// Browser origin policy and browsing-context lifetime remain the host's responsibility.</summary>
public sealed class JsWindowProxy : JsObject, IProxyObject
{
    private ProxyCore core;
    private IWindowAccessPolicy? accessPolicy;
    private IWindowPropertyResolver? propertyResolver;

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
        core = target is null ? default : new ProxyCore(target.GlobalObject, handler);
    }

    /// <summary>
    /// Installs the host's cross-origin member policy. Null restores unrestricted access. The policy
    /// is evaluated only for accesses from a realm other than the attached target, and a member name
    /// is materialized only when a policy exists and would deny; unrestricted access pays one
    /// reference check.
    /// </summary>
    public void SetAccessPolicy(IWindowAccessPolicy? policy) => accessPolicy = policy;

    /// <summary>
    /// Installs the host's dynamic indexed/named property source for this window. Null removes it.
    /// The resolver is consulted only when the attached target does not already provide the
    /// property, so globals declared by scripts keep precedence over child windows and named
    /// elements. A resolver is host state and must not outlive the document realm that installs it.
    /// </summary>
    public void SetPropertyResolver(IWindowPropertyResolver? resolver) => propertyResolver = resolver;

    // Guarded entry points, called by the proxy dispatch layer only when the receiver is a window
    // proxy. They are no-ops (and resolve no names) for the target's own realm.

    internal void EnsureMemberAccess(JsRealm realm, int atom)
    {
        if (accessPolicy is null || !NeedsPolicy(realm))
            return;
        CheckAccess(realm, atom < 0 ? null : realm.Atoms.AtomToString(atom));
    }

    internal void EnsureMemberAccess(JsRealm realm, in JsValue key)
    {
        if (accessPolicy is null || !NeedsPolicy(realm))
            return;
        CheckAccess(realm, MemberNameOfKey(key));
    }

    internal void EnsureControlAccess(JsRealm realm)
    {
        if (accessPolicy is null || !NeedsPolicy(realm))
            return;
        CheckAccess(realm, member: null);
    }

    internal void EnsureIndexAccess(JsRealm realm, uint index)
    {
        if (accessPolicy is null || !NeedsPolicy(realm))
            return;
        CheckAccess(realm, index.ToString());
    }

    private bool NeedsPolicy(JsRealm realm) =>
        !ReferenceEquals(realm, core.CurrentTarget?.Realm);

    private void CheckAccess(JsRealm realm, string? member)
    {
        var decision = accessPolicy!.CheckMemberAccess(realm, member);
        if (decision.Allowed)
            return;
        throw new JsRuntimeException(
            JsErrorKind.TypeError,
            "Cross-origin window access is denied.",
            thrownValue: decision.ThrownValue,
            errorRealm: realm
        );
    }

    private static string? MemberNameOfKey(in JsValue key) =>
        key.IsSymbol ? null
        : key.IsString ? key.AsString()
        : key.IsNumber ? JsValue.NumberToJsString(key.NumberValue)
        : null;

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
        EnsureControlAccess(realm);
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
        slotInfo = SlotInfo.Invalid;
        if (this.TryGetPropertyAtomViaProxy(realm, receiverValue, atom, out value, out _))
            return true;
        if (propertyResolver is null || atom < 0)
        {
            value = JsValue.Undefined;
            return false;
        }
        var name = realm.Atoms.AtomToString(atom);
        if (propertyResolver.TryGetNamed(name, out value))
            return true;
        value = JsValue.Undefined;
        return false;
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
        EnsureIndexAccess(realm, index);
        if (EnsureTarget(realm).TryGetElementWithReceiver(realm, receiver, index, out value))
            return true;
        if (propertyResolver is not null && propertyResolver.TryGetIndexed(index, out value))
            return true;
        value = JsValue.Undefined;
        return false;
    }

    internal override bool SetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        JsValue value
    )
    {
        EnsureIndexAccess(realm, index);
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
        if (target.TryGetOwnElementDescriptor(index, out descriptor))
            return true;
        if (propertyResolver is not null && propertyResolver.TryGetIndexed(index, out var value))
        {
            descriptor = PropertyDescriptor.Data(
                value,
                writable: false,
                enumerable: true,
                configurable: true
            );
            return true;
        }
        descriptor = default;
        return false;
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(
        JsRealm realm,
        int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true
    )
    {
        EnsureMemberAccess(realm, atom);
        var target = EnsureTarget();
        if (
            target.TryGetOwnNamedPropertyDescriptorAtom(
                realm,
                atom,
                out descriptor,
                needDescriptor
            )
        )
            return true;
        if (propertyResolver is null || atom < 0)
        {
            descriptor = default;
            return false;
        }
        var name = realm.Atoms.AtomToString(atom);
        if (!propertyResolver.TryGetNamed(name, out var value))
        {
            descriptor = default;
            return false;
        }
        descriptor = needDescriptor
            ? PropertyDescriptor.Data(
                value,
                writable: false,
                enumerable: true,
                configurable: true
            )
            : default;
        return true;
    }

    /// <summary>Appends the resolver's dynamic keys to an own-keys result.</summary>
    internal void AppendResolvedKeys(JsRealm realm, List<JsValue> keys)
    {
        if (propertyResolver is null)
            return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (key.IsString)
                seen.Add(key.AsString());
        var indices = new List<uint>();
        propertyResolver.CollectIndices(indices);
        indices.Sort();
        foreach (var index in indices)
        {
            var text = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (seen.Add(text))
                keys.Add(JsValue.FromString(text));
        }
        var names = new List<string>();
        propertyResolver.CollectNames(names);
        foreach (var name in names)
            if (seen.Add(name))
                keys.Add(JsValue.FromString(name));
    }

    /// <summary>Whether the resolver currently provides <paramref name="key"/>.</summary>
    internal bool HasResolvedProperty(JsRealm realm, in JsValue key)
    {
        if (propertyResolver is null || key.IsSymbol)
            return false;
        if (key.IsNumber)
            return propertyResolver.TryGetIndexed((uint)key.NumberValue, out _);
        if (!key.IsString)
            return false;
        var text = key.AsString();
        return uint.TryParse(text, out var index)
            ? propertyResolver.TryGetIndexed(index, out _)
            : propertyResolver.TryGetNamed(text, out _);
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
        propertyResolver?.CollectIndices(indicesOut);
    }

    internal override void CollectOwnNamedPropertyAtoms(
        JsRealm realm,
        List<int> atomsOut,
        bool enumerableOnly
    )
    {
        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
        if (propertyResolver is null)
            return;
        var names = new List<string>();
        propertyResolver.CollectNames(names);
        foreach (var name in names)
            atomsOut.Add(realm.Atoms.InternNoCheck(name));
    }

    internal override void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut
    )
    {
        EnsureControlAccess(realm);
        var target = EnsureTarget();
        target.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
        if (propertyResolver is null)
            return;
        var names = new List<string>();
        propertyResolver.CollectNames(names);
        foreach (var name in names)
            if (visited.Add(name))
                enumerableKeysOut.Add(name);
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
