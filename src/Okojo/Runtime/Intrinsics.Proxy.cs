namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateProxyConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Proxy constructor requires 'new'");

            if (args.Length < 2 || !args[0].TryGetObject(out var target) || !args[1].TryGetObject(out var handler))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot create proxy with a non-object as target or handler");

            if (IsCallableTarget(target))
                return new JsProxyFunction(realm, target, handler, IsConstructorTarget(target));

            return new JsProxyObject(realm, target, handler);
        }, "Proxy", 2, true);
    }

    private void InstallProxyConstructorBuiltins()
    {
        const int atomRevocable = IdRevocable;
        const int atomProxy = IdProxy;
        const int atomRevoke = IdRevoke;

        var revocableFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length < 2 || !args[0].TryGetObject(out var target) || !args[1].TryGetObject(out var handler))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot create proxy with a non-object as target or handler");

            JsObject proxy = IsCallableTarget(target)
                ? new JsProxyFunction(realm, target, handler, IsConstructorTarget(target))
                : new JsProxyObject(realm, target, handler);
            var revokeFn = new JsHostFunction(realm, static (in info) =>
            {
                var callee = (JsHostFunction)info.Function;
                if (callee.UserData is IProxyObject proxy)
                {
                    proxy.RevokeProxy();
                    callee.UserData = null;
                }

                return JsValue.Undefined;
            }, string.Empty, 0);
            revokeFn.UserData = proxy;

            var result = new JsPlainObject(realm);
            result.DefineDataPropertyAtom(realm, atomProxy, proxy, JsShapePropertyFlags.Open);
            result.DefineDataPropertyAtom(realm, atomRevoke, revokeFn, JsShapePropertyFlags.Open);
            return result;
        }, "revocable", 2);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Const(IdLength, ProxyConstructor.Length, configurable: true),
            PropertyDefinition.Const(IdName, ProxyConstructor.Name, configurable: true),
            PropertyDefinition.Mutable(atomRevocable, revocableFn)
        ];
        ProxyConstructor.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private static bool IsCallableTarget(JsObject target)
    {
        return target is JsFunction;
    }

    private static bool IsConstructorTarget(JsObject target)
    {
        return target switch
        {
            JsFunction fn => fn.IsConstructor,
            _ => false
        };
    }
}
