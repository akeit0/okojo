using System.Text;
using System.Text.Json;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebPlatform.Internal;

internal static class FetchResponseFactory
{
    public static JsPlainObject Create(
        JsRealm realm,
        int status,
        string statusText,
        string url,
        byte[] bodyBytes,
        IReadOnlyDictionary<string, string> headers)
    {
        var shape = WebPlatformShapeCache.For(realm).Response;
        var response = new JsPlainObject(shape.Shape);
        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.OkSlot,
            status is >= 200 and < 300 ? JsValue.True : JsValue.False);
        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.StatusSlot, JsValue.FromInt32(status));
        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.StatusTextSlot,
            JsValue.FromString(statusText));
        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.UrlSlot, JsValue.FromString(url));
        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.HeadersSlot,
            JsValue.FromObject(FetchHeaders.Create(realm, headers)));

        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.TextSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
                return info.Realm.WrapTask(Task.FromResult(JsValue.FromString(Encoding.UTF8.GetString(bytes))));
            }, "text", 0)
            {
                UserData = bodyBytes
            }));

        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.JsonSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
                using var document = JsonDocument.Parse(bytes);
                return info.Realm.WrapTask(
                    Task.FromResult(ConvertJsonElementToJsValue(info.Realm, document.RootElement)));
            }, "json", 0)
            {
                UserData = bodyBytes
            }));

        response.SetNamedSlotUnchecked(WebPlatformShapeCache.ResponseShapeCache.ArrayBufferSlot, JsValue.FromObject(
            new JsHostFunction(realm, static (in info) =>
            {
                var bytes = (byte[])((JsHostFunction)info.Function).UserData!;
                return info.Realm.WrapTask(Task.FromResult(JsValue.FromObject(CreateArrayBuffer(info.Realm, bytes))));
            }, "arrayBuffer", 0)
            {
                UserData = bodyBytes
            }));

        return response;
    }

    private static JsValue ConvertJsonElementToJsValue(JsRealm realm, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return JsValue.Null;
            case JsonValueKind.False:
                return JsValue.False;
            case JsonValueKind.True:
                return JsValue.True;
            case JsonValueKind.Number:
                return new(element.GetDouble());
            case JsonValueKind.String:
                return JsValue.FromString(element.GetString() ?? string.Empty);
            case JsonValueKind.Array:
            {
                var array = realm.CreateArray();
                uint index = 0;
                foreach (var item in element.EnumerateArray())
                    array.SetElement(index++, ConvertJsonElementToJsValue(realm, item));
                return JsValue.FromObject(array);
            }
            case JsonValueKind.Object:
            {
                var obj = new JsPlainObject(realm);
                foreach (var property in element.EnumerateObject())
                    obj.DefineDataProperty(property.Name, ConvertJsonElementToJsValue(realm, property.Value),
                        JsShapePropertyFlags.Open);
                return JsValue.FromObject(obj);
            }
            default:
                return JsValue.Undefined;
        }
    }

    private static JsArrayBufferObject CreateArrayBuffer(JsRealm realm, byte[] bytes)
    {
        var typedArray = new JsTypedArrayObject(realm, checked((uint)bytes.Length));
        for (uint i = 0; i < bytes.Length; i++)
            typedArray.SetElement(i, JsValue.FromInt32(bytes[i]));
        return typedArray.Buffer;
    }
}
