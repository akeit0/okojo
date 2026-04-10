using System.Runtime.CompilerServices;

namespace Okojo.Objects;

public interface ILazyHostMethodProvider
{
    bool TryGetOrCreateLazyHostMethod(JsRealm realm, int atom, out JsValue method);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void GetOrCreateLazyHostMethod(int atom, JsObject provider, out JsValue method)
    {
        if (provider is ILazyHostMethodProvider lazyProvider &&
            lazyProvider.TryGetOrCreateLazyHostMethod(provider.Realm, atom, out method))
            return;

        method = JsValue.Undefined;
    }
}

public sealed class JsHostObject : JsObject, ILazyHostMethodProvider
{
    private readonly HostRealmLayoutInfo layoutInfo;
    private JsValue hostIteratorMethod = JsValue.TheHole;

    internal JsHostObject(JsRealm realm, object data, HostTypeDescriptor descriptor)
        : this(realm, data, descriptor, descriptor.GetOrCreateRealmLayout(realm))
    {
    }

    private JsHostObject(JsRealm realm, object data, HostTypeDescriptor descriptor,
        HostRealmLayoutInfo layoutInfo)
        : base(layoutInfo.Layout)
    {
        this.layoutInfo = layoutInfo;
        Data = data;
        Descriptor = descriptor;
        Prototype = realm.ObjectPrototype;

        var template = layoutInfo.SlotTemplate;
        if (template.Length != 0)
            Array.Copy(template, Slots, template.Length);
    }

    private string DisplayTag => ClrDisplay.FormatTypeName(Descriptor.ClrType);

    public object Data { get; }
    internal HostTypeDescriptor Descriptor { get; }

    public bool TryGetOrCreateLazyHostMethod(JsRealm realm, int atom, out JsValue method)
    {
        if (layoutInfo.TryGetOrCreateMethodValue(realm, atom, out method)) return true;

        return false;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom == IdSymbolToStringTag)
        {
            value = JsValue.FromString(DisplayTag);
            return true;
        }

        if (atom == IdSymbolIterator && Descriptor.SupportsSyncIteration)
        {
            value = GetOrCreateHostIteratorMethod(realm);
            return true;
        }

        return base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        if (atom == IdSymbolToStringTag)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(JsValue.FromString(DisplayTag), false, false,
                    true)
                : default;
            return true;
        }

        if (atom == IdSymbolIterator && Descriptor.SupportsSyncIteration)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(GetOrCreateHostIteratorMethod(realm), false, false,
                    true)
                : default;
            return true;
        }

        if (NamedPropertyLayout.TryGetSlotInfo(atom, out var foundInfo))
        {
            if (needDescriptor)
            {
                _ = MaterializeLazyMethodValueIfNeeded(realm, atom, foundInfo, Slots[foundInfo.Slot]);

                descriptor = BuildNamedDescriptorBySlotInfo(foundInfo);
            }
            else
            {
                descriptor = default;
            }

            return true;
        }

        descriptor = default;
        return false;
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
        if (!enumerableOnly)
        {
            if (Descriptor.SupportsSyncIteration)
                atomsOut.Add(IdSymbolIterator);
            atomsOut.Add(IdSymbolToStringTag);
        }
    }

    internal override bool TryGetElementWithReceiver(JsRealm realm, JsObject receiver, uint index,
        out JsValue value)
    {
        var indexer = Descriptor.Indexer;
        if (indexer is not null)
        {
            var result = indexer.Getter(realm, Data, index);
            if (result.Success)
            {
                value = result.Value;
                return true;
            }
        }

        return base.TryGetElementWithReceiver(realm, receiver, index, out value);
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        var indexer = Descriptor.Indexer;
        if (ReferenceEquals(this, receiver) && indexer?.Setter is not null)
            if (indexer.Setter(realm, Data, index, value))
                return true;

        return base.SetElementWithReceiver(realm, receiver, index, value);
    }

    internal override bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        var indexer = Descriptor.Indexer;
        if (indexer is not null)
        {
            var result = indexer.Getter(Realm, Data, index);
            hadOwnElement = result.Success;
            if (!result.Success || indexer.Setter is null)
                return false;
            return indexer.Setter(Realm, Data, index, value);
        }

        return base.TrySetOwnElement(index, value, out hadOwnElement);
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        var indexer = Descriptor.Indexer;
        if (indexer is not null)
        {
            var result = indexer.Getter(Realm, Data, index);
            if (result.Success)
            {
                descriptor = PropertyDescriptor.Data(result.Value, indexer.Setter is not null,
                    true,
                    true);
                return true;
            }
        }

        return base.TryGetOwnElementDescriptor(index, out descriptor);
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        Descriptor.Indexer?.CollectOwnIndices?.Invoke(Data, indicesOut);
        base.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }

    public override string ToString()
    {
        return $"[{DisplayTag}]";
    }

    private JsValue MaterializeLazyMethodValueIfNeeded(JsRealm realm, int atom, in SlotInfo slotInfo,
        in JsValue value)
    {
        if (!value.IsTheHole)
            return value;

        if (layoutInfo.TryGetOrCreateMethodValue(realm, atom, out var methodValue))
        {
            Slots[slotInfo.Slot] = methodValue;
            return methodValue;
        }

        return value;
    }

    private JsValue GetOrCreateHostIteratorMethod(JsRealm realm)
    {
        if (!hostIteratorMethod.IsTheHole)
            return hostIteratorMethod;

        var function = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsHostObject host)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "[Symbol.iterator] called on incompatible receiver");

            return JsValue.FromObject(new JsClrEnumeratorObject(info.Realm, host));
        }, "[Symbol.iterator]", 0);

        hostIteratorMethod = JsValue.FromObject(function);
        return hostIteratorMethod;
    }
}
