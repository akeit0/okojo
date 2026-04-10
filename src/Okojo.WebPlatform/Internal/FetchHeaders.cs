using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebPlatform.Internal;

internal static class FetchHeaders
{
    public static JsPlainObject Create(JsRealm realm, IReadOnlyDictionary<string, string> headers)
    {
        var shape = WebPlatformShapeCache.For(realm).Headers;
        var headersObject = new JsPlainObject(shape.Shape);

        headersObject.SetNamedSlotUnchecked(WebPlatformShapeCache.HeaderShapeCache.GetSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
                var name = NormalizeHeaderName(info.GetArgumentOrDefault(0, JsValue.Undefined));
                return headers.TryGetValue(name, out var value) ? JsValue.FromString(value) : JsValue.Null;
            }, "get", 1, false)
            {
                UserData = headers
            }));

        headersObject.SetNamedSlotUnchecked(WebPlatformShapeCache.HeaderShapeCache.HasSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
                var name = NormalizeHeaderName(info.GetArgumentOrDefault(0, JsValue.Undefined));
                return headers.ContainsKey(name) ? JsValue.True : JsValue.False;
            }, "has", 1)
            {
                UserData = headers
            }));

        headersObject.SetNamedSlotUnchecked(WebPlatformShapeCache.HeaderShapeCache.KeysSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
                return JsValue.FromObject(CreateStringArray(info.Realm, headers.Keys));
            }, "keys", 0)
            {
                UserData = headers
            }));

        headersObject.SetNamedSlotUnchecked(WebPlatformShapeCache.HeaderShapeCache.ValuesSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
                return JsValue.FromObject(CreateStringArray(info.Realm, headers.Values));
            }, "values", 0)
            {
                UserData = headers
            }));

        headersObject.SetNamedSlotUnchecked(WebPlatformShapeCache.HeaderShapeCache.EntriesSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var headers = (IReadOnlyDictionary<string, string>)((JsHostFunction)info.Function).UserData!;
                var result = info.Realm.CreateArray();
                uint index = 0;
                foreach (var pair in headers)
                {
                    var entry = info.Realm.CreateArray();
                    entry.SetElement(0, JsValue.FromString(pair.Key));
                    entry.SetElement(1, JsValue.FromString(pair.Value));
                    result.SetElement(index++, JsValue.FromObject(entry));
                }

                return JsValue.FromObject(result);
            }, "entries", 0)
            {
                UserData = headers
            }));

        return headersObject;
    }

    private static string NormalizeHeaderName(in JsValue value)
    {
        return (value.IsString ? value.AsString() : value.ToString()).ToLowerInvariant();
    }

    private static JsArray CreateStringArray(JsRealm realm, IEnumerable<string> values)
    {
        var array = realm.CreateArray();
        uint index = 0;
        foreach (var value in values)
            array.SetElement(index++, JsValue.FromString(value));
        return array;
    }
}
