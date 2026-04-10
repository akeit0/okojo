namespace Okojo.Runtime;

internal sealed class JsDefaultHostMessageSerializer : IHostMessageSerializer
{
    public static readonly JsDefaultHostMessageSerializer Shared = new();

    private JsDefaultHostMessageSerializer()
    {
    }

    public object? CloneCrossAgentPayload(object? payload)
    {
        return payload switch
        {
            null => null,
            string s => s,
            bool b => b,
            byte n => n,
            sbyte n => n,
            short n => n,
            ushort n => n,
            int n => n,
            uint n => n,
            long n => n,
            ulong n => n,
            float n => n,
            double n => n,
            decimal n => n,
            JsArrayBufferObject.SharedBufferStorage storage => storage,
            object?[] arr => CloneArray(arr),
            List<object?> list => CloneList(list),
            Dictionary<string, object?> dict => CloneDictionary(dict),
            _ => new UnsupportedMessagePayload(payload.GetType().FullName ?? "<unknown>")
        };
    }

    public object? SerializeOutgoing(JsRealm realm, in JsValue value)
    {
        if (value.IsUndefined || value.IsNull)
            return null;
        if (value.IsBool)
            return value.IsTrue;
        if (value.IsString)
            return value.AsString();
        if (value.IsInt32)
            return value.Int32Value;
        if (value.IsFloat64)
            return value.Float64Value;
        if (value.TryGetObject(out var obj) && obj is JsArrayBufferObject { IsShared: true } sharedBuffer)
            return sharedBuffer.GetSharedStorage();

        throw new JsRuntimeException(JsErrorKind.TypeError,
            "postMessage currently supports primitive payloads only",
            "POSTMESSAGE_UNSUPPORTED_PAYLOAD");
    }

    public JsValue DeserializeIncoming(JsRealm realm, object? payload)
    {
        return payload switch
        {
            null => JsValue.Null,
            string s => JsValue.FromString(s),
            bool b => b ? JsValue.True : JsValue.False,
            byte n => JsValue.FromInt32(n),
            sbyte n => JsValue.FromInt32(n),
            short n => JsValue.FromInt32(n),
            ushort n => JsValue.FromInt32(n),
            int n => JsValue.FromInt32(n),
            uint n => n <= int.MaxValue ? JsValue.FromInt32((int)n) : new((double)n),
            long n => n is >= int.MinValue and <= int.MaxValue ? JsValue.FromInt32((int)n) : new(n),
            ulong n => n <= int.MaxValue ? JsValue.FromInt32((int)n) : new((double)n),
            float n => new(n),
            double n => new(n),
            decimal n => new((double)n),
            JsArrayBufferObject.SharedBufferStorage storage =>
                JsValue.FromObject(new JsArrayBufferObject(realm, storage, realm.SharedArrayBufferPrototype)),
            UnsupportedMessagePayload unsupported => throw new NotSupportedException(
                $"Unsupported message payload type: {unsupported.TypeName}"),
            object?[] arr => JsValue.FromObject(FromHostArray(realm, arr)),
            List<object?> list => JsValue.FromObject(FromHostList(realm, list)),
            Dictionary<string, object?> dict => JsValue.FromObject(FromHostDictionary(realm, dict)),
            _ => throw new NotSupportedException($"Unsupported message payload type: {payload.GetType().FullName}")
        };
    }

    private static object?[] CloneArray(object?[] source)
    {
        var clone = new object?[source.Length];
        for (var i = 0; i < source.Length; i++)
            clone[i] = Shared.CloneCrossAgentPayload(source[i]);
        return clone;
    }

    private static List<object?> CloneList(List<object?> source)
    {
        var clone = new List<object?>(source.Count);
        for (var i = 0; i < source.Count; i++)
            clone.Add(Shared.CloneCrossAgentPayload(source[i]));
        return clone;
    }

    private static Dictionary<string, object?> CloneDictionary(Dictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (var pair in source)
            clone[pair.Key] = Shared.CloneCrossAgentPayload(pair.Value);
        return clone;
    }

    private static JsArray FromHostArray(JsRealm realm, object?[] arr)
    {
        var outArr = realm.CreateArrayObject();
        for (uint i = 0; i < arr.Length; i++)
            outArr.SetElement(i, Shared.DeserializeIncoming(realm, arr[i]));
        return outArr;
    }

    private static JsArray FromHostList(JsRealm realm, List<object?> list)
    {
        var outArr = realm.CreateArrayObject();
        for (uint i = 0; i < list.Count; i++)
            outArr.SetElement(i, Shared.DeserializeIncoming(realm, list[(int)i]));
        return outArr;
    }

    private static JsPlainObject FromHostDictionary(JsRealm realm, Dictionary<string, object?> dict)
    {
        var obj = new JsPlainObject(realm);
        foreach (var pair in dict)
        {
            var atom = realm.Atoms.InternNoCheck(pair.Key);
            obj.DefineDataPropertyAtom(realm, atom, Shared.DeserializeIncoming(realm, pair.Value),
                JsShapePropertyFlags.Open);
        }

        return obj;
    }
}
