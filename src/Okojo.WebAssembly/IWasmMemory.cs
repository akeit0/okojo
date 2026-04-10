using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebAssembly;

public interface IWasmMemory : IWasmExtern
{
    WasmMemoryType Type { get; }
    long ByteLength { get; }
    IntPtr Pointer { get; }
    Span<byte> GetSpan();
    long Grow(long deltaPages);
    JsArrayBufferObject WrapArrayBuffer(JsRealm realm);
}
