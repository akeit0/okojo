using System.Runtime.CompilerServices;
using Okojo.Bytecode;
using Okojo.Internals;
using Okojo.Objects;
using Okojo.Runtime.Interop;

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
                var method = GetDisposeMethod(realm, value, allowAsyncFallback: false, out _);
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
            stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.SyncDispose,
                JsValue.FromObject(CreateAdoptDisposeWrapper(info.Realm, onDispose, value))));
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

            var method = GetDisposeMethod(realm, value, allowAsyncFallback: true, out var hint);
            if (method.IsUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "AsyncDisposableStack.prototype.use value does not define Symbol.asyncDispose or Symbol.dispose");
            stack.AddResource(new(value, hint, method));
            return value;
        }, "use", 1);

        var asyncAdoptFn = new JsHostFunction(Realm, static (in info) =>
        {
            var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.adopt");
            stack.ThrowIfDisposed();
            var value = info.GetArgumentOrDefault(0, JsValue.Undefined);
            var onDispose = RequireCallableArgument(info, 1, "AsyncDisposableStack.prototype.adopt");
            stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.AsyncDispose,
                JsValue.FromObject(CreateAdoptDisposeWrapper(info.Realm, onDispose, value))));
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
            try
            {
                var stack = RequireDisposableStack(info.ThisValue, true, "AsyncDisposableStack.prototype.disposeAsync");
                if (stack.IsDisposed)
                    return realm.PromiseResolveValue(JsValue.Undefined);

                stack.State = DisposableStackState.Disposed;
                var resources = stack.DetachResources();
                if (RequiresSingleAsyncDisposeTick(resources))
                    return realm.Intrinsics.CreateSingleAsyncDisposeTickPromise();
                return realm.WrapPromiseValueTask(realm.Intrinsics.DisposeResourcesAsync(resources));
            }
            catch (JsRuntimeException ex)
            {
                return realm.Intrinsics.CreateRejectedPromise(ex);
            }
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
            PropertyDefinition.GetterData(IdDisposed, disposableDisposedGetFn, configurable: true),
            PropertyDefinition.Mutable(IdUse, JsValue.FromObject(disposableUseFn)),
            PropertyDefinition.Mutable(IdAdopt, JsValue.FromObject(disposableAdoptFn)),
            PropertyDefinition.Mutable(IdDefer, JsValue.FromObject(disposableDeferFn)),
            PropertyDefinition.Mutable(IdDispose, JsValue.FromObject(disposableDisposeFn)),
            PropertyDefinition.Mutable(IdMove, JsValue.FromObject(disposableMoveFn)),
            PropertyDefinition.Mutable(IdSymbolDispose, JsValue.FromObject(disposableDisposeFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("DisposableStack"), configurable: true)
        ];
        DisposableStackPrototype.DefineNewPropertiesNoCollision(Realm, disposableProtoDefs);

        Span<PropertyDefinition> asyncDisposableProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(AsyncDisposableStackConstructor)),
            PropertyDefinition.GetterData(IdDisposed, asyncDisposedGetFn, configurable: true),
            PropertyDefinition.Mutable(IdUse, JsValue.FromObject(asyncUseFn)),
            PropertyDefinition.Mutable(IdAdopt, JsValue.FromObject(asyncAdoptFn)),
            PropertyDefinition.Mutable(IdDefer, JsValue.FromObject(asyncDeferFn)),
            PropertyDefinition.Mutable(IdDisposeAsync, JsValue.FromObject(asyncDisposeFn)),
            PropertyDefinition.Mutable(IdMove, JsValue.FromObject(asyncMoveFn)),
            PropertyDefinition.Mutable(IdSymbolAsyncDispose, JsValue.FromObject(asyncDisposeFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("AsyncDisposableStack"), configurable: true)
        ];
        AsyncDisposableStackPrototype.DefineNewPropertiesNoCollision(Realm, asyncDisposableProtoDefs);
    }

    internal JsDisposableStackObject CreateCompilerDisposableStack(bool isAsync)
    {
        return new JsDisposableStackObject(Realm, isAsync,
            isAsync ? Realm.AsyncDisposableStackPrototype : Realm.DisposableStackPrototype);
    }

    internal void AddCompilerDisposableResource(JsDisposableStackObject stack, in JsValue value,
        DisposableResourceHint hint)
    {
        stack.ThrowIfDisposed();
        if (value.IsNull || value.IsUndefined)
        {
            if (stack.IsAsync && hint == DisposableResourceHint.AsyncDispose)
                stack.AddResource(new(JsValue.Undefined, DisposableResourceHint.AsyncDispose, JsValue.Undefined));
            return;
        }

        var method = GetDisposeMethod(Realm, value, hint == DisposableResourceHint.AsyncDispose, out var resolvedHint);
        if (method.IsUndefined)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                hint == DisposableResourceHint.AsyncDispose
                    ? "await using value does not define Symbol.asyncDispose or Symbol.dispose"
                    : "using value does not define Symbol.dispose");
        }

        stack.AddResource(new(value, resolvedHint, method));
    }

    internal JsValue DisposeCompilerDisposableStack(JsDisposableStackObject stack, int completionKind,
        in JsValue completionValue)
    {
        if (stack.IsDisposed)
            return JsValue.Undefined;

        stack.State = DisposableStackState.Disposed;
        var priorThrown = completionKind == 2 ? completionValue : (JsValue?)null;
        var resources = stack.DetachResources();
        if (stack.IsAsync)
        {
            if (resources.Count == 0 && priorThrown is null)
                return JsValue.Undefined;
            return Realm.WrapPromiseValueTask(DisposeResourcesAsync(resources, priorThrown));
        }

        DisposeResources(resources, priorThrown);
        return JsValue.Undefined;
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

    private static JsValue GetDisposeMethod(JsRealm realm, in JsValue value, bool allowAsyncFallback,
        out DisposableResourceHint hint)
    {
        hint = DisposableResourceHint.SyncDispose;
        if (!value.TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Disposable resource must be an object");

        if (allowAsyncFallback)
        {
            var asyncMethod = GetDisposeMethodCore(realm, obj, IdSymbolAsyncDispose);
            if (!asyncMethod.IsUndefined)
            {
                hint = DisposableResourceHint.AsyncDispose;
                return asyncMethod;
            }
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

    private void DisposeResources(List<DisposableResource> resources, JsValue? priorThrown = null)
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
                    : priorThrown is JsValue prior
                        ? CreateSuppressedErrorValue(error, prior)
                        : error;
            }
        }

        if (completion is JsValue thrown)
            throw new JsRuntimeException(JsErrorKind.InternalError, string.Empty, null, thrown);
    }

    private async ValueTask DisposeResourcesAsync(List<DisposableResource> resources, JsValue? priorThrown = null)
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
                    : priorThrown is JsValue prior
                        ? CreateSuppressedErrorValue(error, prior)
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

    internal JsValue GetDisposeErrorValue(Exception ex)
    {
        if (ex is PromiseRejectedException promiseRejected)
            return promiseRejected.Reason;
        return ex is JsRuntimeException runtimeEx
            ? runtimeEx.ThrownValue ?? Realm.CreateErrorObjectFromException(runtimeEx)
            : Realm.CreateErrorObjectFromException(new JsRuntimeException(JsErrorKind.InternalError, ex.Message, null, null,
                ex));
    }

    private JsValue CreateSuppressedErrorValue(in JsValue error, in JsValue suppressed)
    {
        return CreateSuppressedErrorInstance(SuppressedErrorPrototype, error, suppressed);
    }

    private static JsHostFunction CreateAdoptDisposeWrapper(JsRealm realm, JsFunction onDispose, JsValue value)
    {
        return new(realm, (in _) =>
        {
            return realm.InvokeFunction(onDispose, JsValue.Undefined, [value]);
        }, string.Empty, 0);
    }

    private JsValue CreateRejectedPromise(JsRuntimeException ex)
    {
        var promise = Realm.CreatePromiseObject();
        Realm.RejectPromise(promise, GetPromiseAbruptReason(ex));
        return JsValue.FromObject(promise);
    }

    private static bool RequiresSingleAsyncDisposeTick(List<DisposableResource> resources)
    {
        if (resources.Count == 0)
            return false;

        for (var i = 0; i < resources.Count; i++)
        {
            if (resources[i].Hint != DisposableResourceHint.AsyncDispose || !resources[i].DisposeMethod.IsUndefined)
                return false;
        }

        return true;
    }

    private JsValue CreateSingleAsyncDisposeTickPromise()
    {
        var promise = Realm.CreatePromiseObject();
        Realm.Agent.EnqueueMicrotask(() => Realm.ResolvePromise(promise, JsValue.Undefined));
        return JsValue.FromObject(promise);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeCreateDisposableResourceStack(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            throw new JsRuntimeException(JsErrorKind.TypeError, "CreateDisposableResourceStack requires no arguments");
        acc = JsValue.FromObject(realm.Intrinsics.CreateCompilerDisposableStack(false));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeCreateAsyncDisposableResourceStack(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "CreateAsyncDisposableResourceStack requires no arguments");
        }

        acc = JsValue.FromObject(realm.Intrinsics.CreateCompilerDisposableStack(true));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeAddDisposableResource(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeAddCompilerDisposableResource(realm, ref registers, argRegStart, argCount,
            DisposableResourceHint.SyncDispose, ref acc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeAddAsyncDisposableResource(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeAddCompilerDisposableResource(realm, ref registers, argRegStart, argCount,
            DisposableResourceHint.AsyncDispose, ref acc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeAddCurrentModuleDisposableResource(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeAddCurrentModuleCompilerDisposableResource(realm, ref registers, argRegStart, argCount,
            DisposableResourceHint.SyncDispose, ref acc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeAddCurrentModuleAsyncDisposableResource(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeAddCurrentModuleCompilerDisposableResource(realm, ref registers, argRegStart, argCount,
            DisposableResourceHint.AsyncDispose, ref acc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeDisposeDisposableResourceStack(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeDisposeCompilerDisposableResourceStack(realm, ref registers, argRegStart, argCount, false,
            ref acc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void HandleRuntimeDisposeAsyncDisposableResourceStack(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleRuntimeDisposeCompilerDisposableResourceStack(realm, ref registers, argRegStart, argCount, true,
            ref acc);
    }

    private static void HandleRuntimeAddCompilerDisposableResource(
        JsRealm realm,
        ref JsValue registers,
        int argRegStart,
        int argCount,
        DisposableResourceHint hint,
        ref JsValue acc)
    {
        if (argCount != 2)
            throw new JsRuntimeException(JsErrorKind.TypeError, "AddDisposableResource requires two arguments");

        var stackValue = Unsafe.Add(ref registers, argRegStart);
        if (!stackValue.TryGetObject(out var stackObject) || stackObject is not JsDisposableStackObject stack)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Explicit resource stack must be an object");

        realm.Intrinsics.AddCompilerDisposableResource(stack, Unsafe.Add(ref registers, argRegStart + 1), hint);
        acc = JsValue.Undefined;
    }

    private static void HandleRuntimeDisposeCompilerDisposableResourceStack(
        JsRealm realm,
        ref JsValue registers,
        int argRegStart,
        int argCount,
        bool requireAsync,
        ref JsValue acc)
    {
        if (argCount != 3)
            throw new JsRuntimeException(JsErrorKind.TypeError, "DisposeDisposableResourceStack requires three arguments");

        var stackValue = Unsafe.Add(ref registers, argRegStart);
        if (!stackValue.TryGetObject(out var stackObject) || stackObject is not JsDisposableStackObject stack)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Explicit resource stack must be an object");
        if (stack.IsAsync != requireAsync)
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                requireAsync
                    ? "Expected async explicit resource stack"
                    : "Expected sync explicit resource stack");
        }

        var completionKind = JsRealm.ToInt32SlowPath(realm, Unsafe.Add(ref registers, argRegStart + 1));
        acc = realm.Intrinsics.DisposeCompilerDisposableStack(
            stack,
            completionKind,
            Unsafe.Add(ref registers, argRegStart + 2));
    }

    private static void HandleRuntimeAddCurrentModuleCompilerDisposableResource(
        JsRealm realm,
        ref JsValue registers,
        int argRegStart,
        int argCount,
        DisposableResourceHint hint,
        ref JsValue acc)
    {
        if (argCount != 1)
            throw new JsRuntimeException(JsErrorKind.TypeError, "AddCurrentModuleDisposableResource requires one argument");

        var stack = realm.Agent.GetCurrentModuleExplicitResourceStack();
        if (hint == DisposableResourceHint.AsyncDispose && !stack.IsAsync)
        {
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "Current module explicit resource stack is not async");
        }

        realm.Intrinsics.AddCompilerDisposableResource(stack, Unsafe.Add(ref registers, argRegStart), hint);
        acc = JsValue.Undefined;
    }
}
