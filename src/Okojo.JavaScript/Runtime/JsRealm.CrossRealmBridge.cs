namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private JsValue BridgeIntoThisRealm(
        in JsValue sourceValue,
        Dictionary<JsObject, JsValue>? visited = null
    )
    {
        if (
            sourceValue.IsUndefined
            || sourceValue.IsNull
            || sourceValue.IsBool
            || sourceValue.IsInt32
            || sourceValue.IsFloat64
            || sourceValue.IsString
            || sourceValue.IsSymbol
        )
            return sourceValue;

        if (!sourceValue.TryGetObject(out var sourceObj))
            return sourceValue;

        var sourceRealm = sourceObj.Realm;
        if (ReferenceEquals(sourceRealm, this))
            return sourceValue;

        visited ??= new(ReferenceEqualityComparer.Instance);
        if (visited.TryGetValue(sourceObj, out var existing))
            return existing;

        if (sourceObj is JsFunction sourceFn)
        {
            var proxy = new JsHostFunction(
                this,
                static (in info) =>
                {
                    var realm = info.Realm;
                    var args = info.Arguments;
                    var callee = (JsHostFunction)info.Function;
                    var data = (CrossRealmFunctionProxyData)callee.UserData!;
                    var forwarded = args.Length == 0 ? [] : new JsValue[args.Length];
                    for (var i = 0; i < args.Length; i++)
                        forwarded[i] = data.SourceRealm.BridgeIntoThisRealm(args[i]);

                    var result = data.SourceRealm.InvokeFunction(
                        data.SourceFunction,
                        JsValue.Undefined,
                        forwarded
                    );
                    return realm.BridgeIntoThisRealm(result);
                },
                sourceFn.Name ?? string.Empty,
                sourceFn.Length
            )
            {
                UserData = new CrossRealmFunctionProxyData(sourceRealm, sourceFn),
            };

            var bridgedFn = JsValue.FromObject(proxy);
            visited[sourceObj] = bridgedFn;
            return bridgedFn;
        }

        if (sourceObj is JsArray sourceArray)
        {
            var targetArray = CreateArrayObject();
            var bridged = JsValue.FromObject(targetArray);
            visited[sourceObj] = bridged;
            for (uint i = 0; i < sourceArray.Length; i++)
            {
                if (!sourceArray.TryGetElement(i, out var item))
                    continue;
                targetArray.SetElement(i, BridgeIntoThisRealm(item, visited));
            }

            return bridged;
        }

        var targetObj = new JsPlainObject(this);
        var bridgedObj = JsValue.FromObject(targetObj);
        visited[sourceObj] = bridgedObj;
        var namedAtoms = RentScratchList<int>();
        try
        {
            sourceObj.CollectOwnNamedPropertyAtoms(sourceRealm, namedAtoms, false);
            foreach (var sourceAtom in namedAtoms)
            {
                if (sourceAtom < 0)
                    continue;
                if (!sourceObj.TryGetPropertyAtom(sourceRealm, sourceAtom, out var value, out _))
                    continue;

                var key = sourceRealm.Atoms.AtomToString(sourceAtom);
                if (TryGetArrayIndexFromCanonicalString(key, out var idx))
                {
                    targetObj.SetElement(idx, BridgeIntoThisRealm(value, visited));
                }
                else
                {
                    var targetAtom = Atoms.InternNoCheck(key);
                    targetObj.DefineDataPropertyAtom(
                        this,
                        targetAtom,
                        BridgeIntoThisRealm(value, visited),
                        JsShapePropertyFlags.Open
                    );
                }
            }
        }
        finally
        {
            ReturnScratchList(namedAtoms);
        }

        return bridgedObj;
    }

    internal JsValue BridgeFromOtherRealm(in JsValue sourceValue)
    {
        return BridgeIntoThisRealm(sourceValue);
    }

    private sealed class CrossRealmFunctionProxyData(JsRealm sourceRealm, JsFunction sourceFunction)
    {
        public JsRealm SourceRealm { get; } = sourceRealm;
        public JsFunction SourceFunction { get; } = sourceFunction;
    }
}
