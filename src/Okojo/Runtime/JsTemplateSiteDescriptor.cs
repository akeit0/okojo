namespace Okojo.Runtime;

internal sealed class JsTemplateSiteDescriptor(string?[] cooked, string[] raw)
{
    private readonly Dictionary<JsRealm, JsArray> cache = new();

    public JsArray GetOrCreate(JsRealm realm)
    {
        if (cache.TryGetValue(realm, out var existing))
            return existing;

        var cookedArray = realm.CreateArrayObject();
        for (var i = 0; i < cooked.Length; i++)
            cookedArray.DefineElementDescriptor((uint)i, PropertyDescriptor.Data(
                cooked[i] is null ? JsValue.Undefined : JsValue.FromString(cooked[i]!),
                false,
                true));

        var rawArray = realm.CreateArrayObject();
        for (var i = 0; i < raw.Length; i++)
            rawArray.DefineElementDescriptor((uint)i, PropertyDescriptor.Data(
                JsValue.FromString(raw[i]),
                false,
                true));

        rawArray.FreezeDataProperties();
        rawArray.PreventExtensions();

        const int rawAtom = IdRaw;
        _ = cookedArray.DefineOwnDataPropertyExact(realm, rawAtom, JsValue.FromObject(rawArray),
            JsShapePropertyFlags.None);
        cookedArray.FreezeDataProperties();
        cookedArray.PreventExtensions();

        cache[realm] = cookedArray;
        return cookedArray;
    }
}
