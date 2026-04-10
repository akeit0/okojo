namespace Okojo.Runtime;

public readonly struct SlotInfo
{
    internal readonly int Value;

    public SlotInfo(int slot, JsShapePropertyFlags flags)
    {
        Value = (slot & 0x00FF_FFFF) | ((byte)flags << 24);
    }

    public int Slot => Value & 0x00FF_FFFF;
    public JsShapePropertyFlags Flags => (JsShapePropertyFlags)((uint)Value >> 24);
    public int AccessorSetterSlot => Slot + 1; // valid when Flags.HasFlag(BothAccessor)

    internal SlotInfo(int value)
    {
        Value = value;
    }

    public static SlotInfo Invalid => new(-1);
    public bool IsValid => Value >= 0;
}
