using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateDisposableStackConstructor()
    {
        return new(Realm, (in info) =>
        {
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "DisposableStack constructor requires 'new'");

            var callee = (JsHostFunction)info.Function;
            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                DisposableStackPrototype);
            return new JsDisposableStackObject(info.Realm, false, prototype);
        }, "DisposableStack", 0, true);
    }

    private JsHostFunction CreateAsyncDisposableStackConstructor()
    {
        return new(Realm, (in info) =>
        {
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "AsyncDisposableStack constructor requires 'new'");

            var callee = (JsHostFunction)info.Function;
            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                AsyncDisposableStackPrototype);
            return new JsDisposableStackObject(info.Realm, true, prototype);
        }, "AsyncDisposableStack", 0, true);
    }

    private void InstallExplicitResourceManagementBuiltins()
    {
        var atomDisposed = Atoms.InternNoCheck("disposed");
        var atomUse = Atoms.InternNoCheck("use");
        var atomAdopt = Atoms.InternNoCheck("adopt");
        var atomDefer = Atoms.InternNoCheck("defer");
        var atomMove = Atoms.InternNoCheck("move");
        var atomDisposeAsync = Atoms.InternNoCheck("disposeAsync");

        var disposableDisposedGetFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, false, "get DisposableStack.prototype.disposed");
            return stack.IsDisposed ? JsValue.True : JsValue.False;
        }, "get disposed", 0);

        var disposableUseFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var stack = RequireDisposableStack(info.ThisValue, false, "DisposableStack.prototype.use");
            stack.ThrowIfDisposed();
            var value = info.GetArgumentOrDefault(0, JsValue.Undefined);
            if (!value.IsNull && !value.IsUndefined)
            {
                var method = GetDisposeMethod(realm, value, allowAsyncFallback: false);
                if (method.IsUndefined)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "DisposableStack.prototype.use value does not define Symbol.dispose");
                stack.AddResource(new(value, DisposableResourceHint.SyncDispose, method));
            }

            return value;
        }, "use", 1);

        var disposableAdoptFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, false, "DisposableStack.prototype.adopt");
            stack.ThrowIfDisposed();
            var value = info.GetArgumentOrDefault(0, JsValue.Undefined);
            var onDispose = RequireCallableArgument(info, 1, "DisposableStack.prototype.adopt");
            stack.AddResource(new(value, DisposableResourceHint.SyncDispose, JsValue.FromObject(onDispose)));
            return value;
        }, "adopt", 2);

        var disposableDeferFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, false, "DisposableStack.prototype.defer");
            stack.ThrowIfDisposed();
            var onDispose = RequireCallableArgument(info, 0, "DisposableStack.prototype.defer");
            stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.SyncDispose, JsValue.FromObject(onDispose)));
            return JsValue.Undefined;
        }, "defer", 1);

        var disposableDisposeFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var stack = RequireDisposableStack(info.ThisValue, false, "DisposableStack.prototype.dispose");
            if (stack.IsDisposed)
                return JsValue.Undefined;

            stack.State = DisposableStackState.Disposed;
            realm.Intrinsics.DisposeResources(stack.DetachResources());
            return JsValue.Undefined;
        }, "dispose", 0);

        var disposableMoveFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, false, "DisposableStack.prototype.move");
            stack.ThrowIfDisposed();
            var movedResources = stack.DetachResources();
            stack.State = DisposableStackState.Disposed;
            var moved = new JsDisposableStackObject(info.Realm, false, info.Realm.DisposableStackPrototype);
            moved.ReplaceResources(movedResources);
            return moved;
        }, "move", 0);

        var asyncDisposedGetFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, true, "get AsyncDisposableStack.prototype.disposed");
            return stack.IsDisposed ? JsValue.True : JsValue.False;
        }, "get disposed", 0);

        var asyncUseFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.use");
            stack.ThrowIfDisposed();
            var value = info.GetArgumentOrDefault(0, JsValue.Undefined);
            if (value.IsNull || value.IsUndefined)
            {
                stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.AsyncDispose, JsValue.Undefined));
                return value;
            }

            var method = GetDisposeMethod(realm, value, allowAsyncFallback: true);
            if (method.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "AsyncDisposableStack.prototype.use value does not define Symbol.asyncDispose or Symbol.dispose");
            stack.AddResource(new(value, DisposableResourceHint.AsyncDispose, method));
            return value;
        }, "use", 1);

        var asyncAdoptFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.adopt");
            stack.ThrowIfDisposed();
            var value = info.GetArgumentOrDefault(0, JsValue.Undefined);
            var onDispose = RequireCallableArgument(info, 1, "AsyncDisposableStack.prototype.adopt");
            stack.AddResource(new(value, DisposableResourceHint.AsyncDispose, JsValue.FromObject(onDispose)));
            return value;
        }, "adopt", 2);

        var asyncDeferFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.defer");
            stack.ThrowIfDisposed();
            var onDispose = RequireCallableArgument(info, 0, "AsyncDisposableStack.prototype.defer");
            stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.AsyncDispose, JsValue.FromObject(onDispose)));
            return JsValue.Undefined;
        }, "defer", 1);

        var asyncDisposeFn = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.disposeAsync");
            if (stack.IsDisposed)
                return JsValue.Undefined;

            stack.State = DisposableStackState.Disposed;
            return realm.WrapTask(realm.Intrinsics.DisposeResourcesAsync(stack.DetachResources()));
        }, "disposeAsync", 0);

        var asyncMoveFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.move");
            stack.ThrowIfDisposed();
            var movedResources = stack.DetachResources();
            stack.State = DisposableStackState.Disposed;
            var moved = new JsDisposableStackObject(info.Realm, true, info.Realm.AsyncDisposableStackPrototype);
            moved.ReplaceResources(movedResources);
            return moved;
        }, "move", 0);

        DisposableStackConstructor.InitializePrototypeProperty(DisposableStackPrototype);
        AsyncDisposableStackConstructor.InitializePrototypeProperty(AsyncDisposableStackPrototype);

        Span<PropertyDefinition> disposableProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(DisposableStackConstructor)),
            PropertyDefinition.GetterData(atomDisposed, disposableDisposedGetFn, configurable: true),
            PropertyDefinition.Mutable(atomUse, JsValue.FromObject(disposableUseFn)),
            PropertyDefinition.Mutable(atomAdopt, JsValue.FromObject(disposableAdoptFn)),
            PropertyDefinition.Mutable(atomDefer, JsValue.FromObject(disposableDeferFn)),
            PropertyDefinition.Mutable(IdDispose, JsValue.FromObject(disposableDisposeFn)),
            PropertyDefinition.Mutable(atomMove, JsValue.FromObject(disposableMoveFn)),
            PropertyDefinition.Mutable(IdSymbolDispose, JsValue.FromObject(disposableDisposeFn)),
            PropertyDefinition.Mutable(IdSymbolToStringTag, JsValue.FromString("DisposableStack"))
        ];
        DisposableStackPrototype.DefineNewPropertiesNoCollision(Realm, disposableProtoDefs);

        Span<PropertyDefinition> asyncDisposableProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(AsyncDisposableStackConstructor)),
            PropertyDefinition.GetterData(atomDisposed, asyncDisposedGetFn, configurable: true),
            PropertyDefinition.Mutable(atomUse, JsValue.FromObject(asyncUseFn)),
            PropertyDefinition.Mutable(atomAdopt, JsValue.FromObject(asyncAdoptFn)),
            PropertyDefinition.Mutable(atomDefer, JsValue.FromObject(asyncDeferFn)),
            PropertyDefinition.Mutable(atomDisposeAsync, JsValue.FromObject(asyncDisposeFn)),
            PropertyDefinition.Mutable(atomMove, JsValue.FromObject(asyncMoveFn)),
            PropertyDefinition.Mutable(IdSymbolAsyncDispose, JsValue.FromObject(asyncDisposeFn)),
            PropertyDefinition.Mutable(IdSymbolToStringTag, JsValue.FromString("AsyncDisposableStack"))
        ];
        AsyncDisposableStackPrototype.DefineNewPropertiesNoCollision(Realm, asyncDisposableProtoDefs);
    }

    private static JsDisposableStackObject RequireDisposableStack(in JsValue value, bool isAsync, string operation)
    {
        if (!value.TryGetObject(out var obj) || obj is not JsDisposableStackObject stack || stack.IsAsync != isAsync)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"{operation} called on incompatible receiver");
        }

        return stack;
    }

    private static JsFunction RequireCallableArgument(in CallInfo info, int index, string operation)
    {
        var value = info.GetArgumentOrDefault(index, JsValue.Undefined);
        if (!value.TryGetObject(out var obj) || obj is not JsFunction fn)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{operation} requires a callable argument");
        return fn;
    }

    private static JsValue GetDisposeMethod(JsRealm realm, in JsValue value, bool allowAsyncFallback)
    {
        if (!value.TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Disposable resource must be an object");

        if (allowAsyncFallback)
        {
            var asyncMethod = GetDisposeMethodCore(realm, obj, IdSymbolAsyncDispose);
            if (!asyncMethod.IsUndefined)
                return asyncMethod;
        }

        return GetDisposeMethodCore(realm, obj, IdSymbolDispose);
    }

    private static JsValue GetDisposeMethodCore(JsRealm realm, JsObject obj, int atom)
    {
        if (!obj.TryGetPropertyAtom(realm, atom, out var method, out _) ||
            method.IsUndefined ||
            method.IsNull)
            return JsValue.Undefined;

        if (!method.TryGetObject(out var methodObj) || methodObj is not JsFunction)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                atom == IdSymbolAsyncDispose
                    ? "Symbol.asyncDispose is not callable"
                    : "Symbol.dispose is not callable");
        }

        return method;
    }

    private void DisposeResources(List<DisposableResource> resources)
    {
        JsValue? completion = null;
        for (var i = resources.Count - 1; i >= 0; i--)
        {
            try
            {
                DisposeResource(resources[i]);
            }
            catch (Exception ex)
            {
                var error = GetDisposeErrorValue(ex);
                completion = completion is JsValue suppressed
                    ? CreateSuppressedErrorValue(error, suppressed)
                    : error;
            }
        }

        if (completion is JsValue thrown)
            throw new JsRuntimeException(JsErrorKind.InternalError, string.Empty, null, thrown);
    }

    private async ValueTask DisposeResourcesAsync(List<DisposableResource> resources)
    {
        JsValue? completion = null;
        for (var i = resources.Count - 1; i >= 0; i--)
        {
            try
            {
                await DisposeResourceAsync(resources[i]);
            }
            catch (Exception ex)
            {
                var error = GetDisposeErrorValue(ex);
                completion = completion is JsValue suppressed
                    ? CreateSuppressedErrorValue(error, suppressed)
                    : error;
            }
        }

        if (completion is JsValue thrown)
            throw new JsRuntimeException(JsErrorKind.InternalError, string.Empty, null, thrown);
    }

    private void DisposeResource(in DisposableResource resource)
    {
        if (resource.DisposeMethod.IsUndefined)
            return;

        if (!resource.DisposeMethod.TryGetObject(out var methodObj) || methodObj is not JsFunction method)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Dispose method is not callable");

        _ = Realm.InvokeFunction(method, resource.Value, ReadOnlySpan<JsValue>.Empty);
    }

    private async ValueTask DisposeResourceAsync(DisposableResource resource)
    {
        if (resource.DisposeMethod.IsUndefined)
            return;

        if (!resource.DisposeMethod.TryGetObject(out var methodObj) || methodObj is not JsFunction method)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Dispose method is not callable");

        var result = Realm.InvokeFunction(method, resource.Value, ReadOnlySpan<JsValue>.Empty);
        if (resource.Hint == DisposableResourceHint.AsyncDispose)
            await Realm.ToPumpedValueTask(result);
    }

    private JsValue GetDisposeErrorValue(Exception ex)
    {
        return ex is JsRuntimeException runtimeEx
            ? runtimeEx.ThrownValue ?? Realm.CreateErrorObjectFromException(runtimeEx)
            : Realm.CreateErrorObjectFromException(new JsRuntimeException(JsErrorKind.InternalError, ex.Message, null, null,
                ex));
    }

    private JsValue CreateSuppressedErrorValue(in JsValue error, in JsValue suppressed)
    {
        var args = new InlineJsValueArray3
        {
            Item0 = error,
            Item1 = suppressed,
            Item2 = JsValue.Undefined
        };
        return Realm.ConstructWithExplicitNewTarget(SuppressedErrorConstructor, args.AsSpan(),
            JsValue.FromObject(SuppressedErrorConstructor), 0);
    }
}
