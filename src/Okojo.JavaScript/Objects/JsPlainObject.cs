using System.Runtime.CompilerServices;

namespace Okojo.JavaScript.Objects;

public class JsPlainObject : JsObject
{
    public JsPlainObject(
        JsRealm realm,
        bool assignDefaultPrototype = true,
        bool useDictionaryMode = false
    )
        : base(realm, useDictionaryMode)
    {
        if (assignDefaultPrototype)
            Prototype = realm.ObjectPrototype;
    }

    public JsPlainObject(StaticNamedPropertyLayout shape, bool assignDefaultPrototype = true)
        : base(shape)
    {
        if (assignDefaultPrototype)
            Prototype = shape.Owner.ObjectPrototype;
    }

    internal JsPlainObject(StaticNamedPropertyLayout shape, JsObject? prototype, int slotCapacity)
        : base(shape, prototype, slotCapacity) { }

    private protected JsPlainObject(
        StaticNamedPropertyLayout shape,
        JsObject? prototype,
        byte inlineSlotCapacity,
        bool inlineStorage
    )
        : base(shape, prototype, inlineSlotCapacity, inlineStorage) { }

    // Host subclasses can override IsExtensible. Only engine-owned layouts can
    // use the base flag in place of that virtual property.
    internal bool CanApplyCachedTransition =>
        IsExtensibleFlag && (InlineSlotCapacity != 0 || GetType() == typeof(JsPlainObject));

    /// <summary>
    /// Creates an ordinary object sized for <paramref name="slotCount"/> named slots, storing
    /// small objects' slots inline (one allocation, no separate slot array).
    /// </summary>
    internal static JsPlainObject CreateWithSlots(
        StaticNamedPropertyLayout shape,
        JsObject? prototype,
        int slotCount
    )
    {
        return slotCount switch
        {
            <= 0 => new JsPlainObject(shape, prototype, 0),
            <= 2 => new JsPlainObjectInline2(shape, prototype),
            <= 4 => new JsPlainObjectInline4(shape, prototype),
            <= 8 => new JsPlainObjectInline8(shape, prototype),
            _ => new JsPlainObject(shape, prototype, slotCount),
        };
    }

    public override string ToString()
    {
        return FormatOwnEnumerablePropertiesForDisplay();
    }

    internal override string FormatForDisplay(int? indentSize, int depth, HashSet<JsObject> visited)
    {
        if (indentSize is null || indentSize <= 0)
            return FormatOwnEnumerablePropertiesForDisplay();
        return FormatOwnEnumerablePropertiesForDisplay(indentSize, depth, visited);
    }
}

[InlineArray(2)]
internal struct JsInlineSlots2
{
    private JsValue element0;
}

[InlineArray(4)]
internal struct JsInlineSlots4
{
    private JsValue element0;
}

[InlineArray(8)]
internal struct JsInlineSlots8
{
    private JsValue element0;
}

internal sealed class JsPlainObjectInline2 : JsPlainObject
{
    internal JsInlineSlots2 InlineSlots;

    internal JsPlainObjectInline2(StaticNamedPropertyLayout shape, JsObject? prototype)
        : base(shape, prototype, 2, true) { }
}

internal sealed class JsPlainObjectInline4 : JsPlainObject
{
    internal JsInlineSlots4 InlineSlots;

    internal JsPlainObjectInline4(StaticNamedPropertyLayout shape, JsObject? prototype)
        : base(shape, prototype, 4, true) { }
}

internal sealed class JsPlainObjectInline8 : JsPlainObject
{
    internal JsInlineSlots8 InlineSlots;

    internal JsPlainObjectInline8(StaticNamedPropertyLayout shape, JsObject? prototype)
        : base(shape, prototype, 8, true) { }
}
