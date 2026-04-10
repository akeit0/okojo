namespace Okojo.Objects;

internal sealed class JsBoundFunction : JsFunction
{
    private readonly JsValue[] boundArguments;

    internal JsBoundFunction(JsRealm realm, JsFunction target, JsValue boundThis, JsValue[] boundArguments,
        string name, int length)
        : base(realm, name, true, length,
            isConstructor: target.IsConstructor)
    {
        Target = target;
        BoundThis = boundThis;
        this.boundArguments = boundArguments;
    }

    internal JsFunction Target { get; }

    internal JsValue BoundThis { get; }

    internal ReadOnlySpan<JsValue> BoundArguments => boundArguments;

    internal JsValue InvokeBound(JsRealm realm, ReadOnlySpan<JsValue> args, int callerPc)
    {
        if (!ReferenceEquals(Target.Realm, realm))
        {
            var mergedArgs = new JsValue[boundArguments.Length + args.Length];
            boundArguments.CopyTo(mergedArgs);
            args.CopyTo(mergedArgs.AsSpan(boundArguments.Length));
            return Target.Realm.InvokeFunction(Target, BoundThis, mergedArgs);
        }

        if (Target is JsBytecodeFunction bytecodeTarget)
            return realm.InvokeBytecodeFunctionWithPrependedArguments(bytecodeTarget, BoundThis, boundArguments, args,
                JsValue.Undefined, callerPc);

        var savedSp = realm.StackTop;
        var argOffset = realm.CopyPrependedArgumentsToStackTop(boundArguments, args);
        try
        {
            return realm.DispatchCallFromStack(Target, BoundThis, argOffset, boundArguments.Length + args.Length,
                callerPc);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    internal JsValue InvokeBoundFromStack(JsRealm realm, int argOffset, int argCount, int callerPc)
    {
        if (!ReferenceEquals(Target.Realm, realm))
        {
            var mergedArgs = new JsValue[boundArguments.Length + argCount];
            boundArguments.CopyTo(mergedArgs);
            realm.StackAsSpan(argOffset, argCount).CopyTo(mergedArgs.AsSpan(boundArguments.Length));
            return Target.Realm.InvokeFunction(Target, BoundThis, mergedArgs);
        }

        if (Target is JsBytecodeFunction bytecodeTarget)
            return realm.InvokeBytecodeFunctionWithPrependedArguments(bytecodeTarget, BoundThis, boundArguments,
                argOffset, argCount, JsValue.Undefined, callerPc);

        var savedSp = realm.StackTop;
        var mergedOffset = realm.CopyPrependedArgumentsToStackTop(boundArguments, argOffset, argCount);
        try
        {
            return realm.DispatchCallFromStack(Target, BoundThis, mergedOffset, boundArguments.Length + argCount,
                callerPc);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    internal JsValue ConstructBound(JsRealm realm, ReadOnlySpan<JsValue> args, JsValue newTarget, int callerPc)
    {
        if (newTarget.TryGetObject(out var newTargetObject) && ReferenceEquals(newTargetObject, this))
            newTarget = Target;

        if (!ReferenceEquals(Target.Realm, realm))
        {
            var mergedArgs = new JsValue[boundArguments.Length + args.Length];
            boundArguments.CopyTo(mergedArgs);
            args.CopyTo(mergedArgs.AsSpan(boundArguments.Length));
            return Target.Realm.ConstructWithExplicitNewTarget(Target, mergedArgs, newTarget, callerPc);
        }

        var prepared = realm.PrepareConstructInvocation(Target, newTarget);
        if (Target is JsBytecodeFunction bytecodeTarget)
        {
            var result = realm.InvokeBytecodeFunctionWithPrependedArguments(bytecodeTarget, prepared.ThisValue,
                boundArguments, args, prepared.NewTarget, callerPc, CallFrameKind.ConstructFrame, prepared.Flags);
            return JsRealm.CompleteConstructResult(result, prepared.ThisValue, prepared.Flags);
        }

        var savedSp = realm.StackTop;
        var argOffset = realm.CopyPrependedArgumentsToStackTop(boundArguments, args);
        try
        {
            return realm.DispatchConstructFromStack(Target, prepared, argOffset, boundArguments.Length + args.Length,
                callerPc);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    internal JsValue ConstructBoundFromStack(JsRealm realm, int argOffset, int argCount, JsValue newTarget,
        int callerPc)
    {
        if (newTarget.TryGetObject(out var newTargetObject) && ReferenceEquals(newTargetObject, this))
            newTarget = Target;

        if (!ReferenceEquals(Target.Realm, realm))
        {
            var mergedArgs = new JsValue[boundArguments.Length + argCount];
            boundArguments.CopyTo(mergedArgs);
            realm.StackAsSpan(argOffset, argCount).CopyTo(mergedArgs.AsSpan(boundArguments.Length));
            return Target.Realm.ConstructWithExplicitNewTarget(Target, mergedArgs, newTarget, callerPc);
        }

        var prepared = realm.PrepareConstructInvocation(Target, newTarget);
        if (Target is JsBytecodeFunction bytecodeTarget)
        {
            var result = realm.InvokeBytecodeFunctionWithPrependedArguments(bytecodeTarget, prepared.ThisValue,
                boundArguments, argOffset, argCount, prepared.NewTarget, callerPc, CallFrameKind.ConstructFrame,
                prepared.Flags);
            return JsRealm.CompleteConstructResult(result, prepared.ThisValue, prepared.Flags);
        }

        var savedSp = realm.StackTop;
        var mergedOffset = realm.CopyPrependedArgumentsToStackTop(boundArguments, argOffset, argCount);
        try
        {
            return realm.DispatchConstructFromStack(Target, prepared, mergedOffset, boundArguments.Length + argCount,
                callerPc);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    internal override JsValue InvokeNonBytecodeCall(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        int callerPc)
    {
        return InvokeBound(realm, args, callerPc);
    }

    internal override JsValue InvokeNonBytecodeConstruct(JsRealm realm, JsValue thisValue, ReadOnlySpan<JsValue> args,
        JsValue newTarget, int callerPc, CallFrameFlag flags)
    {
        return ConstructBound(realm, args, newTarget, callerPc);
    }
}
