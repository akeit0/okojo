using System.Globalization;
using Okojo.JavaScript.Internals;
using static Okojo.JavaScript.Execution.JsRealm;

namespace Okojo.JavaScript.Execution;

public partial class Intrinsics
{
    // The default species and element-free prototypes allow copying holes as-is.
    internal static bool TrySliceDense(
        JsRealm realm,
        JsObject obj,
        long length,
        long start,
        long end,
        out JsArray result
    )
    {
        result = null!;
        if (
            !TryOpenDenseRange(obj, length, out var source, out var sourceStore)
            || !HasDefaultSliceSpecies(realm, source)
            || !ReceiverChainFreeOfElements(source)
        )
            return false;

        var count = (int)Math.Max(0, end - start);
        result = realm.CreateArrayObject();
        sourceStore
            .AsSpan((int)start, count)
            .CopyTo(result.InitializeDenseElementsNoCollision(count));
        return true;
    }

    private static bool HasDefaultSliceSpecies(JsRealm realm, JsArray source)
    {
        var prototype = realm.ArrayPrototype;
        var objectPrototype = realm.ObjectPrototype;
        var constructor = realm.ArrayConstructor;
        return ReferenceEquals(source.Prototype, prototype)
            && ReferenceEquals(prototype.Prototype, objectPrototype)
            && objectPrototype.Prototype is null
            && !source.HasOwnPropertyAtom(realm, IdConstructor)
            && prototype.TryGetOwnNamedPropertyDescriptorAtom(realm, IdConstructor, out var ctor)
            && ReferenceEquals(ctor.Value.Obj, constructor)
            && constructor.TryGetOwnNamedPropertyDescriptorAtom(
                realm,
                IdSymbolSpecies,
                out var species
            )
            && ReferenceEquals(species.Getter, realm.Intrinsics.arraySpeciesGetter);
    }

    private static JsObject SliceArrayLike(JsRealm realm, JsObject source, long start, long end)
    {
        var count = Math.Max(0, end - start);
        var constructor = GetSliceSpeciesConstructor(realm, source);
        JsArray? defaultArray = null;
        JsObject result;
        if (constructor is null || ReferenceEquals(constructor, realm.ArrayConstructor))
        {
            var length = RequireArrayStorageLength(count);
            defaultArray = realm.CreateArrayObject();
            defaultArray.SetLength(length);
            result = defaultArray;
        }
        else
        {
            var lengthArg = new InlineJsValueArray1 { Item0 = FromLength(count) };
            result = realm
                .ConstructWithExplicitNewTarget(constructor, lengthArg.AsSpan(), constructor, 0)
                .AsObject();
        }

        for (var k = start; k < end; k++)
        {
            var key = JsValue.FromString(k.ToString(CultureInfo.InvariantCulture));
            if (!HasPropertySlowPath(realm, source, key))
                continue;
            JsValue value;
            if ((ulong)k < uint.MaxValue)
                source.TryGetElementWithReceiver(realm, source, (uint)k, out value);
            else
                source.TryGetPropertyAtom(
                    realm,
                    realm.Atoms.InternNoCheck(key.AsString()),
                    out value,
                    out _
                );
            var to = k - start;
            if (defaultArray is not null)
                FreshArrayOperations.DefineElement(defaultArray, (uint)to, value);
            else
                CreateDataPropertyOrThrowForArrayLike(
                    realm,
                    result,
                    to,
                    value,
                    "Array.prototype.slice"
                );
        }

        if (defaultArray is null)
            SetArrayLikeLengthOrThrow(realm, result, count, "Array.prototype.slice");
        return result;
    }

    private static JsFunction? GetSliceSpeciesConstructor(JsRealm realm, JsObject source)
    {
        if (!IsArrayObject(realm, source))
            return null;

        source.TryGetPropertyAtom(realm, IdConstructor, out var constructor, out _);
        if (constructor.TryGetObject(out var constructorObject))
        {
            if (constructorObject is JsFunction { IsConstructor: true } function)
            {
                var functionRealm = GetFunctionRealm(realm, function);
                if (
                    functionRealm != realm
                    && ReferenceEquals(function, functionRealm.ArrayConstructor)
                )
                    return null;
            }
            constructorObject.TryGetPropertyAtom(realm, IdSymbolSpecies, out constructor, out _);
            if (constructor.IsNull)
                return null;
        }

        if (constructor.IsUndefined)
            return null;
        if (
            constructor.TryGetObject(out var species)
            && species is JsFunction { IsConstructor: true } ctor
        )
            return ctor;
        throw new JsRuntimeException(JsErrorKind.TypeError, "Array species must be a constructor");
    }
}
