using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

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
