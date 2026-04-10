using Okojo.Objects;
using Okojo.Runtime;
using Wasmtime;

namespace Okojo.WebAssembly.Wasmtime;

internal sealed class WasmtimeMemoryWrapper(Memory memory, WasmMemoryType type) : IWasmMemory
{
    private readonly object syncRoot = new();
    private JsArrayBufferObject? cachedBuffer;

    public Memory Memory { get; } = memory;

    public WasmExternalKind Kind => WasmExternalKind.Memory;

    public WasmMemoryType Type { get; } = type;

    public long ByteLength => Memory.GetLength();

    public IntPtr Pointer => Memory.GetPointer();

    public Span<byte> GetSpan()
    {
        return Memory.GetSpan(0, checked((int)Memory.GetLength()));
    }

    public long Grow(long deltaPages)
    {
        var previous = Memory.Grow(deltaPages);
        cachedBuffer?.RefreshExternalBackingStore();
        return previous;
    }

    public JsArrayBufferObject WrapArrayBuffer(JsRealm realm)
    {
        if (cachedBuffer is not null && ReferenceEquals(cachedBuffer.Realm, realm))
            return cachedBuffer;

        var backingStore = new JsArrayBufferObject.DelegateExternalBufferBackingStore(
            () => Memory.GetSpan(0, checked((int)Memory.GetLength())),
            () => Memory.GetPointer(),
            syncRoot);

        uint? maxByteLength = Type.MaximumPages.HasValue
            ? checked((uint)(Type.MaximumPages.Value * Memory.PageSize))
            : null;

        cachedBuffer = Type.IsShared
            ? JsArrayBufferObject.CreateExternalShared(realm, backingStore, maxByteLength,
                realm.SharedArrayBufferPrototype)
            : JsArrayBufferObject.CreateExternal(realm, backingStore, maxByteLength, realm.ArrayBufferPrototype);

        return cachedBuffer;
    }
}
