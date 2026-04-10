namespace Okojo.WebAssembly;

public interface IWasmInstance : IDisposable
{
    IReadOnlyDictionary<string, IWasmExtern> Exports { get; }
    bool TryGetExport(string name, out IWasmExtern wasmExtern);
}
