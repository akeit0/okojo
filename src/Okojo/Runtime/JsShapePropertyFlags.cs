namespace Okojo.Runtime;

[Flags]
public enum JsShapePropertyFlags : byte
{
    None = 0,
    Enumerable = 1 << 0,
    Configurable = 1 << 1,
    Writable = 1 << 2,
    HasGetter = 1 << 3,
    HasSetter = 1 << 4,
    BothAccessor = HasGetter | HasSetter,
    Open = Writable | Enumerable | Configurable
}
