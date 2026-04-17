using System.Diagnostics;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private const int SuppressedErrorErrorSlot = 0;
    private const int SuppressedErrorSuppressedSlot = 1;
    private const int SuppressedErrorWithMessageMessageSlot = 0;
    private const int SuppressedErrorWithMessageErrorSlot = 1;
    private const int SuppressedErrorWithMessageSuppressedSlot = 2;

    private StaticNamedPropertyLayout? suppressedErrorShape;
    private StaticNamedPropertyLayout? suppressedErrorWithMessageShape;

    private JsHostFunction CreateErrorConstructor()
    {
        return CreateNativeErrorConstructor("Error", ErrorPrototype);
    }

    private JsHostFunction CreateAggregateErrorConstructor()
    {
        const int atomErrors = IdErrors;
        const int atomCause = IdCause;
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            var err = CreateAggregateErrorInstance(in info);
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                var message = realm.ToJsStringSlowPath(args[1]);
                err.DefineDataPropertyAtom(realm, IdMessage, JsValue.FromString(message),
                    JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
            }

            var errorsArray = CollectAggregateErrorErrors(args.Length > 0 ? args[0] : JsValue.Undefined);
            err.DefineDataPropertyAtom(realm, atomErrors, JsValue.FromObject(errorsArray),
                JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);

            if (args.Length > 2)
                InstallErrorCause(err, args[2], atomCause);

            return err;
        }, "AggregateError", 2, true);
    }

    private JsHostFunction CreateSuppressedErrorConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, (JsHostFunction)info.Function,
                SuppressedErrorPrototype);
            if (args.Length > 2 && !args[2].IsUndefined)
            {
                var message = JsValue.FromString(realm.ToJsStringSlowPath(args[2]));
                return CreateSuppressedErrorInstance(prototype,
                    args.Length > 0 ? args[0] : JsValue.Undefined,
                    args.Length > 1 ? args[1] : JsValue.Undefined,
                    message);
            }

            return CreateSuppressedErrorInstance(prototype,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined);
        }, "SuppressedError", 3, true);
    }

    private JsPlainObject CreateSuppressedErrorInstance(JsObject prototype, in JsValue error, in JsValue suppressed,
        JsValue? message = null)
    {
        if (message is JsValue messageValue)
        {
            var shape = suppressedErrorWithMessageShape ??= CreateSuppressedErrorWithMessageShape();
            var err = new JsPlainObject(shape, false)
            {
                Prototype = prototype
            };
            err.SetNamedSlotUnchecked(SuppressedErrorWithMessageMessageSlot, messageValue);
            err.SetNamedSlotUnchecked(SuppressedErrorWithMessageErrorSlot, error);
            err.SetNamedSlotUnchecked(SuppressedErrorWithMessageSuppressedSlot, suppressed);
            return err;
        }

        var baseShape = suppressedErrorShape ??= CreateSuppressedErrorShape();
        var result = new JsPlainObject(baseShape, false)
        {
            Prototype = prototype
        };
        result.SetNamedSlotUnchecked(SuppressedErrorErrorSlot, error);
        result.SetNamedSlotUnchecked(SuppressedErrorSuppressedSlot, suppressed);
        return result;
    }

    private StaticNamedPropertyLayout CreateSuppressedErrorShape()
    {
        var flags = JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable;
        var shape = Realm.EmptyShape.GetOrAddTransition(IdErrorProperty, flags, out var errorInfo);
        shape = shape.GetOrAddTransition(IdSuppressed, flags, out var suppressedInfo);
        Debug.Assert(errorInfo.Slot == SuppressedErrorErrorSlot);
        Debug.Assert(suppressedInfo.Slot == SuppressedErrorSuppressedSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateSuppressedErrorWithMessageShape()
    {
        var flags = JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable;
        var shape = Realm.EmptyShape.GetOrAddTransition(IdMessage, flags, out var messageInfo);
        shape = shape.GetOrAddTransition(IdErrorProperty, flags, out var errorInfo);
        shape = shape.GetOrAddTransition(IdSuppressed, flags, out var suppressedInfo);
        Debug.Assert(messageInfo.Slot == SuppressedErrorWithMessageMessageSlot);
        Debug.Assert(errorInfo.Slot == SuppressedErrorWithMessageErrorSlot);
        Debug.Assert(suppressedInfo.Slot == SuppressedErrorWithMessageSuppressedSlot);
        return shape;
    }

    internal JsHostFunction CreateNativeErrorConstructor(string name, JsObject prototype)
    {
        const int atomCause = IdCause;
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var err = CreateNativeErrorInstance(in info, prototype);
            if (args.Length != 0 && !args[0].IsUndefined)
            {
                var message = realm.ToJsStringSlowPath(args[0]);
                err.DefineDataPropertyAtom(realm, IdMessage, JsValue.FromString(message),
                    JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
            }

            if (args.Length > 1)
                InstallErrorCause(err, args[1], atomCause);

            return err;
        }, name, 1, true);
    }

    internal JsPlainObject CreateNativeErrorInstance(in CallInfo info,
        JsObject intrinsicDefaultPrototype)
    {
        return CreateErrorInstance(in info, intrinsicDefaultPrototype);
    }

    internal JsPlainObject CreateAggregateErrorInstance(in CallInfo info)
    {
        return CreateErrorInstance(in info, AggregateErrorPrototype);
    }

    internal JsPlainObject CreateErrorInstance(in CallInfo info, JsObject intrinsicDefaultPrototype)
    {
        if (info.IsConstruct &&
            info.ThisValue.TryGetObject(out var existingObj) &&
            existingObj is JsPlainObject existingPlainObject)
            return existingPlainObject;

        var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, (JsHostFunction)info.Function,
            intrinsicDefaultPrototype);
        return new(Realm, false)
        {
            Prototype = prototype
        };
    }

    internal JsObject GetPrototypeFromConstructorOrIntrinsic(in JsValue newTarget, JsHostFunction activeFunction,
        JsObject intrinsicDefaultPrototype)
    {
        JsObject constructorObject;
        if (newTarget.TryGetObject(out var newTargetObj) && newTargetObj is JsFunction)
            constructorObject = newTargetObj;
        else
            constructorObject = activeFunction;

        if (constructorObject.TryGetPropertyAtom(Realm, IdPrototype, out var prototypeValue, out _) &&
            prototypeValue.TryGetObject(out var prototypeObj))
            return prototypeObj;

        if (constructorObject is JsFunction constructorFunction)
            return GetIntrinsicPrototypeForRealm(GetFunctionRealm(activeFunction.Realm, constructorFunction),
                activeFunction.Realm,
                intrinsicDefaultPrototype);

        return intrinsicDefaultPrototype;
    }

    internal static JsRealm GetFunctionRealm(JsRealm errorRealm, JsFunction function)
    {
        if (function is JsBoundFunction bound)
            return GetFunctionRealm(errorRealm, bound.Target);
        if (function.TryGetProxyTargetOrThrow(errorRealm, out var target) &&
            target is JsFunction targetFunction)
            return GetFunctionRealm(errorRealm, targetFunction);

        return function.Realm;
    }

    private static JsObject GetIntrinsicPrototypeForRealm(JsRealm targetRealm, JsRealm sourceRealm,
        JsObject intrinsicDefaultPrototype)
    {
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.ArrayPrototype))
            return targetRealm.ArrayPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.MapPrototype))
            return targetRealm.MapPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.SetPrototype))
            return targetRealm.SetPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.FunctionPrototype))
            return targetRealm.FunctionPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.AggregateErrorPrototype))
            return targetRealm.AggregateErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.SuppressedErrorPrototype))
            return targetRealm.SuppressedErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.GeneratorFunctionPrototype))
            return targetRealm.GeneratorFunctionPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.AsyncFunctionPrototype))
            return targetRealm.AsyncFunctionPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.AsyncGeneratorFunctionPrototype))
            return targetRealm.AsyncGeneratorFunctionPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.ErrorPrototype))
            return targetRealm.ErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.TypeErrorPrototype))
            return targetRealm.TypeErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.ReferenceErrorPrototype))
            return targetRealm.ReferenceErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.RangeErrorPrototype))
            return targetRealm.RangeErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.SyntaxErrorPrototype))
            return targetRealm.SyntaxErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.EvalErrorPrototype))
            return targetRealm.EvalErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.UriErrorPrototype))
            return targetRealm.UriErrorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.WeakMapPrototype))
            return targetRealm.WeakMapPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.WeakSetPrototype))
            return targetRealm.WeakSetPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.WeakRefPrototype))
            return targetRealm.WeakRefPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.FinalizationRegistryPrototype))
            return targetRealm.FinalizationRegistryPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.ArrayBufferPrototype))
            return targetRealm.ArrayBufferPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.DataViewPrototype))
            return targetRealm.DataViewPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.SharedArrayBufferPrototype))
            return targetRealm.SharedArrayBufferPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.TypedArrayPrototype))
            return targetRealm.TypedArrayPrototype;
        for (var i = 0; i < 12; i++)
        {
            var kind = (TypedArrayElementKind)i;
            if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.Intrinsics.GetTypedArrayPrototype(kind)))
                return targetRealm.Intrinsics.GetTypedArrayPrototype(kind);
        }

        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.IteratorPrototype))
            return targetRealm.IteratorPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.StringPrototype))
            return targetRealm.StringPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.NumberPrototype))
            return targetRealm.NumberPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.BooleanPrototype))
            return targetRealm.BooleanPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.PromisePrototype))
            return targetRealm.PromisePrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.DisposableStackPrototype))
            return targetRealm.DisposableStackPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.AsyncDisposableStackPrototype))
            return targetRealm.AsyncDisposableStackPrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.DatePrototype))
            return targetRealm.DatePrototype;
        if (ReferenceEquals(intrinsicDefaultPrototype, sourceRealm.RegExpPrototype))
            return targetRealm.RegExpPrototype;
        return targetRealm.ObjectPrototype;
    }

    private JsArray CollectAggregateErrorErrors(in JsValue errors)
    {
        var realm = Realm;
        if (!realm.TryToObject(errors, out var iterableObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "errors is not iterable");

        if (!iterableObj.TryGetPropertyAtom(realm, IdSymbolIterator, out var iteratorMethod, out _) ||
            iteratorMethod.IsUndefined || iteratorMethod.IsNull)
            throw new JsRuntimeException(JsErrorKind.TypeError, "errors is not iterable");

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.iterator is not a function");

        var iteratorValue =
            realm.InvokeFunction(iteratorFn, JsValue.FromObject(iterableObj), ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "iterator is not an object");

        if (!iteratorObj.TryGetPropertyAtom(realm, IdNext, out var nextMethod, out _) ||
            !nextMethod.TryGetObject(out var nextFnObj) || nextFnObj is not JsFunction)
            throw new JsRuntimeException(JsErrorKind.TypeError, "iterator.next is not a function");

        var result = realm.CreateArrayObject();
        uint index = 0;
        while (true)
        {
            JsValue nextValue;
            bool done;
            try
            {
                nextValue = realm.DestructureArrayStepValue(iteratorObj, out done);
            }
            catch
            {
                realm.BestEffortIteratorCloseOnThrow(iteratorObj);
                throw;
            }

            if (done)
                return result;

            FreshArrayOperations.DefineElement(result, index++, nextValue);
        }
    }

    internal void InstallErrorCause(JsObject errorObject, in JsValue options, int atomCause)
    {
        var realm = Realm;
        if (!options.TryGetObject(out var optionsObj))
            return;

        var causeKey = JsValue.FromString("cause");
        if (!JsRealm.HasPropertySlowPath(realm, optionsObj, causeKey))
            return;

        _ = optionsObj.TryGetPropertyAtom(realm, atomCause, out var causeValue, out _);
        errorObject.DefineDataPropertyAtom(realm, atomCause, causeValue,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
    }

    private void InstallErrorConstructorBuiltins()
    {
        var toStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var target))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Error.prototype.toString called on incompatible receiver",
                    "ERROR_TOSTRING_BAD_RECEIVER");

            _ = target.TryGetPropertyAtom(realm, IdName, out var name, out _);
            _ = target.TryGetPropertyAtom(realm, IdMessage, out var message, out _);
            var nameText = name.IsUndefined ? "Error" : realm.ToJsStringSlowPath(name);
            var messageText = message.IsUndefined ? string.Empty : realm.ToJsStringSlowPath(message);
            if (nameText.Length == 0) return messageText;
            if (messageText.Length == 0) return nameText;
            return $"{nameText}: {messageText}";
        }, "toString", 0);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(ErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("Error")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn))
        ];
        ErrorPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);

        ErrorConstructor.InitializePrototypeProperty(ErrorPrototype);
        TypeErrorConstructor.Prototype = ErrorConstructor;
        ReferenceErrorConstructor.Prototype = ErrorConstructor;
        RangeErrorConstructor.Prototype = ErrorConstructor;
        SyntaxErrorConstructor.Prototype = ErrorConstructor;
        EvalErrorConstructor.Prototype = ErrorConstructor;
        UriErrorConstructor.Prototype = ErrorConstructor;

        TypeErrorConstructor.InitializePrototypeProperty(TypeErrorPrototype);

        ReferenceErrorConstructor.InitializePrototypeProperty(ReferenceErrorPrototype);

        RangeErrorConstructor.InitializePrototypeProperty(RangeErrorPrototype);

        SyntaxErrorConstructor.InitializePrototypeProperty(SyntaxErrorPrototype);

        EvalErrorConstructor.InitializePrototypeProperty(EvalErrorPrototype);

        UriErrorConstructor.InitializePrototypeProperty(UriErrorPrototype);
        AggregateErrorConstructor.InitializePrototypeProperty(AggregateErrorPrototype);
        SuppressedErrorConstructor.InitializePrototypeProperty(SuppressedErrorPrototype);

        Span<PropertyDefinition> typeProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(TypeErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("TypeError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        TypeErrorPrototype.DefineNewPropertiesNoCollision(Realm, typeProtoDefs);

        Span<PropertyDefinition> referenceProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(ReferenceErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("ReferenceError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        ReferenceErrorPrototype.DefineNewPropertiesNoCollision(Realm, referenceProtoDefs);

        Span<PropertyDefinition> rangeProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(RangeErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("RangeError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        RangeErrorPrototype.DefineNewPropertiesNoCollision(Realm, rangeProtoDefs);

        Span<PropertyDefinition> syntaxProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(SyntaxErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("SyntaxError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        SyntaxErrorPrototype.DefineNewPropertiesNoCollision(Realm, syntaxProtoDefs);

        Span<PropertyDefinition> evalProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(EvalErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("EvalError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        EvalErrorPrototype.DefineNewPropertiesNoCollision(Realm, evalProtoDefs);

        Span<PropertyDefinition> uriProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(UriErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("URIError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        UriErrorPrototype.DefineNewPropertiesNoCollision(Realm, uriProtoDefs);

        Span<PropertyDefinition> aggregateProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(AggregateErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("AggregateError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        AggregateErrorPrototype.DefineNewPropertiesNoCollision(Realm, aggregateProtoDefs);

        Span<PropertyDefinition> suppressedProtoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(SuppressedErrorConstructor)),
            PropertyDefinition.Mutable(IdName, JsValue.FromString("SuppressedError")),
            PropertyDefinition.Mutable(IdMessage, JsValue.FromString(string.Empty))
        ];
        SuppressedErrorPrototype.DefineNewPropertiesNoCollision(Realm, suppressedProtoDefs);
    }
}
