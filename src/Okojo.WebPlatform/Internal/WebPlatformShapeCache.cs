using System.Diagnostics;
using System.Runtime.CompilerServices;
using Okojo.Runtime;

namespace Okojo.WebPlatform.Internal;

internal static class WebPlatformShapeCache
{
    private static readonly ConditionalWeakTable<JsRealm, CachedShapes> ShapesByRealm = new();

    public static CachedShapes For(JsRealm realm)
    {
        return ShapesByRealm.GetValue(realm, static realmValue => new(realmValue));
    }

    internal sealed class CachedShapes(JsRealm realm)
    {
        public HeaderShapeCache Headers { get; } = HeaderShapeCache.Create(realm);
        public ResponseShapeCache Response { get; } = ResponseShapeCache.Create(realm);
    }

    internal readonly record struct HeaderShapeCache(StaticNamedPropertyLayout Shape)
    {
        public const int GetSlot = 0;
        public const int HasSlot = 1;
        public const int KeysSlot = 2;
        public const int ValuesSlot = 3;
        public const int EntriesSlot = 4;

        public static HeaderShapeCache Create(JsRealm realm)
        {
            var shape = realm.EmptyShape;
            shape = shape.GetOrAddTransition(AtomTable.IdGet, JsShapePropertyFlags.Configurable, out var getInfo);
            shape = shape.GetOrAddTransition(AtomTable.IdHas, JsShapePropertyFlags.Configurable, out var hasInfo);
            shape = shape.GetOrAddTransition(AtomTable.IdKeys, JsShapePropertyFlags.Configurable, out var keysInfo);
            shape = shape.GetOrAddTransition(AtomTable.IdValues, JsShapePropertyFlags.Configurable, out var valuesInfo);
            shape = shape.GetOrAddTransition(AtomTable.IdEntries, JsShapePropertyFlags.Configurable,
                out var entriesInfo);

            Debug.Assert(getInfo.Slot == GetSlot);
            Debug.Assert(hasInfo.Slot == HasSlot);
            Debug.Assert(keysInfo.Slot == KeysSlot);
            Debug.Assert(valuesInfo.Slot == ValuesSlot);
            Debug.Assert(entriesInfo.Slot == EntriesSlot);
            return new(shape);
        }
    }

    internal readonly record struct ResponseShapeCache(StaticNamedPropertyLayout Shape)
    {
        public const int OkSlot = 0;
        public const int StatusSlot = 1;
        public const int StatusTextSlot = 2;
        public const int UrlSlot = 3;
        public const int HeadersSlot = 4;
        public const int TextSlot = 5;
        public const int JsonSlot = 6;
        public const int ArrayBufferSlot = 7;

        public static ResponseShapeCache Create(JsRealm realm)
        {
            var okAtom = realm.Atoms.InternNoCheck("ok");
            var statusAtom = realm.Atoms.InternNoCheck("status");
            var statusTextAtom = realm.Atoms.InternNoCheck("statusText");
            var urlAtom = realm.Atoms.InternNoCheck("url");
            var headersAtom = realm.Atoms.InternNoCheck("headers");
            var textAtom = realm.Atoms.InternNoCheck("text");
            var jsonAtom = realm.Atoms.InternNoCheck("json");
            var arrayBufferAtom = realm.Atoms.InternNoCheck("arrayBuffer");

            var shape = realm.EmptyShape;
            shape = shape.GetOrAddTransition(okAtom, JsShapePropertyFlags.Open, out var okInfo);
            shape = shape.GetOrAddTransition(statusAtom, JsShapePropertyFlags.Open, out var statusInfo);
            shape = shape.GetOrAddTransition(statusTextAtom, JsShapePropertyFlags.Open, out var statusTextInfo);
            shape = shape.GetOrAddTransition(urlAtom, JsShapePropertyFlags.Open, out var urlInfo);
            shape = shape.GetOrAddTransition(headersAtom, JsShapePropertyFlags.Open, out var headersInfo);
            shape = shape.GetOrAddTransition(textAtom, JsShapePropertyFlags.Configurable, out var textInfo);
            shape = shape.GetOrAddTransition(jsonAtom, JsShapePropertyFlags.Configurable, out var jsonInfo);
            shape = shape.GetOrAddTransition(arrayBufferAtom, JsShapePropertyFlags.Configurable,
                out var arrayBufferInfo);

            Debug.Assert(okInfo.Slot == OkSlot);
            Debug.Assert(statusInfo.Slot == StatusSlot);
            Debug.Assert(statusTextInfo.Slot == StatusTextSlot);
            Debug.Assert(urlInfo.Slot == UrlSlot);
            Debug.Assert(headersInfo.Slot == HeadersSlot);
            Debug.Assert(textInfo.Slot == TextSlot);
            Debug.Assert(jsonInfo.Slot == JsonSlot);
            Debug.Assert(arrayBufferInfo.Slot == ArrayBufferSlot);
            return new(shape);
        }
    }
}
