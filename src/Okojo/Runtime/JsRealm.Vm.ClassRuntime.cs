using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetClassHeritage(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("CLASS_HERITAGE_ARGC", "SetClassHeritage requires two arguments");

        var ctorValue = Unsafe.Add(ref registers, argRegStart);
        var superValue = Unsafe.Add(ref registers, argRegStart + 1);

        if (!ctorValue.TryGetObject(out var ctorObj) || ctorObj is not JsBytecodeFunction ctorFn)
            throw TypeError("CLASS_HERITAGE_CTOR", "Class constructor must be a function object");

        JsObject? superPrototypeObject = null;
        if (superValue.IsNull)
        {
            ctorFn.Prototype = realm.FunctionPrototype;
        }
        else if (superValue.TryGetObject(out var superObj) && superObj is JsFunction superFn)
        {
            if (!superFn.IsConstructor)
                ThrowTypeError("CLASS_HERITAGE_SUPER",
                    "Class extends value is not a constructor or null");

            ctorFn.Prototype = superFn;
            if (!superFn.TryGetPropertyAtom(realm, IdPrototype, out var superProtoValue, out _))
                superProtoValue = JsValue.Undefined;

            if (superProtoValue.IsNull)
                superPrototypeObject = null;
            else if (superProtoValue.TryGetObject(out var superProtoObj))
                superPrototypeObject = superProtoObj;
            else
                ThrowTypeError("CLASS_HERITAGE_PROTO",
                    "Class extends value does not have a valid prototype property");
        }
        else
        {
            ThrowTypeError("CLASS_HERITAGE_SUPER",
                "Class extends value is not a constructor or null");
        }

        JsObject? ctorProtoObjTemp = null;
        if (!ctorFn.TryGetPropertyAtom(realm, IdPrototype, out var ctorProtoValue, out _) ||
            !ctorProtoValue.TryGetObject(out ctorProtoObjTemp))
            ThrowTypeError("CLASS_HERITAGE_CTOR_PROTO", "Class constructor prototype is not an object");

        var ctorProtoObj = ctorProtoObjTemp!;
        ctorProtoObj.Prototype = superPrototypeObject;
        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCallSuperConstructor(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        var args = argCount == 0
            ? ReadOnlySpan<JsValue>.Empty
            : MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart), argCount);
        acc = CallSuperConstructorCore(realm, fp, opcodePc, args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCallSuperConstructorForwardAll(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("SUPER_CALL_FORWARD_ARGC", "CallSuperConstructorForwardAll expects zero explicit arguments");
        var args = realm.GetFrameArgumentsSpan(fp);
        acc = CallSuperConstructorCore(realm, fp, opcodePc, args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCallSuperConstructorWithSpread(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 1)
            ThrowTypeError("SUPER_CALL_SPREAD_ARGC",
                "CallSuperConstructorWithSpread requires flags and optional arguments");

        var flagsValue = Unsafe.Add(ref registers, argRegStart);
        var savedSp = realm.StackTop;
        int spreadArgOffset;
        int spreadArgCount;
        try
        {
            spreadArgOffset = realm.CopySpreadArgumentsToStackTop(flagsValue, argCount == 1
                    ? ReadOnlySpan<JsValue>.Empty
                    : MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 1), argCount - 1),
                out spreadArgCount);
            acc = CallSuperConstructorCore(realm, fp, opcodePc, realm.Stack.AsSpan(spreadArgOffset, spreadArgCount));
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue CallSuperConstructorCore(JsRealm realm, int fp, int opcodePc,
        ReadOnlySpan<JsValue> args)
    {
        ref readonly var callFrame = ref Unsafe.As<JsValue, CallFrame>(ref realm.Stack[fp]);
        JsBytecodeFunction currentFunction;
        JsValue newTarget;
        bool thisAlreadyInitialized;
        int thisFramePointer;
        JsBytecodeFunction.DerivedSuperCallState? derivedSuperState = null;

        if ((callFrame.Flags & (CallFrameFlag.IsConstructorCall | CallFrameFlag.IsDerivedConstructorCall)) ==
            (CallFrameFlag.IsConstructorCall | CallFrameFlag.IsDerivedConstructorCall) &&
            callFrame.Function is JsBytecodeFunction
            {
                IsClassConstructor: true, IsDerivedConstructor: true
            } directFunction)
        {
            currentFunction = directFunction;
            newTarget = realm.Stack[fp + OffsetExtra0];
            thisAlreadyInitialized = !callFrame.ThisValue.IsTheHole;
            thisFramePointer = fp;
        }
        else if (callFrame.Function is JsBytecodeFunction
                 {
                     IsArrow: true, BoundDerivedSuperCallState: not null
                 } arrowFunction &&
                 arrowFunction.BoundDerivedSuperCallState is { } boundDerivedSuperState)
        {
            derivedSuperState = boundDerivedSuperState;
            currentFunction = boundDerivedSuperState.ConstructorFunction;
            newTarget = boundDerivedSuperState.NewTarget;
            if (TryGetLiveDerivedSuperCallFrameState(realm, boundDerivedSuperState, out var liveFramePointer,
                    out var liveThisValue))
            {
                thisFramePointer = liveFramePointer;
                thisAlreadyInitialized = !liveThisValue.IsTheHole;
            }
            else
            {
                thisFramePointer = -1;
                thisAlreadyInitialized = boundDerivedSuperState.DerivedThisContext is not null &&
                                         boundDerivedSuperState.DerivedThisSlot >= 0 &&
                                         (uint)boundDerivedSuperState.DerivedThisSlot <
                                         (uint)boundDerivedSuperState.DerivedThisContext.Slots.Length &&
                                         !boundDerivedSuperState.DerivedThisContext.Slots[
                                             boundDerivedSuperState.DerivedThisSlot].IsTheHole;
            }
        }
        else
        {
            ThrowTypeError("SUPER_CALL_INVALID", "super() is only valid in derived constructors");
            return JsValue.Undefined;
        }

        if (currentFunction.Prototype is not JsFunction)
            ThrowTypeError("SUPER_CALL_TARGET", "Class extends value is not a constructor");

        var superCtor = (JsFunction)currentFunction.Prototype;
        var thisValue = realm.ConstructWithExplicitNewTarget(superCtor, args, newTarget, opcodePc);
        if (thisAlreadyInitialized)
            ThrowReferenceError("SUPER_CALL_TWICE", "Super constructor may only be called once");
        if (thisFramePointer >= 0)
        {
            realm.Stack[thisFramePointer + OffsetThisValue] = thisValue;
            realm.UpdateDerivedThisContextValue(thisFramePointer, currentFunction, thisValue);
        }
        else if (derivedSuperState?.DerivedThisContext is not null &&
                 derivedSuperState.DerivedThisSlot >= 0 &&
                 (uint)derivedSuperState.DerivedThisSlot < (uint)derivedSuperState.DerivedThisContext.Slots.Length)
        {
            derivedSuperState.DerivedThisContext.Slots[derivedSuperState.DerivedThisSlot] = thisValue;
        }

        return thisValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetLiveDerivedSuperCallFrameState(
        JsRealm realm,
        JsBytecodeFunction.DerivedSuperCallState state,
        out int liveFramePointer,
        out JsValue thisValue)
    {
        var framePointer = state.FramePointer;
        if ((uint)framePointer < (uint)realm.StackTop &&
            ReferenceEquals(realm.GetCallFrameAt(framePointer).Function, state.ConstructorFunction))
        {
            liveFramePointer = framePointer;
            thisValue = realm.Stack[framePointer + OffsetThisValue];
            return true;
        }

        liveFramePointer = -1;
        thisValue = JsValue.Undefined;
        return false;
    }
}
