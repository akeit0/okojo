using Okojo.Internals;

namespace Okojo.Objects;

internal sealed class JsProxyFunction : JsFunction, IProxyObject
{
    private ProxyCore core;

    internal JsProxyFunction(JsRealm realm, JsObject target, JsObject handler, bool isConstructor)
        : base(realm, string.Empty, length: 0, isConstructor: isConstructor)
    {
        core = new(target, handler);
        Prototype = target.Prototype;
    }

    private JsObject? ProxyHandler => core.CurrentHandler;

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

    private JsFunction EnsureCallableTarget()
    {
        return EnsureCallableTarget(Realm);
    }

    private JsFunction EnsureCallableTarget(JsRealm errorRealm)
    {
        return core.EnsureCallableTarget(errorRealm);
    }

    internal JsValue InvokeProxy(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args)
    {
        var target = EnsureCallableTarget(realm);
        var handler = ProxyHandler!;
        const int atomApply = IdApply;
        if (!TryGetFunctionTrap(realm, handler, atomApply, out var trapFn))
            return realm.InvokeFunction(target, thisValue, args);

        var argArray = realm.CreateArrayFromArgumentWindow(args);
        var trapArgs = new InlineJsValueArray3
        {
            Item0 = target,
            Item1 = thisValue,
            Item2 = argArray
        };
        return realm.InvokeFunction(trapFn, handler, trapArgs.AsSpan());
    }

    internal JsValue InvokeProxyFromStack(JsRealm realm, JsValue thisValue, int argOffset, int argCount,
        int callerPc)
    {
        return DispatchProxyFromStack(realm, thisValue, argOffset, argCount, JsValue.Undefined, callerPc,
            false);
    }

    internal JsValue ConstructProxy(JsRealm realm, ReadOnlySpan<JsValue> args, JsValue newTarget, int callerPc)
    {
        if (!IsConstructor)
            throw new JsRuntimeException(JsErrorKind.TypeError, "constructor is not a function", "NOT_CONSTRUCTOR");

        var target = EnsureCallableTarget(realm);
        var handler = ProxyHandler!;
        const int atomConstruct = IdConstruct;
        if (!TryGetFunctionTrap(realm, handler, atomConstruct, out var trapFn))
            return realm.ConstructWithExplicitNewTarget(target, args, newTarget, callerPc);

        var argArray = realm.CreateArrayFromArgumentWindow(args);
        var trapArgs = new InlineJsValueArray3
        {
            Item0 = target,
            Item1 = argArray,
            Item2 = newTarget
        };
        var trapResult = realm.InvokeFunction(trapFn, handler, trapArgs.AsSpan());
        if (!trapResult.IsObject)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy construct trap must return an object");

        return trapResult;
    }

    internal JsValue ConstructProxyFromStack(JsRealm realm, int argOffset, int argCount, JsValue newTarget,
        int callerPc)
    {
        if (!IsConstructor)
            throw new JsRuntimeException(JsErrorKind.TypeError, "constructor is not a function", "NOT_CONSTRUCTOR");

        return DispatchProxyFromStack(realm, JsValue.Undefined, argOffset, argCount, newTarget, callerPc,
            true);
    }

    internal JsValue DispatchProxyFromStack(
        JsRealm realm,
        JsValue thisValue,
        int argOffset,
        int argCount,
        JsValue newTarget,
        int callerPc,
        bool isConstruct)
    {
        var target = EnsureCallableTarget(realm);
        var handler = ProxyHandler!;
        var trapAtom = isConstruct ? IdConstruct : IdApply;
        if (!TryGetFunctionTrap(realm, handler, trapAtom, out var trapFn))
        {
            if (!isConstruct)
                return realm.DispatchCallFromStack(target, thisValue, argOffset, argCount, callerPc);

            var prepared = realm.PrepareConstructInvocation(target, newTarget);
            return realm.DispatchConstructFromStack(target, prepared, argOffset, argCount, callerPc);
        }

        var argArray = realm.CreateArrayFromArgumentWindow(argOffset, argCount);
        var trapArgs = new InlineJsValueArray3
        {
            Item0 = target,
            Item1 = isConstruct ? argArray : thisValue,
            Item2 = isConstruct ? newTarget : argArray
        };
        var trapResult = realm.InvokeFunction(trapFn, handler, trapArgs.AsSpan());
        if (isConstruct && !trapResult.IsObject)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Proxy construct trap must return an object");

        return trapResult;
    }

    internal override JsValue InvokeNonBytecodeCall(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        int callerPc)
    {
        return InvokeProxy(realm, thisValue, args);
    }

    internal override JsValue InvokeNonBytecodeConstruct(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        JsValue newTarget, int callerPc, CallFrameFlag flags)
    {
        return ConstructProxy(realm, args, newTarget, callerPc);
    }

    private static bool TryGetFunctionTrap(JsRealm realm, JsObject handler, int atom, out JsFunction trapFn)
    {
        if (!handler.TryGetPropertyAtom(realm, atom, out var trapValue, out _) || trapValue.IsUndefined ||
            trapValue.IsNull)
        {
            trapFn = null!;
            return false;
        }

        if (!trapValue.TryGetObject(out var trapObj) || trapObj is not JsFunction fn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy trap is not a function");

        trapFn = fn;
        return true;
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
