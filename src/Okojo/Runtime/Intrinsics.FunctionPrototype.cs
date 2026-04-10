namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallFunctionPrototypeBuiltins()
    {
        const int atomApply = IdApply;
        const int atomCall = IdCall;
        const int atomBind = IdBind;
        const int atomHasInstance = IdSymbolHasInstance;
        var atomCaller = Atoms.InternNoCheck("caller");
        var atomArguments = Atoms.InternNoCheck("arguments");

        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                if (thisValue.TryGetObject(out var obj) && obj is JsFunction fn)
                {
                    if (fn is JsBytecodeFunction { IsArrow: true })
                        return "function () { [native code] }";
                    if (fn is JsBytecodeFunction bytecodeFn &&
                        !string.IsNullOrEmpty(bytecodeFn.Script.FunctionSourceText))
                        return bytecodeFn.Script.FunctionSourceText!;
                    return "function () { [native code] }";
                }

                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Function.prototype.toString requires that 'this' be a Function");
            }, "toString", 0);

        var callFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Function.prototype.call called on incompatible receiver");

                var callThis = args.Length > 0 ? args[0] : JsValue.Undefined;
                var callArgs = args.Length > 1 ? args[1..] : ReadOnlySpan<JsValue>.Empty;
                return realm.InvokeFunction(fn, callThis, callArgs);
            }, "call", 1);

        var applyFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Function.prototype.apply called on incompatible receiver", errorRealm: info.Function.Realm);

                var callThis = args.Length > 0 ? args[0] : JsValue.Undefined;
                if (args.Length < 2 || args[1].IsNullOrUndefined)
                    return realm.InvokeFunction(fn, callThis, ReadOnlySpan<JsValue>.Empty);
                if (!args[1].TryGetObject(out _))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "CreateListFromArrayLike requires object", errorRealm: info.Function.Realm);

                return realm.InvokeFunctionWithArrayLikeArguments(fn, callThis, args[1], 0);
            }, "apply", 2);

        var hasInstanceFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsFunction fn)
                    return JsValue.False;
                return OrdinaryHasInstance(info.Realm, fn,
                    info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0])
                    ? JsValue.True
                    : JsValue.False;
            }, "[Symbol.hasInstance]", 1);

        var bindFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var fnObj) || fnObj is not JsFunction target)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Function.prototype.bind called on incompatible receiver");

                var boundThis = args.Length > 0 ? args[0] : JsValue.Undefined;
                var boundArgs = args.Length > 1 ? args[1..].ToArray() : Array.Empty<JsValue>();
                var boundName = GetBoundFunctionName(realm, target);
                var boundLengthNumber = GetBoundFunctionLength(realm, target, boundArgs.Length);
                var boundLengthField = double.IsPositiveInfinity(boundLengthNumber)
                    ? int.MaxValue
                    : boundLengthNumber <= 0d
                        ? 0
                        : boundLengthNumber >= int.MaxValue
                            ? int.MaxValue
                            : (int)boundLengthNumber;
                var bound = new JsBoundFunction(realm, target, boundThis, boundArgs, boundName, boundLengthField);
                _ = bound.DefineOwnDataPropertyExact(realm, IdName, JsValue.FromString(boundName),
                    JsShapePropertyFlags.Configurable);
                _ = bound.DefineOwnDataPropertyExact(realm, IdLength, new(boundLengthNumber),
                    JsShapePropertyFlags.Configurable);
                return bound;
            }, "bind", 1);

        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(FunctionConstructor)),
            PropertyDefinition.Mutable(atomApply, JsValue.FromObject(applyFn)),
            PropertyDefinition.Mutable(atomCall, JsValue.FromObject(callFn)),
            PropertyDefinition.Mutable(atomBind, JsValue.FromObject(bindFn)),
            PropertyDefinition.GetterSetterData(atomArguments, ThrowTypeErrorIntrinsic, ThrowTypeErrorIntrinsic,
                configurable: true),
            PropertyDefinition.GetterSetterData(atomCaller, ThrowTypeErrorIntrinsic, ThrowTypeErrorIntrinsic,
                configurable: true),
            PropertyDefinition.Const(atomHasInstance, JsValue.FromObject(hasInstanceFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn))
        ];
        FunctionPrototype.DefineNewPropertiesNoCollision(Realm, defs);
    }

    private static string GetBoundFunctionName(JsRealm realm, JsFunction target)
    {
        var targetName = string.Empty;
        if (target.TryGetPropertyAtom(realm, IdName, out var nameValue, out _) && nameValue.IsString)
            targetName = nameValue.AsString();
        return $"bound {targetName}";
    }

    private static double GetBoundFunctionLength(JsRealm realm, JsFunction target, int boundArgCount)
    {
        if (!target.HasOwnPropertyAtom(realm, IdLength))
            return 0d;
        if (!target.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
            return 0d;
        if (!lengthValue.IsNumber)
            return 0d;

        var targetLength = realm.ToIntegerOrInfinity(lengthValue);
        if (double.IsPositiveInfinity(targetLength))
            return double.PositiveInfinity;
        if (double.IsNegativeInfinity(targetLength))
            return 0d;
        return Math.Max(targetLength - boundArgCount, 0d);
    }

    private static bool OrdinaryHasInstance(JsRealm realm, JsFunction fn, in JsValue candidate)
    {
        if (fn is JsBoundFunction bound)
            return OrdinaryHasInstance(realm, bound.Target, candidate);
        if (!candidate.TryGetObject(out var candidateObject))
            return false;

        if (!fn.TryGetPropertyAtom(realm, IdPrototype, out var prototypeValue, out _))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Function has non-object prototype in instanceof check");
        if (!prototypeValue.TryGetObject(out var prototypeObject))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Function has non-object prototype in instanceof check");

        for (var current = candidateObject.GetPrototypeOf(realm);
             current is not null;
             current = current.GetPrototypeOf(realm))
            if (ReferenceEquals(current, prototypeObject))
                return true;

        return false;
    }
}
