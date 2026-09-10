namespace Okojo.JavaScript.Execution;

internal sealed class JsTemplateSiteDescriptor(string?[] cooked, string[] raw)
{
    internal JsArray Create(JsRealm realm)
    {
        var cookedArray = realm.CreateArrayObject();
        for (var i = 0; i < cooked.Length; i++)
            cookedArray.DefineElementDescriptor(
                (uint)i,
                PropertyDescriptor.Data(
                    cooked[i] is null ? JsValue.Undefined : JsValue.FromString(cooked[i]!),
                    false,
                    true
                )
            );

        var rawArray = realm.CreateArrayObject();
        for (var i = 0; i < raw.Length; i++)
            rawArray.DefineElementDescriptor(
                (uint)i,
                PropertyDescriptor.Data(JsValue.FromString(raw[i]), false, true)
            );

        rawArray.FreezeDataProperties();
        rawArray.PreventExtensions();

        const int rawAtom = IdRaw;
        _ = cookedArray.DefineOwnDataPropertyExact(
            realm,
            rawAtom,
            JsValue.FromObject(rawArray),
            JsShapePropertyFlags.None
        );
        cookedArray.FreezeDataProperties();
        cookedArray.PreventExtensions();

        return cookedArray;
    }
}

// Template identity and its frozen arrays belong to the linked realm, not the code graph.
internal sealed class JsTemplateSite(JsRealm realm, JsTemplateSiteDescriptor descriptor)
{
    private JsArray? templateObject;

    internal JsArray GetOrCreate() => templateObject ??= descriptor.Create(realm);
}
