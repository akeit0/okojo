using Okojo.Internals;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallArrayConstructorBuiltins()
    {
        const int atomIsArray = IdIsArray;
        const int atomFrom = IdFrom;
        const int atomFromAsync = IdFromAsync;
        const int atomOf = IdOf;
        var speciesGetter = new JsHostFunction(Realm, (in info) => { return info.ThisValue; }, "get [Symbol.species]",
            0);
        var isArrayFn = new JsHostFunction(Realm, "isArray", 1, static (in info) =>
        {
            var args = info.Arguments;
            if (args.Length == 0 || !args[0].TryGetObject(out var obj))
                return JsValue.False;
            while (true)
            {
                if (obj is JsArray)
                    return JsValue.True;
                if (obj.TryGetProxyTargetOrThrow(info.Realm, out var target))
                {
                    obj = target;
                    continue;
                }

                return JsValue.False;
            }
        }, false);
        var fromFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var items))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Array.from items must not be null or undefined");

            JsFunction? mapFn = null;
            var thisArg = JsValue.Undefined;
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                if (!args[1].TryGetObject(out var mapFnObj) || mapFnObj is not JsFunction callback)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Array.from mapfn must be a function");
                mapFn = callback;
                if (args.Length > 2)
                    thisArg = args[2];
            }

            if (JsRealm.TryGetIteratorMethodForArrayFrom(realm, items, out var iteratorMethod))
            {
                long index = 0;
                var result = realm.CreateArrayFromTarget(thisValue, true, 0,
                    "Array.from");
                var iterator = JsRealm.GetIteratorObjectForArrayFrom(realm, items, iteratorMethod);
                try
                {
                    while (true)
                    {
                        var nextValue = realm.StepIteratorForArrayFrom(iterator, out var done);
                        if (done)
                        {
                            SetArrayLikeLengthOrThrow(realm, result, index, "Array.from");
                            return result;
                        }

                        var mappedValue = mapFn is null
                            ? nextValue
                            : realm.InvokeFunction(mapFn, thisArg, new InlineJsValueArray2
                            {
                                Item0 = nextValue,
                                Item1 = index <= int.MaxValue ? JsValue.FromInt32((int)index) : new(index)
                            }.AsSpan());
                        CreateDataPropertyOrThrowForArrayLike(realm, result, index, mappedValue, "Array.from");
                        index++;
                    }
                }
                catch
                {
                    realm.BestEffortIteratorCloseOnThrow(iterator);
                    throw;
                }
            }

            var length = realm.GetArrayFromLength(items);
            var resultArrayLike = realm.CreateArrayFromTarget(thisValue, false, length,
                "Array.from");
            realm.PopulateArrayFromArrayLike(resultArrayLike, length, items, mapFn, thisArg);
            return resultArrayLike;
        }, "from", 1);
        var ofFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            long len = args.Length;
            JsObject target;
            if (thisValue.TryGetObject(out var ctorObj) && ctorObj is JsFunction ctor && ctor.IsConstructor)
            {
                var lenArg = new InlineJsValueArray1
                {
                    Item0 = len <= int.MaxValue ? JsValue.FromInt32((int)len) : new(len)
                };
                var created = realm.ConstructWithExplicitNewTarget(ctor, lenArg.AsSpan(), ctor, 0);
                if (!created.TryGetObject(out target!))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Array.of constructor must return an object");
            }
            else
            {
                target = realm.CreateArrayObject();
            }

            for (long i = 0; i < len; i++)
                CreateDataPropertyOrThrowForArrayLike(realm, target, i, args[(int)i], "Array.of");
            SetArrayLikeLengthOrThrow(realm, target, len, "Array.of");
            return target;
        }, "of", 0);
        var fromAsyncFn = CreateArrayFromAsyncFunction();

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.GetterData(IdSymbolSpecies, speciesGetter, configurable: true),
            PropertyDefinition.Mutable(atomIsArray, JsValue.FromObject(isArrayFn)),
            PropertyDefinition.Mutable(atomFrom, JsValue.FromObject(fromFn)),
            PropertyDefinition.Mutable(atomFromAsync, JsValue.FromObject(fromAsyncFn)),
            PropertyDefinition.Mutable(atomOf, JsValue.FromObject(ofFn))
        ];
        ArrayConstructor.InitializePrototypeProperty(ArrayPrototype);
        ArrayConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);
    }
}
