using System.Numerics;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsPlainObject CreateAtomicsObject()
    {
        var atomics = new JsPlainObject(Realm);

        atomics.DefineNewPropertiesNoCollision(Realm,
        [
            PropertyDefinition.Mutable(IdAdd, JsValue.FromObject(CreateAtomicsReadModifyWriteFunction("add",
                static (kind, left, right) => AtomicsAdd(kind, left, right),
                static (_, left, right) => left + right))),
            PropertyDefinition.Mutable(IdAnd, JsValue.FromObject(CreateAtomicsReadModifyWriteFunction("and",
                static (_, left, right) => left & right,
                static (_, left, right) => left & right))),
            PropertyDefinition.Mutable(IdCompareExchange, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) => AtomicsCompareExchange(info), "compareExchange", 4))),
            PropertyDefinition.Mutable(IdExchange, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) => AtomicsExchange(info), "exchange", 3))),
            PropertyDefinition.Mutable(IdIsLockFree, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) =>
                {
                    var args = info.Arguments;
                    var size = args.Length == 0 ? 0 : (int)info.Realm.ToIntegerOrInfinity(args[0]);
                    return size is 1 or 2 or 4 or 8 ? JsValue.True : JsValue.False;
                }, "isLockFree", 1))),
            PropertyDefinition.Mutable(IdLoad, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) =>
                {
                    var view = ValidateIntegerTypedArray(info.Realm, info.Arguments, out var index, false);
                    return view.GetDirectElementValue((uint)index);
                }, "load", 2))),
            PropertyDefinition.Mutable(IdNotify, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) => AtomicsNotify(info), "notify", 3))),
            PropertyDefinition.Mutable(IdOr, JsValue.FromObject(CreateAtomicsReadModifyWriteFunction("or",
                static (_, left, right) => left | right,
                static (_, left, right) => left | right))),
            PropertyDefinition.Mutable(IdPause, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) =>
                {
                    var args = info.Arguments;
                    if (args.Length == 0 || args[0].IsUndefined)
                        return JsValue.Undefined;

                    var value = args[0];
                    var iterationNumber = value.IsInt32
                        ? value.Int32Value
                        : value.IsFloat64
                            ? value.Float64Value
                            : double.NaN;
                    if (double.IsNaN(iterationNumber) ||
                        double.IsInfinity(iterationNumber) ||
                        Math.Floor(iterationNumber) != iterationNumber)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Atomics.pause iterationNumber must be an integral Number");

                    return JsValue.Undefined;
                }, "pause", 0))),
            PropertyDefinition.Mutable(IdStore, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) => AtomicsStore(info), "store", 3))),
            PropertyDefinition.Mutable(IdSub, JsValue.FromObject(CreateAtomicsReadModifyWriteFunction("sub",
                static (kind, left, right) => AtomicsSub(kind, left, right),
                static (_, left, right) => left - right))),
            PropertyDefinition.Mutable(IdWait, JsValue.FromObject(new JsHostFunction(Realm,
                static (in info) => AtomicsWait(info), "wait", 4))),
            PropertyDefinition.Mutable(IdWaitAsync, JsValue.FromObject(new JsHostFunction(Realm,
                (in info) => AtomicsWaitAsync(info), "waitAsync", 4))),
            PropertyDefinition.Mutable(IdXor, JsValue.FromObject(CreateAtomicsReadModifyWriteFunction("xor",
                static (_, left, right) => left ^ right,
                static (_, left, right) => left ^ right))),
            PropertyDefinition.Const(IdSymbolToStringTag, "Atomics", configurable: true)
        ]);

        return atomics;
    }

    private JsHostFunction CreateAtomicsReadModifyWriteFunction(string name,
        Func<TypedArrayElementKind, long, long, long> numericOp,
        Func<TypedArrayElementKind, BigInteger, BigInteger, BigInteger> bigIntOp)
    {
        return new(Realm, (in info) =>
        {
            var view = ValidateIntegerTypedArray(info.Realm, info.Arguments, out var index, false);
            var args = info.Arguments;
            var valueArg = args.Length > 2 ? args[2] : JsValue.Undefined;
            return DoAtomicsReadModifyWrite(info.Realm, view, (uint)index, valueArg, numericOp, bigIntOp);
        }, name, 3);
    }

    private static long AtomicsAdd(TypedArrayElementKind kind, long left, long right)
    {
        return kind is TypedArrayElementKind.BigUint64 or TypedArrayElementKind.Uint32 or TypedArrayElementKind.Uint16
            or TypedArrayElementKind.Uint8
            ? unchecked(left + right)
            : left + right;
    }

    private static long AtomicsSub(TypedArrayElementKind kind, long left, long right)
    {
        return kind is TypedArrayElementKind.BigUint64 or TypedArrayElementKind.Uint32 or TypedArrayElementKind.Uint16
            or TypedArrayElementKind.Uint8
            ? unchecked(left - right)
            : left - right;
    }

    private static JsValue AtomicsCompareExchange(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = ValidateIntegerTypedArray(realm, args, out var index, false);
        var expectedArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        var replacementArg = args.Length > 3 ? args[3] : JsValue.Undefined;
        var buffer = view.Buffer;
        var byteIndex = GetTypedArrayByteIndex(view, (uint)index);
        var kind = view.Kind;
        var expected = TypedArrayElementKindInfo.NormalizeValue(realm, kind, expectedArg);
        var replacement = TypedArrayElementKindInfo.NormalizeValue(realm, kind, replacementArg);

        lock (GetAtomicsSyncRoot(buffer))
        {
            var current = buffer.ReadTypedArrayElement(realm, kind, byteIndex);
            if (AtomicsSame(kind, current, expected))
                buffer.WriteNormalizedTypedArrayElement(kind, byteIndex, replacement);
            return current;
        }
    }

    private static JsValue AtomicsExchange(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = ValidateIntegerTypedArray(realm, args, out var index, false);
        var replacementArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        var buffer = view.Buffer;
        var byteIndex = GetTypedArrayByteIndex(view, (uint)index);
        var kind = view.Kind;
        var replacement = TypedArrayElementKindInfo.NormalizeValue(realm, kind, replacementArg);

        lock (GetAtomicsSyncRoot(buffer))
        {
            var current = buffer.ReadTypedArrayElement(realm, kind, byteIndex);
            buffer.WriteNormalizedTypedArrayElement(kind, byteIndex, replacement);
            return current;
        }
    }

    private static JsValue AtomicsStore(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = ValidateIntegerTypedArray(realm, args, out var index, false);
        var valueArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        var kind = view.Kind;
        var normalized = TypedArrayElementKindInfo.NormalizeValue(realm, kind, valueArg);
        var buffer = view.Buffer;

        lock (GetAtomicsSyncRoot(buffer))
        {
            buffer.WriteNormalizedTypedArrayElement(kind, GetTypedArrayByteIndex(view, (uint)index), normalized);
        }

        return kind.IsBigIntFamily()
            ? JsValue.FromBigInt(ToBigIntValue(realm, valueArg))
            : new(realm.ToIntegerOrInfinity(valueArg));
    }

    private static JsValue AtomicsNotify(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = RequireAtomicsTypedArray(args, true);
        if (view.Buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Atomics.notify requires a non-detached typed array");
        var index = ValidateAtomicsAccess(realm, view, args.Length > 1 ? args[1] : JsValue.Undefined);
        var count = args.Length > 2 && !args[2].IsUndefined
            ? (int)Math.Max(0, realm.ToIntegerOrInfinity(args[2]))
            : int.MaxValue;
        if (!view.Buffer.IsShared)
            return JsValue.FromInt32(0);
        var woken = view.Buffer.NotifySharedWaiters(GetTypedArrayByteIndex(view, (uint)index), count);
        return JsValue.FromInt32(woken);
    }

    private static JsValue AtomicsWait(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = RequireAtomicsTypedArray(args, true);
        if (view.Buffer.IsDetached || !view.Buffer.IsShared)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Atomics.wait requires a SharedArrayBuffer-backed typed array");
        var index = ValidateAtomicsAccess(realm, view, args.Length > 1 ? args[1] : JsValue.Undefined);
        var expected =
            TypedArrayElementKindInfo.NormalizeValue(realm, view.Kind, args.Length > 2 ? args[2] : JsValue.Undefined);
        var timeout = NormalizeWaitTimeout(realm, args.Length > 3 ? args[3] : JsValue.Undefined);
        return AtomicsWaitCore(realm, view, (uint)index, expected, timeout, false);
    }

    private JsValue AtomicsWaitAsync(in CallInfo info)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        var view = RequireAtomicsTypedArray(args, true);
        if (view.Buffer.IsDetached || !view.Buffer.IsShared)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Atomics.waitAsync requires a SharedArrayBuffer-backed typed array");
        var index = ValidateAtomicsAccess(realm, view, args.Length > 1 ? args[1] : JsValue.Undefined);
        var expected =
            TypedArrayElementKindInfo.NormalizeValue(realm, view.Kind, args.Length > 2 ? args[2] : JsValue.Undefined);
        var timeout = NormalizeWaitTimeout(realm, args.Length > 3 ? args[3] : JsValue.Undefined);
        var byteIndex = GetTypedArrayByteIndex(view, (uint)index);
        var buffer = view.Buffer;
        var capability = CreatePromiseCapabilityWithFunctions(PromiseConstructor);
        var result = CreateAtomicsWaitAsyncResult(realm, true, JsValue.FromObject(capability.Promise));
        JsArrayBufferObject.SharedWaiter? waiter;
        lock (buffer.GetSharedSyncRoot())
        {
            var current = buffer.ReadTypedArrayElement(realm, view.Kind, byteIndex);
            if (!AtomicsSame(view.Kind, current, expected))
                return JsValue.FromObject(CreateAtomicsWaitAsyncResult(realm, false, "not-equal"));

            if (timeout == 0d)
                return JsValue.FromObject(CreateAtomicsWaitAsyncResult(realm, false, "timed-out"));

            waiter = buffer.AddSharedWaiterLocked(realm, byteIndex);
            waiter.Continuation = static state =>
            {
                var data = ((JsRealm Realm, JsArrayBufferObject Buffer, uint ByteIndex,
                    JsArrayBufferObject.SharedWaiter Waiter, JsPromiseObject.PromiseCapability Capability))state!;
                data.Buffer.RemoveSharedWaiter(data.ByteIndex, data.Waiter);
                var notified = data.Waiter.Notified;
                data.Waiter.Dispose();
                var settled = JsValue.FromString(notified ? "ok" : "timed-out");
                data.Realm.Agent.EnqueueHostTask(static taskState =>
                {
                    var taskData =
                        ((JsRealm Realm, JsPromiseObject.PromiseCapability Capability, JsValue Settled))taskState!;
                    taskData.Realm.ResolvePromiseCapability(taskData.Capability, taskData.Settled);
                }, (data.Realm, data.Capability, settled));
            };
            waiter.ContinuationState = (realm, buffer, byteIndex, waiter, capability);
        }

        var timeoutDuration = ToAtomicsWaitTimeout(timeout);
        waiter!.ArmAsyncTimeout(timeoutDuration);

        return JsValue.FromObject(result);
    }

    private static JsValue DoAtomicsReadModifyWrite(JsRealm realm, JsTypedArrayObject view, uint index,
        in JsValue valueArg,
        Func<TypedArrayElementKind, long, long, long> numericOp,
        Func<TypedArrayElementKind, BigInteger, BigInteger, BigInteger> bigIntOp)
    {
        var buffer = view.Buffer;
        var kind = view.Kind;
        var normalized = TypedArrayElementKindInfo.NormalizeValue(realm, kind, valueArg);
        var byteIndex = GetTypedArrayByteIndex(view, index);
        lock (GetAtomicsSyncRoot(buffer))
        {
            var current = buffer.ReadTypedArrayElement(realm, kind, byteIndex);
            var replacement = kind.IsBigIntFamily()
                ? FromAtomicBigInt(kind, bigIntOp(kind, current.AsBigInt().Value, normalized.AsBigInt().Value))
                : FromAtomicInt64(kind, numericOp(kind, ToAtomicInt64(kind, current), ToAtomicInt64(kind, normalized)));
            buffer.WriteNormalizedTypedArrayElement(kind, byteIndex, replacement);
            return current;
        }
    }

    private static JsTypedArrayObject ValidateIntegerTypedArray(JsRealm realm, ReadOnlySpan<JsValue> args,
        out int index, bool requireWaitable)
    {
        var view = RequireAtomicsTypedArray(args, requireWaitable);
        var length = (int)view.Length;
        var numericIndex = args.Length > 1 ? ToAtomicsIndex(realm, args[1]) : 0u;
        if (numericIndex >= length)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Atomics index is out of range");
        index = (int)numericIndex;
        return view;
    }

    private static JsTypedArrayObject RequireAtomicsTypedArray(ReadOnlySpan<JsValue> args, bool waitableOnly)
    {
        if (args.Length == 0 || !TryGetTypedArrayValue(args[0], out var view))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Atomics operation requires a typed array");
        if (!IsAtomicsElementKind(view.Kind))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Atomics operation requires an integer typed array");
        if (waitableOnly && view.Kind is not (TypedArrayElementKind.Int32 or TypedArrayElementKind.BigInt64))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Atomics.wait/notify require Int32Array or BigInt64Array");
        return view;
    }

    private static int ValidateAtomicsAccess(JsRealm realm, JsTypedArrayObject view, in JsValue indexValue)
    {
        var length = (int)view.Length;
        var numericIndex = ToAtomicsIndex(realm, indexValue);
        if (numericIndex >= length)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Atomics index is out of range");
        return (int)numericIndex;
    }

    private static bool IsAtomicsElementKind(TypedArrayElementKind kind)
    {
        return kind is
            TypedArrayElementKind.Int8 or
            TypedArrayElementKind.Uint8 or
            TypedArrayElementKind.Int16 or
            TypedArrayElementKind.Uint16 or
            TypedArrayElementKind.Int32 or
            TypedArrayElementKind.Uint32 or
            TypedArrayElementKind.BigInt64 or
            TypedArrayElementKind.BigUint64;
    }

    private static uint GetTypedArrayByteIndex(JsTypedArrayObject view, uint index)
    {
        return checked(view.StoredByteOffset + index * (uint)view.BytesPerElement);
    }

    private static object GetAtomicsSyncRoot(JsArrayBufferObject buffer)
    {
        return buffer.IsShared ? buffer.GetSharedSyncRoot() : buffer;
    }

    private static JsValue AtomicsWaitCore(JsRealm realm, JsTypedArrayObject view, uint index, in JsValue expected,
        double timeout, bool asyncMode)
    {
        var byteIndex = GetTypedArrayByteIndex(view, index);
        var buffer = view.Buffer;
        var syncRoot = buffer.GetSharedSyncRoot();
        JsArrayBufferObject.SharedWaiter? waiter = null;
        lock (syncRoot)
        {
            var current = buffer.ReadTypedArrayElement(realm, view.Kind, byteIndex);
            if (!AtomicsSame(view.Kind, current, expected))
                return asyncMode
                    ? JsValue.FromObject(CreateAtomicsWaitAsyncResult(realm, false, "not-equal"))
                    : JsValue.FromString("not-equal");

            if (timeout <= 0 || double.IsNaN(timeout))
                return asyncMode
                    ? JsValue.FromObject(CreateAtomicsWaitAsyncResult(realm, false, "timed-out"))
                    : JsValue.FromString("timed-out");

            if (!asyncMode)
                waiter = buffer.AddSharedWaiterLocked(realm, byteIndex);
        }

        if (asyncMode)
            return JsValue.FromObject(CreateAtomicsWaitAsyncResult(realm, true, JsValue.Undefined));

        using var ownedWaiter = waiter!;
        var timeoutDuration = ToAtomicsWaitTimeout(timeout);
        var notified = ownedWaiter.Wait(timeoutDuration);
        buffer.RemoveSharedWaiter(byteIndex, ownedWaiter);
        return JsValue.FromString(notified ? "ok" : "timed-out");
    }

    private static JsPlainObject CreateAtomicsWaitAsyncResult(JsRealm realm, bool async, object value)
    {
        var result = new JsPlainObject(realm);
        result.DefineDataPropertyAtom(realm, IdAsync, async ? JsValue.True : JsValue.False, JsShapePropertyFlags.Open);
        result.DefineDataPropertyAtom(realm, IdValue,
            value switch
            {
                JsValue jsValue => jsValue,
                string s => JsValue.FromString(s),
                _ => JsValue.Undefined
            }, JsShapePropertyFlags.Open);
        return result;
    }

    private static bool AtomicsSame(TypedArrayElementKind kind, in JsValue left, in JsValue right)
    {
        return kind is TypedArrayElementKind.BigInt64 or TypedArrayElementKind.BigUint64
            ? left.AsBigInt().Value == right.AsBigInt().Value
            : ToAtomicInt64(kind, left) == ToAtomicInt64(kind, right);
    }

    private static long ToAtomicInt64(TypedArrayElementKind kind, in JsValue value)
    {
        return kind switch
        {
            TypedArrayElementKind.Int8 or TypedArrayElementKind.Int16 or TypedArrayElementKind.Int32 =>
                value.Int32Value,
            TypedArrayElementKind.Uint8 or TypedArrayElementKind.Uint16 => unchecked((ushort)value.Int32Value),
            TypedArrayElementKind.Uint32 => unchecked((uint)value.NumberValue),
            TypedArrayElementKind.BigInt64 or TypedArrayElementKind.BigUint64 => (long)value.AsBigInt().Value,
            _ => throw new InvalidOperationException("Unsupported Atomics kind")
        };
    }

    private static JsValue FromAtomicInt64(TypedArrayElementKind kind, long value)
    {
        return kind switch
        {
            TypedArrayElementKind.Int8 => JsValue.FromInt32(unchecked((sbyte)value)),
            TypedArrayElementKind.Uint8 => JsValue.FromInt32(unchecked((byte)value)),
            TypedArrayElementKind.Int16 => JsValue.FromInt32(unchecked((short)value)),
            TypedArrayElementKind.Uint16 => JsValue.FromInt32(unchecked((ushort)value)),
            TypedArrayElementKind.Int32 => JsValue.FromInt32(unchecked((int)value)),
            TypedArrayElementKind.Uint32 => new((double)unchecked((uint)value)),
            TypedArrayElementKind.BigInt64 => JsValue.FromBigInt(new(new(value))),
            TypedArrayElementKind.BigUint64 => JsValue.FromBigInt(new(new(unchecked((ulong)value)))),
            _ => throw new InvalidOperationException("Unsupported Atomics kind")
        };
    }

    private static JsValue FromAtomicBigInt(TypedArrayElementKind kind, BigInteger value)
    {
        return kind switch
        {
            TypedArrayElementKind.BigInt64 => JsValue.FromBigInt(new(BigIntAsIntN(64, value))),
            TypedArrayElementKind.BigUint64 => JsValue.FromBigInt(new(BigIntAsUintN(64, value))),
            _ => throw new InvalidOperationException("Unsupported Atomics kind")
        };
    }

    private static uint ToAtomicsIndex(JsRealm realm, in JsValue value)
    {
        var number = realm.ToIntegerOrInfinity(value);
        if (double.IsNaN(number) || number == 0d)
            return 0;
        if (number < 0d)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Atomics index is out of range");
        if (double.IsInfinity(number) || number > uint.MaxValue)
            return uint.MaxValue;
        return (uint)number;
    }

    private static double NormalizeWaitTimeout(JsRealm realm, in JsValue value)
    {
        if (value.IsUndefined)
            return double.PositiveInfinity;
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number))
            return double.PositiveInfinity;
        return Math.Max(number, 0d);
    }

    private static TimeSpan? ToAtomicsWaitTimeout(double timeout)
    {
        if (double.IsPositiveInfinity(timeout))
            return null;

        return TimeSpan.FromMilliseconds(Math.Min(timeout, int.MaxValue));
    }
}
